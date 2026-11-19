"""Safety boundaries for the explicit packaged access-policy proof."""

import importlib
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from validation import accesspolicy
from validation.common import ValidationFailure
from validation.tests.test_common import package


class AccessPolicyBoundaryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = package(self.root / "runtime")

    def test_import_does_not_start_application_or_updater_processes(self):
        with patch.object(subprocess, "Popen") as process:
            importlib.reload(accesspolicy)
        process.assert_not_called()

    def test_runtime_and_evidence_must_be_disjoint_before_starting_processes(self):
        with patch.object(subprocess, "Popen") as process:
            with self.assertRaises(ValidationFailure):
                accesspolicy.run(self.runtime, self.runtime / "results")
        process.assert_not_called()
        self.assertFalse((self.runtime / "results").exists())

    def test_errors_remain_secret_safe_and_package_fingerprint_is_checked(self):
        with (patch.object(accesspolicy, "ensure_host_compatible"),
              patch.object(accesspolicy, "exercise", side_effect=RuntimeError("private-password-canary"))):
            report = accesspolicy.run(self.runtime, self.root / "output")
        self.assertFalse(report["completed"])
        self.assertEqual(report["failure"], {"stage": "access_policy", "exceptionType": "RuntimeError"})
        self.assertNotIn("private-password-canary", json.dumps(report))
        self.assertTrue(report["checks"]["candidate_package_unchanged"])
        self.assertEqual((self.root / "output").stat().st_mode & 0o777, 0o700)

    def test_interrupt_still_joins_all_owned_process_cleanup(self):
        fixtures = [SimpleNamespace(process=object(), forced_stops=0),
                    SimpleNamespace(process=object(), forced_stops=0)]
        for fixture in fixtures:
            fixture.cleanup = Mock(side_effect=lambda current=fixture: setattr(current, "process", None))

        def interrupted(runtime, output, owned, check):
            owned.extend(fixtures)
            raise KeyboardInterrupt()

        with (patch.object(accesspolicy, "ensure_host_compatible"),
              patch.object(accesspolicy, "exercise", side_effect=interrupted),
              self.assertRaises(KeyboardInterrupt)):
            accesspolicy.run(self.runtime, self.root / "output")
        for fixture in fixtures:
            fixture.cleanup.assert_called_once()
            self.assertIsNone(fixture.process)

    def test_one_cleanup_failure_does_not_skip_remaining_owned_processes(self):
        first = SimpleNamespace(process=object(), forced_stops=0, cleanup=Mock(side_effect=OSError("private-error")))
        second = SimpleNamespace(process=object(), forced_stops=0)
        second.cleanup = Mock(side_effect=lambda: setattr(second, "process", None))

        def failed(runtime, output, owned, check):
            owned.extend([second, first])
            raise ValidationFailure("owned_failure")

        with (patch.object(accesspolicy, "ensure_host_compatible"),
              patch.object(accesspolicy, "exercise", side_effect=failed)):
            report = accesspolicy.run(self.runtime, self.root / "output")
        self.assertFalse(report["checks"]["all_owned_application_processes_stopped"])
        first.cleanup.assert_called_once()
        second.cleanup.assert_called_once()
        self.assertIsNone(second.process)
        self.assertNotIn("private-error", json.dumps(report))

    def test_mutated_package_fails_even_after_an_earlier_execution_failure(self):
        def changed(*args):
            (self.runtime / "Readarr.Core.dll").write_bytes(b"changed synthetic package")
            raise ValidationFailure("owned_failure")
        with (patch.object(accesspolicy, "ensure_host_compatible"),
              patch.object(accesspolicy, "exercise", side_effect=changed)):
            report = accesspolicy.run(self.runtime, self.root / "output")
        self.assertFalse(report["checks"]["candidate_package_unchanged"])

    def test_a_health_timeout_is_not_misreported_as_invalid_startup_rejection(self):
        fixture = SimpleNamespace(process=Mock(returncode=None),
                                  start=Mock(side_effect=ValidationFailure("owned_application_startup_timed_out")),
                                  stop=Mock())
        fixture.process.poll.return_value = None
        self.assertFalse(accesspolicy.rejected_start(fixture))
        fixture.stop.assert_called_once()

    def test_invalid_startup_requires_a_nonzero_process_exit_and_still_cleans_up(self):
        fixture = SimpleNamespace(process=Mock(returncode=1),
                                  start=Mock(side_effect=ValidationFailure("owned_application_exited_before_healthy")),
                                  stop=Mock())
        fixture.process.poll.return_value = 1
        self.assertTrue(accesspolicy.rejected_start(fixture))
        fixture.stop.assert_called_once()

    def test_successful_startup_cannot_count_as_a_configuration_rejection(self):
        fixture = SimpleNamespace(start=Mock(), stop=Mock())
        self.assertFalse(accesspolicy.rejected_start(fixture))
        fixture.stop.assert_called_once()
