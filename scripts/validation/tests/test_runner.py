import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import signal
import tempfile
import types
import unittest
from unittest.mock import patch

import validation
from validation.tests.test_common import package


SPEC = importlib.util.spec_from_file_location("local_validation_cli", Path(__file__).parents[2] / "validate-local.py")
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class RunnerResultTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.runtime = package(self.root / "runtime")
        self.modules = {"validation." + name: types.ModuleType("validation." + name)
                        for name in ("app", "authpolicy", "cancellation", "datastore", "docker", "logstorage", "media", "package", "postgresconnections", "restore", "sqlite", "upgrade")}
        self.output = self.root / "results"

    @contextlib.contextmanager
    def mocked_modules(self):
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.dict(sys.modules, self.modules))
            for name, module in self.modules.items():
                stack.enter_context(patch.object(validation, name.rsplit(".", 1)[1], module, create=True))
            yield

    def invoke(self, callback):
        self.modules["validation.package"].run = callback
        console = io.StringIO()
        with self.mocked_modules(), contextlib.redirect_stdout(console), contextlib.redirect_stderr(console):
            status = runner.main(["--runtime", str(self.runtime), "--output", str(self.output), "--checks", "package"])
        return status, json.loads((self.output / "summary.json").read_text()), console.getvalue()

    def test_a_failed_or_incomplete_check_never_returns_success(self):
        status, report, _ = self.invoke(lambda *args: {"checks": {"expected": False}, "passed": 0, "failed": 1, "completed": False})
        self.assertEqual(status, 1)
        self.assertFalse(report["completed"])

    def test_report_preserves_requested_scope_and_package_fingerprint(self):
        status, report, _ = self.invoke(lambda *args: {"checks": {"expected": True}, "passed": 1, "failed": 0, "completed": True})
        self.assertEqual(status, 0)
        self.assertTrue(report["completed"])
        self.assertEqual(report["checksRequested"], ["package"])
        self.assertIn("upgrade", report["notRequested"])
        self.assertTrue(report["results"]["unchanged_candidate"]["checks"]["input_package_unchanged"])
        self.assertEqual(len(report["inputFingerprints"]["candidate"]), 64)

    def test_explicit_logstorage_check_uses_candidate_without_requiring_a_baseline(self):
        calls = []
        self.modules["validation.logstorage"].run = lambda *args: (
            calls.append(args) or {"checks": {"owned": True}, "passed": 1, "failed": 0, "completed": True})
        console = io.StringIO()
        with (self.mocked_modules(), patch.object(runner.common, "ensure_host_compatible"),
              contextlib.redirect_stdout(console), contextlib.redirect_stderr(console)):
            status = runner.main(["--runtime", str(self.runtime), "--output", str(self.output), "--checks", "logstorage"])
        self.assertEqual(status, 0)
        self.assertEqual(calls, [(self.runtime.resolve(), self.output / "logstorage")])
        report = json.loads((self.output / "summary.json").read_text())
        self.assertEqual(report["checksRequested"], ["logstorage"])
        self.assertIn("datastore", report["notRequested"])

    def test_postgres_connection_check_requires_and_passes_an_explicit_baseline(self):
        baseline = package(self.root / "baseline")
        calls = []
        self.modules["validation.postgresconnections"].run = lambda *args, **kwargs: (
            calls.append((args, kwargs)) or {"checks": {"owned": True}, "passed": 1, "failed": 0, "completed": True})
        console = io.StringIO()
        with (self.mocked_modules(), patch.object(runner.common, "ensure_host_compatible"),
              contextlib.redirect_stdout(console), contextlib.redirect_stderr(console)):
            with self.assertRaises(SystemExit):
                runner.main(["--runtime", str(self.runtime), "--output", str(self.output), "--checks", "postgresconnections"])
            self.assertFalse(self.output.exists())
            status = runner.main(["--runtime", str(self.runtime), "--baseline-runtime", str(baseline),
                                  "--output", str(self.output), "--checks", "postgresconnections"])
        self.assertEqual(status, 0)
        self.assertEqual(calls, [((self.runtime.resolve(), self.output / "postgresconnections"),
                                  {"baseline_runtime": baseline.resolve()})])

    def test_authpolicy_requires_and_receives_owned_baseline_argument(self):
        baseline = package(self.root / "baseline")
        calls = []
        self.modules["validation.authpolicy"].run = lambda *args, **kwargs: (
            calls.append((args, kwargs)) or {"checks": {"owned": True}, "passed": 1, "failed": 0, "completed": True})
        console = io.StringIO()
        with (self.mocked_modules(), patch.object(runner.common, "ensure_host_compatible"),
              contextlib.redirect_stdout(console), contextlib.redirect_stderr(console)):
            with self.assertRaises(SystemExit):
                runner.main(["--runtime", str(self.runtime), "--output", str(self.output), "--checks", "authpolicy"])
            self.assertFalse(self.output.exists())
            status = runner.main(["--runtime", str(self.runtime), "--baseline-runtime", str(baseline),
                                  "--output", str(self.output), "--checks", "authpolicy"])
        self.assertEqual(status, 0)
        self.assertEqual(calls, [((self.runtime.resolve(), self.output / "authpolicy"),
                                  {"baseline_runtime": baseline.resolve()})])

    def test_candidate_mutation_fails_even_when_check_claims_success(self):
        def mutate(*args):
            (self.runtime / "Readarr.Core.dll").write_bytes(b"changed synthetic candidate")
            return {"checks": {"executed": True}, "passed": 1, "failed": 0, "completed": True}
        status, report, _ = self.invoke(mutate)
        self.assertEqual(status, 1)
        self.assertFalse(report["results"]["unchanged_candidate"]["checks"]["input_package_unchanged"])

    def test_exception_payload_is_not_written_to_console_or_report(self):
        def fail(*args):
            raise RuntimeError("synthetic-secret-that-must-not-be-printed")
        status, report, console = self.invoke(fail)
        self.assertEqual(status, 1)
        self.assertNotIn("synthetic-secret", console + json.dumps(report))
        self.assertEqual(report["results"]["package"]["exceptionType"], "RuntimeError")

    def test_empty_or_inconsistent_reports_are_not_successful_checks(self):
        status, report, _ = self.invoke(lambda *args: {"checks": {}, "passed": 1, "failed": 0, "completed": True})
        self.assertEqual(status, 1)
        self.assertIn("invalid_check_report", report["results"]["package"]["checks"])

    def test_interrupt_stops_subsequent_checks_and_returns_130(self):
        calls = []
        def interrupt(*args):
            raise KeyboardInterrupt()
        self.modules["validation.package"].run = interrupt
        self.modules["validation.app"].run = lambda *args: calls.append(True)
        console = io.StringIO()
        with (self.mocked_modules(), patch.object(runner.common, "ensure_host_compatible"),
              contextlib.redirect_stdout(console), contextlib.redirect_stderr(console)):
            status = runner.main(["--runtime", str(self.runtime), "--output", str(self.output), "--checks", "package", "app"])
        self.assertEqual(status, 130)
        self.assertFalse(calls)
        self.assertTrue(json.loads((self.output / "summary.json").read_text())["interrupted"])

    def test_report_write_failure_returns_nonzero_and_restores_process_settings(self):
        self.modules["validation.package"].run = lambda *args: {"checks": {"expected": True}, "passed": 1, "failed": 0, "completed": True}
        previous_handler = signal.getsignal(signal.SIGTERM)
        previous_umask = os.umask(0o077)
        os.umask(previous_umask)
        console = io.StringIO()
        with (self.mocked_modules(), patch.object(runner.common, "write_json", side_effect=OSError("synthetic-secret")),
              contextlib.redirect_stdout(console), contextlib.redirect_stderr(console)):
            status = runner.main(["--runtime", str(self.runtime), "--output", str(self.output), "--checks", "package"])
        self.assertEqual(status, 1)
        self.assertNotIn("synthetic-secret", console.getvalue())
        self.assertIn("private_report_write_failed", console.getvalue())
        self.assertEqual(signal.getsignal(signal.SIGTERM), previous_handler)
        actual_umask = os.umask(previous_umask)
        self.assertEqual(actual_umask, previous_umask)


if __name__ == "__main__":
    unittest.main()
