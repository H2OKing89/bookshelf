"""Exercise the delivery preflight and static native-package boundaries."""

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from validation import package as packaging
from validation.tests.test_common import package


REPOSITORY = Path(__file__).resolve().parents[3]
SUPPORTED_RIDS = ("linux-x64", "linux-musl-x64", "linux-musl-arm64")


class BuildPreflightTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        shutil.copy2(REPOSITORY / "build.sh", self.root / "build.sh")
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.marker = self.root / "build-command-invoked"
        for command in ("sed", "rm", "dotnet", "git", "yarn", "docker", "curl", "cp", "mkdir"):
            tool = self.bin / command
            tool.write_text('#!/bin/sh\necho "$0" >> "$OWNED_BUILD_COMMAND_MARKER"\nexit 41\n')
            tool.chmod(0o700)
        uname = self.bin / "uname"
        uname.write_text('#!/bin/sh\nprintf "%s\\n" "$OWNED_BUILD_HOST"\n')
        uname.chmod(0o700)
        for relative in ("_output/keep", "_tests/keep", "_artifacts/keep", "_cache/keep", "_inno/keep",
                         "src/Directory.Build.props", "distribution/osx/Readarr.app/Contents/Info.plist"):
            location = self.root / relative
            location.parent.mkdir(parents=True, exist_ok=True)
            location.write_bytes(b"owned sentinel")
        self.before = self.snapshot()
        self.environment = {name: os.environ[name] for name in ("PATH", "LANG") if name in os.environ}
        self.environment.update(PATH=str(self.bin) + os.pathsep + self.environment.get("PATH", ""),
                                READARRVERSION="99.99.99.99", BUILD_SOURCEBRANCHNAME="owned-test",
                                OWNED_BUILD_COMMAND_MARKER=str(self.marker), OWNED_BUILD_HOST="Linux")

    def snapshot(self):
        return {str(path.relative_to(self.root)): path.read_bytes()
                for path in self.root.rglob("*") if path.is_file()}

    def invoke(self, *args, **environment):
        return subprocess.run(["bash", "./build.sh", *args], cwd=self.root,
                              env={**self.environment, **environment}, capture_output=True, text=True, timeout=10)

    def assert_early_rejection(self, args, diagnostic, **environment):
        result = self.invoke(*args, **environment)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(diagnostic, result.stderr)
        self.assertFalse(self.marker.exists(), "A build command ran before target rejection")
        self.assertEqual(self.snapshot(), self.before, "Existing outputs or source files changed")

    def test_unsupported_runtime_identifiers_are_rejected_before_build_or_package(self):
        for rid in ("win-x64", "win-x86", "win-arm64", "osx-x64", "osx-arm64", "freebsd-x64",
                    "linux-arm64", "linux-x86", "linux-arm", "linux-musl-arm", "unknown"):
            with self.subTest(rid=rid):
                self.assert_early_rejection(
                    ("--backend", "--frontend", "--packages", "--runtime", rid, "--framework", "net10.0"),
                    "Unsupported Bookshelf package runtime")

    def test_legacy_extra_platform_and_installer_switches_are_rejected_early(self):
        for flag in ("--enable-bsd", "--enable-extra-platforms", "--enable-extra-platforms-in-sdk", "--installer"):
            with self.subTest(flag=flag):
                self.assert_early_rejection(("--backend", "--packages", flag),
                                            "Extra-platform builds and desktop installers are unsupported")
        self.assert_early_rejection(("--backend",), "Extra-platform builds and desktop installers are unsupported",
                                    ENABLE_EXTRA_PLATFORMS="YES")

    def test_target_requires_both_supported_runtime_and_framework(self):
        self.assert_early_rejection(("--packages", "--runtime", "linux-x64"), "Specify both --runtime and --framework")
        self.assert_early_rejection(("--backend", "--framework", "net10.0"), "Specify both --runtime and --framework")
        self.assert_early_rejection(("--backend", "--runtime", "linux-x64", "--framework", "net8.0"),
                                    "Unsupported Bookshelf package framework")

    def test_missing_and_unknown_arguments_cannot_silently_trigger_default_build(self):
        self.assert_early_rejection(("--backend", "--runtime"), "Missing runtime identifier")
        self.assert_early_rejection(("--packages", "--runtime", ""), "Missing runtime identifier")
        self.assert_early_rejection(("--backend", "--runtime", "--packages"), "Missing runtime identifier")
        self.assert_early_rejection(("--backend", "--framework"), "Missing package framework")
        self.assert_early_rejection(("--backend", "--packages", "--runtime=win-x64"), "Unknown build argument")

    def test_non_linux_host_delivery_is_rejected_before_touching_outputs(self):
        for host in ("Darwin", "FreeBSD", "MSYS_NT-10.0"):
            with self.subTest(host=host):
                self.assert_early_rejection(("--backend", "--packages", "--runtime", "linux-x64", "--framework", "net10.0"),
                                            "delivery builds require a Linux host", OWNED_BUILD_HOST=host)

    def test_supported_runtime_selection_reaches_build_preflight_boundary(self):
        for rid in SUPPORTED_RIDS:
            with self.subTest(rid=rid):
                # The command sentinel deliberately stops the build before any
                # filesystem modifications. A 41 proves preflight accepted it.
                result = self.invoke("--backend", "--runtime", rid, "--framework", "net10.0")
                self.assertEqual(result.returncode, 41, result.stdout + result.stderr)
                self.assertTrue(self.marker.exists())
                self.marker.unlink()
                self.assertEqual(self.snapshot(), self.before)

    def test_docker_target_mapping_matches_the_two_musl_delivery_rids(self):
        source = (REPOSITORY / "docker/Dockerfile").read_text()
        targets = dict(re.findall(r'"([^"]+)"\) cp -a /_output/net10\.0/([^/]+)/publish', source))
        self.assertEqual(targets, {"linux/amd64": "linux-musl-x64", "linux/arm64": "linux-musl-arm64"})


class NativePackageMatrixTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def test_declared_delivery_rids_match_build_and_native_validation(self):
        props = (REPOSITORY / "src/Directory.Build.props").read_text()
        match = re.search(r"<RuntimeIdentifiers>([^<]+)</RuntimeIdentifiers>", props)
        self.assertIsNotNone(match)
        self.assertEqual(tuple(match.group(1).split(";")), SUPPORTED_RIDS)
        self.assertEqual(set(packaging.LINUX_MACHINES), set(SUPPORTED_RIDS))
        self.assertEqual(set(packaging.LINUX_INTERPRETERS), set(SUPPORTED_RIDS))
        self.assertEqual(packaging.LINUX_MACHINES["linux-musl-arm64"], 183)

    def test_glibc_arm_native_asset_does_not_make_an_unsupported_delivery_rid_valid(self):
        runtime = package(self.root / "runtime")
        (runtime / "Readarr.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"tfm": "net10.0"}}))
        (runtime / "Readarr.deps.json").write_text(json.dumps({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-arm64"}}))
        with patch.object(packaging, "_sdk_runtime") as sdk:
            report = packaging.run(runtime, self.root / "results", REPOSITORY)
        self.assertFalse(report["completed"])
        self.assertEqual(report["metadata"]["failure"]["code"], "unsupported_package_runtime_identifier")
        sdk.assert_not_called()

    def test_elf_inspection_distinguishes_loader_family_and_static_helper(self):
        def image(name, machine, interpreter=None, dynamic=False, needed=False):
            raw = bytearray(512)
            raw[:6] = b"\x7fELF\x02\x01"
            struct.pack_into("<H", raw, 18, machine)
            struct.pack_into("<Q", raw, 32, 64)
            segments = [(1, None)]
            if interpreter:
                segments.append((3, interpreter.encode() + b"\0"))
            if dynamic:
                dynamic_data = (struct.pack("<qQ", 1, 42) if needed else struct.pack("<qQ", 0x6ffffff9, 1)) + struct.pack("<qQ", 0, 0)
                segments.append((2, dynamic_data))
            struct.pack_into("<HH", raw, 54, 56, len(segments))
            for index, (kind, value) in enumerate(segments):
                start = 64 + index * 56
                struct.pack_into("<I", raw, start, kind)
                if value:
                    location = 256 if kind == 3 else 384
                    struct.pack_into("<Q", raw, start + 8, location)
                    struct.pack_into("<Q", raw, start + 32, len(value))
                    raw[location:location + len(value)] = value
            path = self.root / name
            path.write_bytes(raw)
            return packaging.elf_info(path)

        for rid in SUPPORTED_RIDS:
            with self.subTest(rid=rid):
                loader = packaging.LINUX_INTERPRETERS[rid]
                machine = packaging.LINUX_MACHINES[rid]
                self.assertEqual(image(rid, machine, loader, True, needed=True), {"machine": machine, "interpreter": loader, "dynamic": True, "neededLibraries": True})
        self.assertEqual(image("helper", 62), {"machine": 62, "interpreter": None, "dynamic": False, "neededLibraries": False})
        # Alpine x64 can produce static PIE: .dynamic is present for
        # relocations, but there is neither an interpreter nor DT_NEEDED.
        self.assertEqual(image("static-pie", 62, dynamic=True), {"machine": 62, "interpreter": None, "dynamic": True, "neededLibraries": False})
        self.assertTrue(image("shared-object", 62, dynamic=True, needed=True)["neededLibraries"])

    def test_mediainfo_checksums_cover_required_licenses_and_every_delivered_notice(self):
        bundle = self.root / "bundle"
        bundle.mkdir()
        files = {"mediainfo", "VERSION", "sources.lock", "build-id",
                 "licenses/MediaInfo.txt", "licenses/MediaInfoLib.txt", "licenses/MediaInfoLib-third-party.html",
                 "licenses/ZenLib.txt", "licenses/zlib.txt", "licenses/GCC-Runtime-Exception.txt", "licenses/GPL-3.0.txt",
                 "licenses/musl.txt", "licenses/README.txt",
                 "licenses/aes-gladman-aes.h.txt", "licenses/sha1-gladman-sha1.h.txt", "licenses/sha2-gladman-sha2.h.txt",
                 "licenses/hmac-gladman-hmac.h.txt", "licenses/fmt-format.h.txt", "licenses/tinyxml2-tinyxml2.h.txt",
                 "licenses/tfsxml-tfsxml.h.txt", "licenses/md5-md5.c.txt", "licenses/base64-base64.h.txt"}
        for name in files:
            path = bundle / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("owned file " + name)

        def manifest(names):
            (bundle / "SHA256SUMS").write_text("".join(
                hashlib.sha256((bundle / name).read_bytes()).hexdigest() + "  " + name + "\n" for name in sorted(names)))

        manifest(files)
        self.assertTrue(packaging._bundle_checksums(bundle))
        manifest(files - {"licenses/musl.txt"})
        self.assertFalse(packaging._bundle_checksums(bundle))
        manifest(files)
        extra = "licenses/embedded-library.txt"
        (bundle / extra).write_text("owned extra notice")
        self.assertFalse(packaging._bundle_checksums(bundle))
        manifest(files | {extra})
        self.assertTrue(packaging._bundle_checksums(bundle))
        (bundle / "licenses/ZenLib.txt").unlink()
        self.assertFalse(packaging._bundle_checksums(bundle))
        manifest((files | {extra}) - {"licenses/ZenLib.txt"})
        self.assertFalse(packaging._bundle_checksums(bundle), "Removing a required notice and its checksum cannot make the bundle complete")


if __name__ == "__main__":
    unittest.main()
