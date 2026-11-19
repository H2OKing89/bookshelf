import importlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import unquote

from validation import acquisition, common
from validation.tests.test_media import package


class AcquisitionBoundaryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = package(self.root / "runtime")
        self.output = self.root / "result"
        self.probe = self.root / acquisition.PROBE_ASSEMBLY
        self.probe.write_bytes(b"synthetic compiled acquisition probe")

    def test_import_starts_no_application_sdk_or_database(self):
        with patch.object(acquisition.media, "_execute") as execute:
            importlib.reload(acquisition)
        execute.assert_not_called()

    def test_saved_home_input_is_rejected_before_building_or_creating_output(self):
        (self.runtime / "config.xml").write_text("Synthetic saved-home marker")
        with patch.object(acquisition, "build_probe") as build, self.assertRaises(common.ValidationFailure):
            acquisition.run(self.runtime, self.output)
        build.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_only_owned_clone_entry_is_replaced_and_readonly_input_stays_unchanged(self):
        entry = self.runtime / "Readarr.dll"
        entry.chmod(0o444)
        (self.runtime / "internal.dll").symlink_to("Readarr.Core.dll")
        before = common.snapshot_runtime(self.runtime)
        clone = acquisition.stage_clone(self.runtime, self.probe, self.output)
        self.assertEqual((clone / "Readarr.dll").read_bytes(), self.probe.read_bytes())
        self.assertEqual((clone / "Readarr.Application.dll").read_bytes(), b"synthetic package fixture")
        self.assertEqual((clone / "Readarr.Core.dll").read_bytes(), b"synthetic package fixture")
        self.assertTrue((clone / "internal.dll").is_symlink())
        self.assertEqual(clone.stat().st_mode & 0o777, 0o700)
        self.assertEqual(common.snapshot_runtime(self.runtime), before)

    def test_escaping_links_and_reserved_clone_names_are_rejected(self):
        outside = self.root / "outside"
        outside.write_text("Synthetic outside marker")
        link = self.runtime / "escape"
        link.symlink_to(outside)
        with self.assertRaises(common.ValidationFailure):
            acquisition.stage_clone(self.runtime, self.probe, self.output)
        self.assertFalse(self.output.exists())
        link.unlink()
        (self.runtime / "Readarr.Application.dll").write_bytes(b"reserved")
        with self.assertRaises(common.ValidationFailure):
            acquisition.stage_clone(self.runtime, self.probe, self.output)
        self.assertFalse(self.output.exists())
        self.assertEqual(outside.read_text(), "Synthetic outside marker")

    def test_sdk_paths_are_owned_and_msbuild_values_are_escaped(self):
        runtime = package(self.root / "candidate; OutputPath=outside %3B $()")
        output = self.root / "output; BaseIntermediateOutputPath=outside %3B @()"
        observed = {}

        def execute(command, **kwargs):
            for argument in command:
                if argument.startswith("-p:"):
                    name, value = argument[3:].split("=", 1)
                    self.assertNotIn(";", value)
                    observed[name] = unquote(value)
            self.assertEqual(kwargs["cwd"], acquisition.PROJECT.parents[3])
            Path(observed["OutputPath"], acquisition.PROBE_ASSEMBLY).write_bytes(b"synthetic probe")
            return {"exitCode": 0, "timedOut": False}

        with patch.object(acquisition.media, "_execute", side_effect=execute):
            probe = acquisition.build_probe(runtime, output)
        self.assertEqual(observed["CandidateDir"], str(runtime))
        self.assertEqual(observed["ValidationOutputDir"], str(output))
        self.assertEqual(Path(observed["OutputPath"]), output / "bin")
        self.assertEqual(Path(observed["BaseIntermediateOutputPath"]), output / "obj")
        self.assertEqual(probe, output / "bin" / acquisition.PROBE_ASSEMBLY)
        self.assertFalse((self.root / "outside").exists())

    def test_success_receipt_uses_real_owned_clone_and_boolean_checks(self):
        before = common.snapshot_runtime(self.runtime)

        def execute(command, **kwargs):
            self.assertNotEqual(Path(command[0]).parent, self.runtime)
            self.assertTrue(Path(command[0]).is_relative_to(self.output))
            fixture = Path(command[1])
            self.assertEqual(kwargs["environment"]["HOME"], str(fixture))
            (fixture / "acquisition-probe-result.json").write_text(json.dumps({
                "checks": {"published_fixture_check": True}, "metadata": {"runtimeVersion": "10.0.15"}}))
            return {"exitCode": 0, "timedOut": False}

        with (patch.object(acquisition, "ensure_host_compatible"),
              patch.object(acquisition, "build_probe", return_value=self.probe),
              patch.object(acquisition.media, "_execute", side_effect=execute)):
            result = acquisition.run(self.runtime, self.output)
        self.assertTrue(result["completed"])
        self.assertEqual(result["failed"], 0)
        self.assertTrue(result["checks"]["candidate_package_unchanged"])
        self.assertEqual(common.snapshot_runtime(self.runtime), before)
        self.assertTrue(result["metadata"]["privateSyntheticFixturesOnly"])
        self.assertFalse(result["metadata"]["downloadClientsContacted"])

    def test_non_boolean_probe_checks_cannot_make_a_passing_receipt(self):
        def execute(command, **kwargs):
            (Path(command[1]) / "acquisition-probe-result.json").write_text(json.dumps({
                "checks": {"published_fixture_check": "true"}, "metadata": {"runtimeVersion": "10.0.15"}}))
            return {"exitCode": 0, "timedOut": False}

        with (patch.object(acquisition, "ensure_host_compatible"),
              patch.object(acquisition, "build_probe", return_value=self.probe),
              patch.object(acquisition.media, "_execute", side_effect=execute)):
            result = acquisition.run(self.runtime, self.output)
        self.assertFalse(result["completed"])
        self.assertGreater(result["failed"], 0)
        self.assertEqual(result["metadata"]["failureCode"], "acquisition_probe_checks_invalid")
        self.assertTrue(result["checks"]["candidate_package_unchanged"])

    def test_failure_message_is_secret_safe_and_keeps_candidate_unchanged(self):
        with (patch.object(acquisition, "ensure_host_compatible"),
              patch.object(acquisition, "build_probe", side_effect=RuntimeError("private-owned-canary"))):
            result = acquisition.run(self.runtime, self.output)
        self.assertFalse(result["completed"])
        self.assertNotIn("private-owned-canary", json.dumps(result))
        self.assertEqual(result["metadata"]["errorType"], "RuntimeError")
        self.assertTrue(result["checks"]["candidate_package_unchanged"])

    def test_process_timeout_or_failure_never_reports_completed(self):
        def execute(command, **kwargs):
            (Path(command[1]) / "acquisition-probe-result.json").write_text(json.dumps({
                "checks": {"published_fixture_check": True}, "metadata": {"runtimeVersion": "10.0.15"}}))
            return {"exitCode": -9, "timedOut": True}

        with (patch.object(acquisition, "ensure_host_compatible"),
              patch.object(acquisition, "build_probe", return_value=self.probe),
              patch.object(acquisition.media, "_execute", side_effect=execute)):
            result = acquisition.run(self.runtime, self.output)
        self.assertFalse(result["completed"])
        self.assertFalse(result["checks"]["probe_process_completed_without_timeout"])
        self.assertFalse(result["checks"]["probe_process_exited_successfully"])
        self.assertTrue(result["checks"]["candidate_package_unchanged"])


if __name__ == "__main__":
    unittest.main()
