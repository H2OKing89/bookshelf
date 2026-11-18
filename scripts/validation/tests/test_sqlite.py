from pathlib import Path
import json
import tempfile
import unittest
from unittest import mock
from urllib.parse import unquote

from validation import common, media, sqlite
from validation.tests.test_media import package


class SqliteBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.runtime = package(self.root / "runtime")
        self.probe = self.root / sqlite.PROBE_ASSEMBLY
        self.probe.write_bytes(b"synthetic compiled SQLite probe")

    def test_staging_preserves_input_and_does_not_change_media_entry_contract(self):
        before = common.snapshot_runtime(self.runtime)
        media_name = media.PROBE_ASSEMBLY
        clone = sqlite.stage_probe(self.runtime, self.probe, self.root / "clone")
        self.assertEqual(common.snapshot_runtime(self.runtime), before)
        self.assertEqual((clone / "Readarr.dll").read_bytes(), self.probe.read_bytes())
        self.assertEqual((clone / "Readarr.Application.dll").read_bytes(), b"synthetic package fixture")
        self.assertEqual(media.PROBE_ASSEMBLY, media_name)

    def test_sdk_paths_cannot_redirect_build_properties(self):
        runtime = package(self.root / "candidate; OutputPath=outside %3B $()")
        output = self.root / "output; BaseIntermediateOutputPath=outside %3B @()"
        observed = {}

        def execute(command, **kwargs):
            for argument in command:
                if argument.startswith("-p:"):
                    name, value = argument[3:].split("=", 1)
                    self.assertNotIn(";", value)
                    observed[name] = unquote(value)
            Path(observed["OutputPath"], sqlite.PROBE_ASSEMBLY).write_bytes(b"synthetic probe")
            return {"exitCode": 0, "timedOut": False}

        with mock.patch.object(media, "_execute", side_effect=execute):
            built = sqlite.build_probe(runtime, output)
        self.assertEqual(observed["CandidateDir"], str(runtime))
        self.assertEqual(Path(observed["OutputPath"]), output / "bin")
        self.assertEqual(Path(observed["BaseIntermediateOutputPath"]), output / "obj")
        self.assertEqual(built, output / "bin" / sqlite.PROBE_ASSEMBLY)
        self.assertFalse((self.root / "outside").exists())

    def test_malformed_or_symlinked_reports_are_rejected(self):
        report = self.root / "report.json"
        cases = [[], {"checks": {}}, {"checks": {"valid": 1}, "metadata": {}},
                 {"checks": {"valid": True}, "metadata": []}]
        for value in cases:
            with self.subTest(value=value):
                report.write_text(json.dumps(value))
                with self.assertRaises(common.ValidationFailure):
                    sqlite.read_report(report)
        report.write_text("{")
        with self.assertRaises(common.ValidationFailure):
            sqlite.read_report(report)
        link = self.root / "linked-report.json"
        link.symlink_to(report.name)
        with self.assertRaises(common.ValidationFailure):
            sqlite.read_report(link)

    def test_missing_bundle_probe_never_removes_input_or_host_fixture(self):
        bundle = self.runtime / "libe_sqlite3.so"
        bundle.write_bytes(b"synthetic bundled native engine")
        host = self.root / "libsqlite3.so.0"
        host.write_bytes(b"synthetic host engine must remain")
        before = common.snapshot_runtime(self.runtime)

        def execute(command, **kwargs):
            clone, fixture = kwargs["cwd"], Path(command[1])
            self.assertFalse((clone / bundle.name).exists())
            self.assertEqual(host.read_bytes(), b"synthetic host engine must remain")
            (fixture / "sqlite-probe-result.json").write_text(json.dumps({
                "checks": {"probe_completed": False},
                "metadata": {"failureChain": ["TypeInitializationException", "DllNotFoundException"],
                             "nativeSqliteMappings": []},
            }))
            return {"exitCode": 1, "timedOut": False}

        with mock.patch.object(media, "_execute", side_effect=execute):
            result = sqlite.missing_native_probe(self.runtime, self.probe, self.root / "negative")
        self.assertTrue(result["checks"]["missing_bundle_fails_closed_without_host_fallback"])
        self.assertEqual(common.snapshot_runtime(self.runtime), before)

    def test_host_fallback_or_native_crash_cannot_pass_missing_bundle_gate(self):
        (self.runtime / "libe_sqlite3.so").write_bytes(b"synthetic bundled engine")
        for code, mappings in [(1, ["/synthetic-host/libsqlite3.so.0"]), (-11, [])]:
            with self.subTest(code=code):
                def execute(command, **kwargs):
                    (Path(command[1]) / "sqlite-probe-result.json").write_text(json.dumps({
                        "checks": {"probe_completed": False},
                        "metadata": {"failureChain": ["DllNotFoundException"], "nativeSqliteMappings": mappings},
                    }))
                    return {"exitCode": code, "timedOut": False}

                with mock.patch.object(media, "_execute", side_effect=execute):
                    result = sqlite.missing_native_probe(self.runtime, self.probe, self.root / ("negative" + str(code)))
                self.assertFalse(result["checks"]["missing_bundle_fails_closed_without_host_fallback"])

    def test_crashed_probe_retains_checkpoint_measurements_but_fails(self):
        measurements = [{"busyTimeoutMilliseconds": 100, "elapsedMilliseconds": 385.0}]

        def execute(command, **kwargs):
            (Path(command[1]) / "sqlite-probe-result.json").write_text(json.dumps({
                "checks": {"earlier_work_passed": True},
                "metadata": {"runtimeVersion": "10.0.15", "phase": "cancellation_requested",
                             "contentionMeasurements": measurements},
            }))
            return {"exitCode": -11, "timedOut": False}

        with (mock.patch.object(sqlite, "ensure_host_compatible"),
              mock.patch.object(sqlite, "build_probe", return_value=self.probe),
              mock.patch.object(media, "_execute", side_effect=execute)):
            result = sqlite.run_one(self.runtime, self.root / "crashed")
        self.assertFalse(result["completed"])
        self.assertFalse(result["checks"]["probe_exits_successfully"])
        self.assertEqual(result["metadata"]["probe"]["contentionMeasurements"], measurements)
        self.assertTrue(result["checks"]["input_package_unchanged"])

    def test_comparison_keeps_baseline_failure_and_candidate_success_separate(self):
        baseline = package(self.root / "baseline")
        results = [{"checks": {"passed": False}, "completed": False},
                   {"checks": {"passed": True}, "completed": True}]
        with mock.patch.object(sqlite, "run_one", side_effect=results) as run:
            result = sqlite.run(self.runtime, self.root / "comparison", baseline_runtime=baseline)
        self.assertFalse(result["completed"])
        self.assertEqual(result["failed"], 1)
        self.assertFalse(result["checks"]["baseline_passed"])
        self.assertTrue(result["checks"]["candidate_passed"])
        self.assertEqual(run.call_args_list[0].args[0], baseline)

    def test_self_comparison_is_rejected_before_creating_output(self):
        output = self.root / "comparison"
        with self.assertRaisesRegex(common.ValidationFailure, "sqlite_comparison_requires_distinct_baseline"):
            sqlite.run(self.runtime, output, baseline_runtime=self.runtime)
        self.assertFalse(output.exists())

    def test_net6_baseline_is_rejected_before_sdk_or_probe_execution(self):
        baseline = package(self.root / "baseline")
        (baseline / "Readarr.runtimeconfig.json").write_text(json.dumps({
            "runtimeOptions": {"tfm": "net6.0", "includedFrameworks": [
                {"name": "Microsoft.NETCore.App", "version": "6.0.36"}]},
        }))
        output = self.root / "comparison"
        with (mock.patch.object(media, "_execute") as execute,
              mock.patch.object(sqlite, "run_one") as run):
            with self.assertRaisesRegex(common.ValidationFailure, "sqlite_requires_self_contained_net10_package"):
                sqlite.run(self.runtime, output, baseline_runtime=baseline)
        execute.assert_not_called()
        run.assert_not_called()
        self.assertFalse(output.exists())

    def test_net6_candidate_and_mismatched_tfm_are_rejected_before_sdk(self):
        for index, (tfm, core) in enumerate([("net6.0", "6.0.36"), ("net6.0", "10.0.15")]):
            with self.subTest(tfm=tfm, core=core):
                (self.runtime / "Readarr.runtimeconfig.json").write_text(json.dumps({
                    "runtimeOptions": {"tfm": tfm, "includedFrameworks": [
                        {"name": "Microsoft.NETCore.App", "version": core}]},
                }))
                output = self.root / ("build" + str(index))
                with mock.patch.object(media, "_execute") as execute:
                    with self.assertRaisesRegex(common.ValidationFailure, "sqlite_requires_self_contained_net10_package"):
                        sqlite.build_probe(self.runtime, output)
                execute.assert_not_called()
                self.assertFalse(output.exists())
