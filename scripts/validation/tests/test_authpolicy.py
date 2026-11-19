import importlib
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from validation import authpolicy
from validation.common import ValidationFailure
from validation.tests.test_common import package


class AuthenticationPolicyBoundaryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = package(self.root / "runtime")
        self.baseline = package(self.root / "baseline")
        (self.baseline / "Readarr.Core.dll").write_bytes(b"different synthetic baseline")

    def test_import_has_no_application_or_database_side_effects(self):
        with patch.object(subprocess, "Popen") as process, patch.object(authpolicy.datastore.OwnedPostgres, "start") as postgres:
            importlib.reload(authpolicy)
        process.assert_not_called()
        postgres.assert_not_called()

    def test_a_distinct_baseline_is_required_before_creating_outputs(self):
        for baseline in (None, self.runtime):
            with self.subTest(baseline=baseline), patch.object(authpolicy.datastore.OwnedPostgres, "start") as start:
                with self.assertRaises(ValidationFailure):
                    authpolicy.run(self.runtime, self.root / "output", baseline_runtime=baseline)
                self.assertFalse((self.root / "output").exists())
                start.assert_not_called()

    def test_errors_remain_secret_safe_and_cleanup_still_runs(self):
        with (patch.object(authpolicy, "ensure_host_compatible"),
              patch.object(authpolicy, "baseline_sqlite", side_effect=RuntimeError("private-password-canary")),
              patch.object(authpolicy.datastore.OwnedPostgres, "close", return_value=True) as close):
            report = authpolicy.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        self.assertFalse(report["completed"])
        self.assertEqual(report["failure"], {"stage": "baseline_sqlite", "exceptionType": "RuntimeError"})
        self.assertNotIn("private-password-canary", json.dumps(report))
        self.assertTrue(report["checks"]["candidate_package_unchanged"])
        self.assertTrue(report["checks"]["baseline_package_unchanged"])
        close.assert_called_once()
        self.assertEqual((self.root / "output").stat().st_mode & 0o777, 0o700)

    def test_interrupt_joins_application_and_postgres_cleanup(self):
        fixture = SimpleNamespace(process=object(), forced_stops=0)
        fixture.cleanup = Mock(side_effect=lambda: setattr(fixture, "process", None))

        def interrupted(baseline, output, fixtures, check):
            fixtures.append(fixture)
            raise KeyboardInterrupt()

        with (patch.object(authpolicy, "ensure_host_compatible"),
              patch.object(authpolicy, "baseline_sqlite", side_effect=interrupted),
              patch.object(authpolicy.datastore.OwnedPostgres, "close", return_value=True) as close,
              self.assertRaises(KeyboardInterrupt)):
            authpolicy.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        fixture.cleanup.assert_called_once()
        close.assert_called_once()
        self.assertIsNone(fixture.process)

    def test_application_cleanup_failure_does_not_skip_database_cleanup(self):
        fixture = SimpleNamespace(process=object(), forced_stops=0, cleanup=Mock(side_effect=OSError("private-error")))

        def failed(baseline, output, fixtures, check):
            fixtures.append(fixture)
            raise ValidationFailure("owned_failure")

        with (patch.object(authpolicy, "ensure_host_compatible"),
              patch.object(authpolicy, "baseline_sqlite", side_effect=failed),
              patch.object(authpolicy.datastore.OwnedPostgres, "close", return_value=True) as close):
            report = authpolicy.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        self.assertFalse(report["checks"]["all_owned_application_processes_stopped"])
        self.assertTrue(report["checks"]["owned_postgres_container_and_socket_removed"])
        close.assert_called_once()

    def test_changed_input_fails_even_after_an_earlier_execution_failure(self):
        def mutated(*args):
            (self.runtime / "Readarr.Core.dll").write_bytes(b"changed synthetic package")
            raise ValidationFailure("owned_failure")
        with (patch.object(authpolicy, "ensure_host_compatible"),
              patch.object(authpolicy, "baseline_sqlite", side_effect=mutated),
              patch.object(authpolicy.datastore.OwnedPostgres, "close", return_value=True)):
            report = authpolicy.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        self.assertFalse(report["checks"]["candidate_package_unchanged"])

    def test_recovery_passwords_use_stdin_not_arguments_or_environment(self):
        home = self.root / "home"
        home.mkdir()
        temporary = home / "tmp"
        temporary.mkdir()
        fixture = SimpleNamespace(runtime=self.runtime, home=home, private_tmp=temporary)
        process = Mock(pid=123456789, returncode=0)
        process.communicate.return_value = (b"completed", None)
        process.poll.return_value = 0
        secret = "private-recovery-canary"
        with patch.object(authpolicy.subprocess, "Popen", return_value=process) as popen, patch.object(authpolicy.os, "killpg", side_effect=ProcessLookupError):
            code, output = authpolicy.recovery(fixture, "owned-user", secret, secret)
        self.assertEqual(code, 0)
        self.assertEqual(output, b"completed")
        arguments = popen.call_args.args[0]
        self.assertNotIn(secret, " ".join(arguments))
        self.assertNotIn(secret, json.dumps(popen.call_args.kwargs["env"]))
        self.assertTrue(popen.call_args.kwargs["start_new_session"])
        self.assertEqual(process.communicate.call_args.args[0], ("owned-user\n" + secret + "\n" + secret + "\n").encode())

    def test_recovery_timeout_still_kills_and_joins_its_owned_process(self):
        home = self.root / "home"
        home.mkdir()
        temporary = home / "tmp"
        temporary.mkdir()
        fixture = SimpleNamespace(runtime=self.runtime, home=home, private_tmp=temporary)
        process = Mock(pid=123456789)
        process.communicate.side_effect = [subprocess.TimeoutExpired("owned command", 1), (b"", None)]
        process.poll.return_value = None
        with (patch.object(authpolicy.subprocess, "Popen", return_value=process),
              patch.object(authpolicy.os, "killpg") as kill,
              self.assertRaises(subprocess.TimeoutExpired)):
            authpolicy.recovery(fixture, "owned-user", "private-recovery-canary", "private-recovery-canary", timeout=1)
        kill.assert_called_once_with(process.pid, authpolicy.signal.SIGKILL)
        self.assertEqual(process.communicate.call_count, 2)

    def test_handoff_prompt_failure_still_joins_its_owned_recovery(self):
        home = self.root / "home"
        home.mkdir()
        (home / "config.xml").write_bytes(b"synthetic configuration")
        fixture = SimpleNamespace(process=None, home=home)
        storage = SimpleNamespace(users=Mock(return_value={}))
        process = Mock()
        with (patch.object(authpolicy, "recovery_process", return_value=process),
              patch.object(authpolicy, "wait_for_recovery_prompt", side_effect=ValidationFailure("owned_prompt_failure")),
              patch.object(authpolicy, "finish_recovery") as finish,
              self.assertRaises(ValidationFailure)):
            authpolicy.recovery_startup_handoff(fixture, storage, Mock(), "owned")
        finish.assert_called_once_with(process)

    def test_handoff_requires_a_stopped_owned_home_before_starting_recovery(self):
        fixture = SimpleNamespace(process=object())
        with patch.object(authpolicy, "recovery_process") as start, self.assertRaises(ValidationFailure):
            authpolicy.recovery_startup_handoff(fixture, Mock(), Mock(), "owned")
        start.assert_not_called()


if __name__ == "__main__":
    unittest.main()
