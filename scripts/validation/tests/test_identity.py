"""Boundaries for the public recipe and hosted identity/browser harness."""
import copy
import json
from pathlib import Path
import sqlite3
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

from scripts.validation import identity
from scripts.validation.common import ValidationFailure


class IdentityTests(unittest.TestCase):
    def test_recipe_has_distinct_marketplace_local_and_narration_identities(self):
        value = identity.recipe()
        cases = {item["key"]: item for item in value["cases"]}
        self.assertEqual(cases["us"]["asin"], cases["uk"]["asin"])
        self.assertNotEqual(cases["us"]["recordingId"], cases["uk"]["recordingId"])
        self.assertNotEqual(cases["us"]["narrator"], cases["alternate"]["narrator"])
        self.assertIsNone(cases["local"]["asin"])
        self.assertTrue(cases["local"]["recordingId"].startswith("local:recording:"))
        self.assertEqual(cases["pack"]["position"], "1-3.5")
        self.assertEqual(cases["fractional"]["position"], "3.5")
        self.assertTrue(cases["unknown-date"]["placeholder"])

    def test_recipe_rejects_identity_collisions_market_mismatch_and_unsafe_paths(self):
        for problem in ("duplicate", "market", "local", "path", "parts", "version"):
            with self.subTest(problem=problem), tempfile.TemporaryDirectory() as temporary:
                value = copy.deepcopy(identity.recipe())
                if problem == "duplicate": value["cases"][1]["recordingId"] = value["cases"][0]["recordingId"]
                elif problem == "market": value["cases"][0]["region"] = "uk"
                elif problem == "local": value["cases"][3]["asin"] = "B011111111"
                elif problem == "path": value["cases"][0]["key"] = "../../existing-home"
                elif problem == "parts": value["cases"][0]["parts"] = 0
                elif problem == "version": value["version"] = 2
                source = Path(temporary) / "recipe.json"
                source.write_text(json.dumps(value))
                with self.assertRaises(ValidationFailure): identity.recipe(source)

    def test_missing_inputs_never_start_a_process_or_create_an_output(self):
        with patch.object(identity, "Fixture") as fixture:
            value = identity.run(Path("absent"), Path("unused"))
            self.assertFalse(value["completed"])
            self.assertIn("identity_requires_explicit_baseline_runtime", value["skipped"])
            with self.assertRaisesRegex(ValidationFailure, "identity_requires_explicit_playwright_module"):
                identity.run(Path("absent"), Path("unused"), baseline_runtime=Path("baseline"))
            fixture.assert_not_called()

    def test_projection_preserves_ids_zero_false_and_unknown_dates_across_added_columns(self):
        with tempfile.TemporaryDirectory() as temporary:
            fixture = SimpleNamespace(home=Path(temporary))
            with sqlite3.connect(fixture.home / "readarr.db") as database:
                for table in identity.TABLES:
                    database.execute('CREATE TABLE "' + table + '" ("Id" INTEGER, "Monitored" INTEGER, "Date" TEXT)')
                    database.execute('INSERT INTO "' + table + '" VALUES (17,0,NULL)')
                database.execute('CREATE TABLE Config ("Key" TEXT,"Value" TEXT)')
                database.execute("INSERT INTO Config VALUES ('usehardlinks','False')")
            golden = identity.snapshot(fixture)
            with sqlite3.connect(fixture.home / "readarr.db") as database:
                database.execute('ALTER TABLE "Books" ADD COLUMN "Additive" TEXT')
            self.assertEqual(identity.snapshot(fixture, golden), golden)
            with sqlite3.connect(fixture.home / "readarr.db") as database:
                database.execute('UPDATE "Editions" SET "Id"=18')
            self.assertNotEqual(identity.snapshot(fixture, golden), golden)

    def test_browser_credentials_are_only_stdin_and_invalid_output_is_not_exposed(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "browser"
            fixture = SimpleNamespace(backend_host="127.0.0.1:12345", URL_BASE="/owned", current_username="reader",
                                      current_password="synthetic-never-print", api_key="synthetic-api-key", checks={})
            with patch.object(identity, "_browser_process", return_value=SimpleNamespace(stdout="secret malformed stdout", stderr="secret", returncode=1)) as execute:
                with self.assertRaisesRegex(ValidationFailure, "identity_browser_report_invalid"):
                    identity.browser(fixture, identity.recipe(), output, Path("/owned/playwright"))
                arguments, options = execute.call_args
                self.assertEqual(arguments[0]["password"], fixture.current_password)
                self.assertNotIn(fixture.current_password, str(arguments[1]))
                self.assertFalse(options)

    def test_browser_process_uses_owned_group_and_does_not_put_credentials_in_arguments(self):
        from unittest.mock import MagicMock
        with tempfile.TemporaryDirectory() as temporary:
            process = MagicMock(pid=12345, returncode=0)
            def spawn(arguments, **options):
                self.assertNotIn("secret-password", str(arguments))
                self.assertTrue(options["start_new_session"])
                options["stdout"].write(b'{"checks":{"owned":true},"completed":true}')
                return process
            with patch.object(identity.subprocess, "Popen", side_effect=spawn), patch.object(identity.media, "_wait_leader_without_reaping", return_value=True), patch.object(identity.media, "_stop") as stop:
                result = identity._browser_process({"password": "secret-password"}, Path(temporary))
                self.assertEqual(result.returncode, 0)
                self.assertEqual(json.loads(process.stdin.write.call_args.args[0])["password"], "secret-password")
                process.stdin.close.assert_called_once()
                stop.assert_called_once_with(process, process.pid)


if __name__ == "__main__":
    unittest.main()
