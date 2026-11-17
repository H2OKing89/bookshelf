from pathlib import Path
import os
import platform
import struct
import tempfile
import unittest

from validation import common


def package(root):
    root.mkdir()
    (root / "UI").mkdir()
    for name in ("Readarr", "Readarr.Core.dll", "Readarr.runtimeconfig.json", "UI/index.html"):
        (root / name).write_bytes(b"synthetic package fixture")
    return root


class PathBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.runtime = package(self.root / "runtime")

    def test_runtime_output_overlap_and_symlink_aliases_are_rejected(self):
        alias = self.root / "alias"
        alias.symlink_to(self.runtime, target_is_directory=True)
        for output in (self.runtime, self.runtime / "results", self.root, alias / "results"):
            with self.subTest(output=output), self.assertRaises(common.ValidationFailure):
                common.validate_output(self.runtime, output)

    def test_existing_application_home_cannot_be_runtime_or_output_parent(self):
        home = self.root / "home"
        home.mkdir()
        (home / "config.xml").write_text("synthetic home marker")
        with self.assertRaises(common.ValidationFailure):
            common.validate_output(self.runtime, home / "new-results")
        (self.runtime / "readarr.db").write_bytes(b"synthetic database marker")
        with self.assertRaises(common.ValidationFailure):
            common.resolve_runtime(self.runtime)
        self.assertFalse((home / "new-results").exists())

    def test_fresh_output_is_private_and_never_reused(self):
        output = common.private_directory(self.root / "output")
        self.assertEqual(output.stat().st_mode & 0o777, 0o700)
        with self.assertRaises(common.ValidationFailure):
            common.private_directory(output)

    def test_nested_portable_application_home_is_rejected_before_snapshot(self):
        data = self.runtime / "data"
        data.mkdir()
        (data / "readarr.db").write_bytes(b"synthetic database marker")
        with self.assertRaises(common.ValidationFailure):
            common.snapshot_runtime(self.runtime)

    def test_snapshot_rejects_external_symlinks_and_detects_content_or_mode_changes(self):
        outside = self.root / "outside"
        outside.write_bytes(b"synthetic outside value")
        link = self.runtime / "escape"
        link.symlink_to(outside)
        with self.assertRaises(common.ValidationFailure):
            common.snapshot_runtime(self.runtime)
        link.unlink()
        before = common.snapshot_runtime(self.runtime)
        path = self.runtime / "Readarr.Core.dll"
        path.chmod(0o600 if path.stat().st_mode & 0o777 != 0o600 else 0o644)
        self.assertNotEqual(before, common.snapshot_runtime(self.runtime))
        before = common.snapshot_runtime(self.runtime)
        path.write_bytes(b"changed synthetic file")
        self.assertNotEqual(before, common.snapshot_runtime(self.runtime))

    def test_private_atomic_report_does_not_leave_temporary_files(self):
        output = common.private_directory(self.root / "output")
        common.write_json(output / "summary.json", {"passed": True})
        self.assertEqual((output / "summary.json").stat().st_mode & 0o777, 0o600)
        self.assertEqual([p.name for p in output.iterdir()], ["summary.json"])

    def test_foreign_apphost_is_rejected_without_execution(self):
        data = bytearray(64)
        data[:6] = b"\x7fELF\x02\x01"
        machine = 183 if platform.machine().lower() in ("x86_64", "amd64") else 62
        struct.pack_into("<H", data, 18, machine)
        (self.runtime / "Readarr").write_bytes(data)
        with self.assertRaises(common.ValidationFailure) as error:
            common.ensure_host_compatible(self.runtime)
        self.assertEqual(error.exception.code, "native_host_architecture_required")


if __name__ == "__main__":
    unittest.main()
