import importlib
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from validation import logstorage
from validation.common import ValidationFailure
from validation.tests.test_common import package


class LogStorageLifecycleTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = package(self.root / "runtime")

    def test_import_starts_no_application_or_postgres_container(self):
        with patch.object(subprocess, "Popen") as process, patch.object(logstorage.datastore.OwnedPostgres, "start") as postgres:
            importlib.reload(logstorage)
        process.assert_not_called()
        postgres.assert_not_called()

    def test_failure_is_secret_safe_and_still_closes_owned_resources(self):
        with (patch.object(logstorage, "ensure_host_compatible"),
              patch.object(logstorage, "sqlite_fresh", side_effect=RuntimeError("private-runtime-token")),
              patch.object(logstorage.datastore.OwnedPostgres, "start") as start,
              patch.object(logstorage.datastore.OwnedPostgres, "close", return_value=True) as close):
            report = logstorage.run(self.runtime, self.root / "output")
        self.assertFalse(report["completed"])
        self.assertEqual(report["failure"], {"stage": "sqlite_fresh_disabled", "exceptionType": "RuntimeError"})
        self.assertNotIn("private-runtime-token", json.dumps(report))
        self.assertTrue(report["checks"]["input_package_unchanged"])
        self.assertTrue(report["checks"]["all_owned_application_processes_stopped"])
        self.assertTrue(report["checks"]["owned_postgres_container_and_private_socket_removed"])
        self.assertEqual((self.root / "output").stat().st_mode & 0o777, 0o700)
        start.assert_not_called()
        close.assert_called_once()

    def test_interrupt_propagates_after_owned_fixture_and_postgres_cleanup(self):
        fixture = SimpleNamespace(process=object(), forced_stops=0)
        fixture.cleanup = Mock(side_effect=lambda: setattr(fixture, "process", None))

        def interrupted(runtime, output, fixtures, check):
            fixtures.append(fixture)
            raise KeyboardInterrupt()

        with (patch.object(logstorage, "ensure_host_compatible"),
              patch.object(logstorage, "sqlite_fresh", side_effect=interrupted),
              patch.object(logstorage.datastore.OwnedPostgres, "close", return_value=True) as close,
              self.assertRaises(KeyboardInterrupt)):
            logstorage.run(self.runtime, self.root / "output")
        fixture.cleanup.assert_called_once()
        close.assert_called_once()
        self.assertIsNone(fixture.process)

    def test_application_cleanup_failure_does_not_skip_postgres_cleanup(self):
        fixture = SimpleNamespace(process=object(), forced_stops=0, cleanup=Mock(side_effect=OSError("owned cleanup failure")))

        def failed(runtime, output, fixtures, check):
            fixtures.append(fixture)
            raise ValidationFailure("owned_synthetic_failure")

        with (patch.object(logstorage, "ensure_host_compatible"),
              patch.object(logstorage, "sqlite_fresh", side_effect=failed),
              patch.object(logstorage.datastore.OwnedPostgres, "close", return_value=True) as close):
            report = logstorage.run(self.runtime, self.root / "output")
        self.assertFalse(report["checks"]["all_owned_application_processes_stopped"])
        self.assertTrue(report["checks"]["owned_postgres_container_and_private_socket_removed"])
        close.assert_called_once()

    def test_input_mutation_never_passes_even_after_an_earlier_failure(self):
        def mutate(*args):
            (self.runtime / "Readarr.Core.dll").write_bytes(b"changed synthetic candidate")
            raise ValidationFailure("owned_injected_failure")

        with (patch.object(logstorage, "ensure_host_compatible"),
              patch.object(logstorage, "sqlite_fresh", side_effect=mutate),
              patch.object(logstorage.datastore.OwnedPostgres, "close", return_value=True)):
            report = logstorage.run(self.runtime, self.root / "output")
        self.assertFalse(report["checks"]["input_package_unchanged"])
        self.assertGreater(report["failed"], 0)


if __name__ == "__main__":
    unittest.main()
