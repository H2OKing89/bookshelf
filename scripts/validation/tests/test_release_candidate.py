import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import release_candidate as release
from validation.common import ValidationFailure, runtime_fingerprint, snapshot_runtime
from validation.tests.test_common import package


class ReleaseCandidateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repo = self.root / "source"
        self.repo.mkdir()
        self.run_git("init", "-q", "-b", "owned-test")
        (self.repo / "LICENSE.md").write_text("Synthetic GPL license fixture")
        migrations = self.repo / "src/NzbDrone.Core/Datastore/Migration"
        migrations.mkdir(parents=True)
        (migrations / "001_initial.cs").write_text("Synthetic historical migration")
        self.run_git("add", ".")
        self.run_git("-c", "user.name=Owned Test", "-c", "user.email=owned@example.invalid", "commit", "-qm", "Owned source")
        self.source = {"productId": release.PRODUCT_ID, "sourceCommit": self.run_git("rev-parse", "HEAD"),
                       "sourceTree": self.run_git("rev-parse", "HEAD^{tree}"), "sourceBranch": "owned-test", "buildDirty": False}
        self.runtime = package(self.root / "runtime")
        self.write(self.runtime / "Readarr.runtimeconfig.json", {"runtimeOptions": {"tfm": "net10.0", "includedFrameworks": [
            {"name": name, "version": "10.0.15"} for name in ("Microsoft.NETCore.App", "Microsoft.AspNetCore.App")]}})
        self.write(self.runtime / "Readarr.deps.json", {"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-x64"}})
        self.write(self.runtime / "UI/source-provenance.json", self.source)
        self.report = self.root / "evidence.json"
        self.update_report()
        self.output = self.root / "candidate"

    def run_git(self, *args):
        return subprocess.run(["git", *args], cwd=self.repo, check=True, capture_output=True, text=True).stdout.strip()

    def write(self, path, value):
        path.write_text(json.dumps(value))

    def update_report(self):
        self.evidence = {"completed": True, "failed": 0, "passed": len(release.REQUIRED_CHECKS), "syntheticDataOnly": True,
                         "inputFingerprints": {"candidate": runtime_fingerprint(snapshot_runtime(self.runtime))},
                         "results": {name: {"completed": True, "failed": 0, "passed": 1, "checks": {"owned_check": True}}
                                     for name in release.REQUIRED_CHECKS}}
        self.evidence["results"]["releaseidentity"]["metadata"] = {"source": self.source, "version": "10.0.0.123"}
        self.write(self.report, self.evidence)

    def stage(self):
        return release.prepare(self.runtime, [self.report], self.output, self.repo)

    def mutate_manifest(self, action):
        path = self.output / "release-manifest.json"
        value = json.loads(path.read_text())
        action(value)
        self.write(path, value)

    def test_stage_binds_unchanged_tested_package_exact_git_source_and_license(self):
        before = snapshot_runtime(self.runtime)
        result = self.stage()
        self.assertFalse(result["publishable"])
        self.assertFalse(result["builtInUpdatesEnabled"])
        self.assertEqual(snapshot_runtime(self.runtime), before)
        self.assertEqual(release.verify(self.output, self.repo), result)
        self.assertEqual(self.output.stat().st_mode & 0o777, 0o700)
        self.assertEqual(result["source"]["archiveSha256"], release.archive_sha256(self.repo, self.source["sourceCommit"]))

    def test_unrelated_or_failing_evidence_cannot_stage_a_release(self):
        for label in ("fingerprint", "failure", "incomplete", "check_false", "check_missing", "dirty", "source", "version"):
            with self.subTest(label=label):
                value = copy.deepcopy(self.evidence)
                if label == "fingerprint": value["inputFingerprints"]["candidate"] = "0" * 64
                elif label == "failure": value["failed"] = 1
                elif label == "incomplete": value["completed"] = False
                elif label == "check_false": value["results"]["app"]["checks"]["owned_check"] = False
                elif label == "check_missing": del value["results"]["restore"]
                elif label == "dirty": value["results"]["releaseidentity"]["metadata"]["source"]["buildDirty"] = True
                elif label == "source": value["results"]["releaseidentity"]["metadata"]["source"]["sourceCommit"] = "0" * 40
                elif label == "version": value["results"]["releaseidentity"]["metadata"]["version"] = "unknown"
                self.write(self.report, value)
                with self.assertRaises(ValidationFailure): self.stage()
                self.assertFalse(self.output.exists())

    def test_dirty_source_unsupported_channel_and_foreign_frontend_are_rejected(self):
        (self.repo / "uncommitted.txt").write_text("owned uncommitted source")
        with self.assertRaises(ValidationFailure): self.stage()
        (self.repo / "uncommitted.txt").unlink()
        with self.assertRaises(ValidationFailure):
            release.prepare(self.runtime, [self.report], self.output, self.repo, "hardcover")
        self.write(self.runtime / "UI/source-provenance.json", dict(self.source, sourceCommit="0" * 40))
        self.evidence["inputFingerprints"]["candidate"] = runtime_fingerprint(snapshot_runtime(self.runtime))
        self.write(self.report, self.evidence)
        with self.assertRaises(ValidationFailure): self.stage()
        self.assertFalse(self.output.exists())

    def test_manifest_product_channel_commit_version_runtime_and_schema_tampering_is_rejected(self):
        self.stage()
        path = self.output / "release-manifest.json"
        original = path.read_bytes()
        changes = [lambda m: m.update(productId="sonarr"), lambda m: m.update(channel="stable"),
                   lambda m: m["source"].update(sourceCommit="0" * 40), lambda m: m.update(version="99.0.0.0"),
                   lambda m: m["runtime"].update(rid="linux-musl-arm64"),
                   lambda m: m["runtime"].update(tfm="net6.0"), lambda m: m["database"].update(fingerprint="0" * 64),
                   lambda m: m.update(publishable=True), lambda m: m.update(builtInUpdatesEnabled=True)]
        for change in changes:
            with self.subTest(change=changes.index(change)):
                path.write_bytes(original)
                self.mutate_manifest(change)
                with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)

    def test_changed_payload_extra_file_and_executable_permissions_are_rejected(self):
        self.stage()
        binary = self.output / "package/Readarr"
        content, mode = binary.read_bytes(), binary.stat().st_mode & 0o777
        binary.write_bytes(b"different binary")
        with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)
        binary.write_bytes(content)
        binary.chmod(mode ^ 0o100)
        with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)
        binary.chmod(mode)
        extra = self.output / "package/extra-updater"
        extra.write_text("unexpected executable")
        with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)

    def test_changed_source_archive_evidence_and_external_package_symlink_are_rejected(self):
        self.stage()
        archive = self.output / "source.tar.gz"
        original = archive.read_bytes()
        archive.write_bytes(b"wrong source archive")
        self.mutate_manifest(lambda m: m["source"].update(archiveSha256=release.file_sha256(archive)))
        with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)
        archive.write_bytes(original)
        self.mutate_manifest(lambda m: m["source"].update(archiveSha256=release.file_sha256(archive)))
        receipt = self.output / "evidence-1.json"
        receipt.write_text("{}")
        with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)

    def test_expected_identity_constraints_reject_the_wrong_target(self):
        self.stage()
        for kwargs in ({"expected_product": "readarr"}, {"expected_channel": "stable"},
                       {"expected_commit": "0" * 40}, {"expected_rid": "linux-musl-x64"}, {"expected_version": "9.0.0.0"}):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValidationFailure):
                release.verify(self.output, self.repo, **kwargs)

    def test_existing_output_is_never_replaced_and_foreign_runtime_is_rejected(self):
        self.output.mkdir()
        marker = self.output / "owned-marker"
        marker.write_text("keep")
        with self.assertRaises(ValidationFailure): self.stage()
        self.assertEqual(marker.read_text(), "keep")
        marker.unlink(); self.output.rmdir()
        self.write(self.runtime / "Readarr.deps.json", {"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-musl-arm64"}})
        self.update_report()
        with self.assertRaises(ValidationFailure): self.stage()

    def test_malformed_boolean_or_float_counts_and_schema_versions_are_rejected(self):
        for change in (lambda r: r.update(failed=False), lambda r: r.update(passed=True),
                       lambda r: r.update(passed=1.0), lambda r: r["results"]["app"].update(failed=False),
                       lambda r: r["results"]["app"].update(passed=1.0)):
            original = copy.deepcopy(self.evidence)
            change(original)
            self.write(self.report, original)
            with self.assertRaises(ValidationFailure): self.stage()
        self.write(self.report, self.evidence)
        self.stage()
        for value in (True, 1.0):
            self.mutate_manifest(lambda manifest: manifest.update(schemaVersion=value))
            with self.assertRaises(ValidationFailure): release.verify(self.output, self.repo)


if __name__ == "__main__":
    unittest.main()
