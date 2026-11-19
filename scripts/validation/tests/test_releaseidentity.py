import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from validation import releaseidentity
from validation.common import ValidationFailure
from validation.tests.test_common import package
from validation.tests import test_release_candidate as release_cases


class ReleaseIdentityBoundaryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = package(self.root / "runtime")
        self.source = {"productId": releaseidentity.PRODUCT_ID, "sourceCommit": "1" * 40,
                       "sourceTree": "2" * 40, "sourceBranch": "owned-test", "buildDirty": False}
        (self.runtime / "UI/source-provenance.json").write_text(json.dumps(self.source))
        self.status = dict(self.source, version="10.0.0.123", builtInUpdatesAvailable=False)
        self.fixture = SimpleNamespace(process=None, checks={})
        self.fixture.configure_home = Mock()
        self.fixture.start = Mock(side_effect=lambda: setattr(self.fixture, "process", object()))
        self.fixture.api = Mock(return_value=self.status)
        self.fixture.cleanup = Mock(side_effect=lambda: setattr(self.fixture, "process", None))

        def check(name, condition):
            self.fixture.checks[name] = bool(condition)
            if not condition:
                raise ValidationFailure(name)

        self.fixture.check = check

    def run_probe(self, snapshots=None):
        snapshot = snapshots or [{"owned": "same"}, {"owned": "same"}]
        with (patch.object(releaseidentity, "Fixture", return_value=self.fixture),
              patch.object(releaseidentity, "snapshot_runtime", side_effect=snapshot)):
            return releaseidentity.run(self.runtime, self.root / "output")

    def test_matching_compiled_and_frontend_identities_are_reported_without_enabling_updates(self):
        result = self.run_probe()

        self.assertTrue(result["completed"])
        self.assertEqual(result["failed"], 0)
        self.assertEqual(result["metadata"], {"source": self.source, "version": "10.0.0.123"})
        self.fixture.cleanup.assert_called_once()
        self.assertIsNone(self.fixture.process)

    def test_unset_or_malformed_compiled_metadata_cannot_be_treated_as_a_clean_source_identity(self):
        for name, value in (("productId", "readarr"), ("sourceCommit", "1" * 39),
                            ("sourceCommit", "x" * 40), ("sourceTree", ""),
                            ("sourceBranch", None), ("sourceBranch", ""),
                            ("buildDirty", "false"), ("buildDirty", 0), ("buildDirty", None)):
            with self.subTest(name=name, value=value):
                source = dict(self.source, **{name: value})
                self.assertFalse(releaseidentity.valid_identity(source))

    def test_dirty_build_identity_is_preserved_for_the_release_gate_to_reject(self):
        self.source["buildDirty"] = True
        self.status["buildDirty"] = True
        (self.runtime / "UI/source-provenance.json").write_text(json.dumps(self.source))

        result = self.run_probe()

        self.assertTrue(result["completed"])
        self.assertIs(result["metadata"]["source"]["buildDirty"], True)

    def test_foreign_frontend_or_enabled_updates_cannot_produce_a_passing_receipt(self):
        for label in ("frontend", "updates"):
            with self.subTest(label=label):
                self.fixture.checks.clear()
                self.status["builtInUpdatesAvailable"] = label == "updates"
                frontend = copy.deepcopy(self.source)
                if label == "frontend":
                    frontend["sourceCommit"] = "3" * 40
                (self.runtime / "UI/source-provenance.json").write_text(json.dumps(frontend))

                result = self.run_probe()

                self.assertFalse(result["completed"])
                self.assertGreater(result["failed"], 0)
                self.assertTrue(result["checks"]["owned_process_cleaned_up"])

    def test_failure_messages_do_not_leak_private_values_and_owned_process_is_still_cleaned(self):
        self.fixture.api.side_effect = RuntimeError("owned-private-value-canary")

        result = self.run_probe()

        self.assertFalse(result["completed"])
        self.assertEqual(result["failure"], {"stage": "release_identity", "exceptionType": "RuntimeError"})
        self.assertNotIn("owned-private-value-canary", json.dumps(result))
        self.assertTrue(result["checks"]["owned_process_cleaned_up"])

    def test_payload_change_or_cleanup_failure_cannot_produce_a_passing_receipt(self):
        result = self.run_probe([{"owned": "before"}, {"owned": "after"}])
        self.assertFalse(result["checks"]["package_unchanged"])
        self.assertGreater(result["failed"], 0)

        self.fixture.checks.clear()
        self.fixture.cleanup.side_effect = RuntimeError("owned-cleanup-failure")
        result = self.run_probe()

        self.assertFalse(result["checks"]["owned_process_cleaned_up"])
        self.assertGreater(result["failed"], 0)
        self.assertNotIn("owned-cleanup-failure", json.dumps(result))

    def release_fixture(self):
        fixture = release_cases.ReleaseCandidateTests(methodName="test_stage_binds_unchanged_tested_package_exact_git_source_and_license")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        return fixture

    def test_failed_staging_cleans_only_its_new_output_and_preserves_the_input(self):
        fixture = self.release_fixture()
        before = release_cases.snapshot_runtime(fixture.runtime)
        marker = fixture.root / "existing-owned-marker"
        marker.write_text("preserve")
        copytree = release_cases.release.shutil.copytree

        def copy_then_fail(*args, **kwargs):
            copytree(*args, **kwargs)
            raise RuntimeError("owned-copy-failure")

        with patch.object(release_cases.release.shutil, "copytree", side_effect=copy_then_fail):
            with self.assertRaises(RuntimeError):
                fixture.stage()

        self.assertFalse(fixture.output.exists())
        self.assertEqual(release_cases.snapshot_runtime(fixture.runtime), before)
        self.assertEqual(marker.read_text(), "preserve")

    def test_symlinked_evidence_cannot_create_output_or_change_its_target(self):
        fixture = self.release_fixture()
        target = fixture.root / "owned-external-receipt.json"
        original = fixture.report.read_bytes()
        target.write_bytes(original)
        fixture.report.unlink()
        fixture.report.symlink_to(target)

        with self.assertRaises(ValidationFailure):
            fixture.stage()

        self.assertFalse(fixture.output.exists())
        self.assertEqual(target.read_bytes(), original)

    def test_bundle_receipts_cannot_escape_the_bundle_and_source_archive_cannot_be_a_symlink(self):
        fixture = self.release_fixture()
        fixture.stage()
        manifest_path = fixture.output / "release-manifest.json"
        original = manifest_path.read_bytes()
        fixture.mutate_manifest(lambda value: value.update(evidence={"../owned-external.json": "0" * 64}))

        with self.assertRaises(ValidationFailure):
            release_cases.release.verify(fixture.output, fixture.repo)

        manifest_path.write_bytes(original)
        archive = fixture.output / "source.tar.gz"
        target = fixture.root / "owned-external-source.tar.gz"
        target.write_bytes(archive.read_bytes())
        archive.unlink()
        archive.symlink_to(target)
        with self.assertRaises(ValidationFailure):
            release_cases.release.verify(fixture.output, fixture.repo)
        self.assertTrue(target.is_file())


if __name__ == "__main__":
    unittest.main()
