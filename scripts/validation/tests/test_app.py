"""Safety boundaries and lifecycle behavior of synthetic application fixtures."""

import importlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from scripts.validation import app
from scripts.validation.common import ValidationFailure


class AppFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.runtime = self.root / "runtime"
        (self.runtime / "UI").mkdir(parents=True)
        for name in ("Readarr", "Readarr.Core.dll", "UI/index.html"):
            (self.runtime / name).write_bytes(b"synthetic package fixture")
        self.config = self.runtime / "Readarr.runtimeconfig.json"
        self.config.write_text(json.dumps({"runtimeOptions": {"includedFrameworks": [
            {"name": "Microsoft.NETCore.App", "version": "10.0.77"},
            {"name": "Microsoft.AspNetCore.App", "version": "10.0.77"}]}}))

    def tearDown(self):
        self.temporary.cleanup()

    def test_home_is_new_private_and_uses_loopback_without_reusing_config(self):
        fixture = app.Fixture(self.runtime, self.root / "output")
        fixture.configure_home()
        root = ET.parse(fixture.home / "config.xml").getroot()
        self.assertEqual(root.findtext("BindAddress"), "127.0.0.1")
        self.assertEqual(root.findtext("ApiKey"), fixture.api_key)
        self.assertEqual(root.findtext("UpdateAutomatically"), "False")
        self.assertEqual(fixture.home.stat().st_mode & 0o777, 0o700)
        self.assertEqual(fixture.private_tmp.stat().st_mode & 0o777, 0o700)
        self.assertEqual((fixture.home / "config.xml").stat().st_mode & 0o777, 0o600)
        with self.assertRaises(ValidationFailure):
            fixture.configure_home()
        with self.assertRaises(ValidationFailure):
            app.Fixture(self.runtime, self.root / "output")

    def test_existing_application_home_cannot_be_an_output_ancestor(self):
        home = self.root / "existing-home"
        home.mkdir()
        original = b"existing saved configuration"
        (home / "config.xml").write_bytes(original)
        with self.assertRaises(ValidationFailure):
            app.Fixture(self.runtime, home / "validation")
        self.assertEqual((home / "config.xml").read_bytes(), original)
        self.assertFalse((home / "validation").exists())

    def test_runtime_configuration_controls_version_expectation(self):
        self.assertTrue(app.runtime_matches(self.runtime, "10.0.77"))
        self.assertFalse(app.runtime_matches(self.runtime, "10.0.15"))
        self.config.write_text(json.dumps({"runtimeOptions": {"framework": {
            "name": "Microsoft.NETCore.App", "version": "10.0.77"}}}))
        self.assertTrue(app.runtime_matches(self.runtime, "10.0.88"))
        self.assertFalse(app.runtime_matches(self.runtime, "11.0.0"))

    def test_child_environment_drops_app_and_jit_overrides(self):
        with patch.dict(os.environ, {"READARR__APIKEY": "unrelated saved credential",
                                    "DOTNET_TieredCompilation": "0", "LD_PRELOAD": "/unrelated/library.so",
                                    "HOME": "/unrelated/home", "ASPNETCORE_URLS": "http://0.0.0.0:8787"}):
            environment = app.child_environment(self.root / "home", self.root / "tmp")
        for name in ("READARR__APIKEY", "DOTNET_TieredCompilation", "LD_PRELOAD", "ASPNETCORE_URLS"):
            self.assertNotIn(name, environment)
        self.assertEqual(environment["HOME"], str(self.root / "home"))
        self.assertEqual(environment["TMPDIR"], str(self.root / "tmp"))

    def test_owned_log_override_accepts_only_an_explicit_boolean(self):
        fixture = app.Fixture(self.runtime, self.root / "invalid-override")
        fixture.configure_home()
        for invalid in ("false", "true", 0, 1, {}, []):
            with self.subTest(invalid=invalid), patch.object(subprocess, "Popen") as process:
                with self.assertRaises(ValidationFailure) as error:
                    fixture.start(log_db_enabled=invalid)
                self.assertEqual(error.exception.code, "invalid_owned_log_db_override")
                process.assert_not_called()

    def test_owned_log_override_is_scoped_and_never_inherited_from_the_caller(self):
        for enabled in (None, False, True):
            with self.subTest(enabled=enabled):
                fixture = app.Fixture(self.runtime, self.root / ("override-" + str(enabled)))
                fixture.configure_home()
                with (patch.dict(os.environ, {"Readarr__Log__DbEnabled": "unrelated override"}),
                      patch.object(app, "ensure_host_compatible"),
                      patch.object(subprocess, "Popen") as process,
                      patch.object(fixture, "request", return_value=(200, [], b"{}"))):
                    process.return_value.poll.return_value = None
                    try:
                        fixture.start(log_db_enabled=enabled)
                        environment = process.call_args.kwargs["env"]
                        if enabled is None:
                            self.assertNotIn("Readarr__Log__DbEnabled", environment)
                        else:
                            self.assertEqual(environment["Readarr__Log__DbEnabled"], str(enabled).lower())
                    finally:
                        fixture.process = None
                        if fixture.process_log is not None:
                            fixture.process_log.close()
                            fixture.process_log = None

    def test_owned_postgres_overrides_reject_non_strings_unknown_keys_and_native_injection(self):
        fixture = app.Fixture(self.runtime, self.root / "invalid-postgres")
        fixture.configure_home()
        for value in ([], "value", {"PATH": "unsafe"}, {"Host": "unowned"},
                      {"MainDbConnectionString": False}, {"MainDbConnectionString": "bad\0value"}):
            with self.subTest(value=value), patch.object(subprocess, "Popen") as process:
                with self.assertRaises(ValidationFailure) as error:
                    fixture.start(postgres_connection_strings=value)
                self.assertEqual(error.exception.code, "invalid_owned_postgres_connection_override")
                process.assert_not_called()

    def test_owned_postgres_strings_are_injected_only_from_explicit_exact_keys(self):
        fixture = app.Fixture(self.runtime, self.root / "postgres-override")
        fixture.configure_home()
        overrides = {"MainDbConnectionString": "owned-main", "CacheDbConnectionString": "owned-cache"}
        with (patch.dict(os.environ, {"Readarr__Postgres__LogDbConnectionString": "unrelated connection"}),
              patch.object(app, "ensure_host_compatible"), patch.object(subprocess, "Popen") as process,
              patch.object(fixture, "request", return_value=(200, [], b"{}"))):
            process.return_value.poll.return_value = None
            try:
                fixture.start(postgres_connection_strings=overrides)
                environment = process.call_args.kwargs["env"]
                self.assertEqual(environment["Readarr__Postgres__MainDbConnectionString"], "owned-main")
                self.assertEqual(environment["Readarr__Postgres__CacheDbConnectionString"], "owned-cache")
                self.assertNotIn("Readarr__Postgres__LogDbConnectionString", environment)
            finally:
                fixture.process = None
                if fixture.process_log is not None:
                    fixture.process_log.close()
                    fixture.process_log = None

    def test_failure_metadata_omits_exception_payload_and_traceback(self):
        error = RuntimeError("saved-credential-must-not-reach-a-report")
        report = app.safe_failure("startup", error)
        self.assertEqual(report, {"stage": "startup", "exceptionType": "RuntimeError"})

    def test_import_starts_no_runtime(self):
        with patch.object(subprocess, "Popen") as start:
            importlib.reload(app)
        start.assert_not_called()

    def test_interrupt_propagates_after_fixture_cleanup(self):
        with patch.object(app.Fixture, "start", side_effect=KeyboardInterrupt), \
                patch.object(app.Fixture, "cleanup") as cleanup:
            with self.assertRaises(KeyboardInterrupt):
                app.run(self.runtime, self.root / "output")
        cleanup.assert_called_once()

    @unittest.skipUnless(os.name == "posix", "process groups require POSIX")
    def test_cleanup_stops_only_its_owned_process_group(self):
        fixture = app.Fixture(self.runtime, self.root / "output")
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"],
                                     start_new_session=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        owned = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"],
                                 start_new_session=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        fixture.process = owned
        try:
            fixture.cleanup()
            fixture.cleanup()
            self.assertIsNone(fixture.process)
            self.assertIsNotNone(owned.poll())
            self.assertIsNone(unrelated.poll())
        finally:
            if owned.poll() is None:
                os.killpg(owned.pid, signal.SIGKILL)
                owned.wait(timeout=5)
            unrelated.terminate()
            unrelated.wait(timeout=5)


if __name__ == "__main__":
    unittest.main()
