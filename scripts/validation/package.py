"""Read-only checks for a packaged Linux Bookshelf runtime."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import platform
import re
import struct
import subprocess

from .common import ValidationFailure, private_directory, snapshot_runtime, validate_output


LINUX_MACHINES = {"linux-x64": 62, "linux-musl-x64": 62, "linux-musl-arm64": 183}
LINUX_INTERPRETERS = {
    "linux-x64": "/lib64/ld-linux-x86-64.so.2",
    "linux-musl-x64": "/lib/ld-musl-x86_64.so.1",
    "linux-musl-arm64": "/lib/ld-musl-aarch64.so.1",
}


def elf_info(path: Path) -> dict:
    """Inspect ELF metadata without running the supplied file or its loader."""
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:6] != b"\x7fELF\x02\x01":
            raise ValidationFailure("expected_little_endian_64_bit_elf")
        machine = struct.unpack_from("<H", header, 18)[0]
        offset = struct.unpack_from("<Q", header, 32)[0]
        entry_size, count = struct.unpack_from("<HH", header, 54)
        if count > 4096 or entry_size < 56:
            raise ValidationFailure("invalid_elf_program_headers")
        interpreter = None
        dynamic = False
        needed_libraries = False
        for index in range(count):
            stream.seek(offset + index * entry_size)
            entry = stream.read(56)
            if len(entry) != 56:
                raise ValidationFailure("truncated_elf_program_headers")
            segment_type = struct.unpack_from("<I", entry)[0]
            if segment_type == 2:  # PT_DYNAMIC
                dynamic = True
                start = struct.unpack_from("<Q", entry, 8)[0]
                length = struct.unpack_from("<Q", entry, 32)[0]
                if length == 0 or length > 1024 * 1024 or length % 16:
                    raise ValidationFailure("invalid_elf_dynamic_segment")
                stream.seek(start)
                data = stream.read(length)
                if len(data) != length:
                    raise ValidationFailure("invalid_elf_dynamic_segment")
                for tag, _ in struct.iter_unpack("<qQ", data):
                    if tag == 0:  # DT_NULL
                        break
                    if tag in (1, 0x7ffffffd, 0x7fffffff):  # DT_NEEDED / DT_AUXILIARY / DT_FILTER
                        needed_libraries = True
                else:
                    raise ValidationFailure("unterminated_elf_dynamic_segment")
            if segment_type == 3:  # PT_INTERP
                start = struct.unpack_from("<Q", entry, 8)[0]
                length = struct.unpack_from("<Q", entry, 32)[0]
                if not 1 <= length <= 4096:
                    raise ValidationFailure("invalid_elf_interpreter")
                stream.seek(start)
                raw = stream.read(length)
                if len(raw) != length or not raw.endswith(b"\0"):
                    raise ValidationFailure("invalid_elf_interpreter")
                interpreter = raw[:-1].decode("utf-8")
        return {"machine": machine, "interpreter": interpreter, "dynamic": dynamic, "neededLibraries": needed_libraries}


def host_machine() -> int | None:
    return {"x86_64": 62, "amd64": 62, "aarch64": 183, "arm64": 183}.get(platform.machine().lower())


def runtime_details(runtime: Path) -> dict:
    options = json.loads((runtime / "Readarr.runtimeconfig.json").read_text())["runtimeOptions"]
    dependencies = json.loads((runtime / "Readarr.deps.json").read_text())
    target = dependencies["runtimeTarget"]["name"]
    rid = target.rsplit("/", 1)[-1]
    frameworks = {item["name"]: item["version"] for item in options.get("includedFrameworks", [])}
    return {"rid": rid, "tfm": options["tfm"], "frameworks": frameworks}


def _sdk_runtime(repo_root: Path, output: Path) -> tuple[str, str]:
    environment = os.environ.copy()
    environment.update(DOTNET_CLI_HOME=str(output / "sdk-home"), DOTNET_NOLOGO="1",
                       DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1")
    version = subprocess.run(["dotnet", "--version"], cwd=repo_root, env=environment,
                             capture_output=True, text=True, timeout=30, check=True).stdout.strip()
    baseline = json.loads((repo_root / "global.json").read_text())["sdk"]["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValidationFailure("invalid_selected_sdk_version")
    selected = tuple(map(int, version.split(".")))
    minimum = tuple(map(int, baseline.split(".")))
    if selected[:2] != minimum[:2] or selected < minimum:
        raise ValidationFailure("selected_sdk_does_not_satisfy_global_json")
    listing = subprocess.run(["dotnet", "--list-sdks"], cwd=repo_root, env=environment,
                             capture_output=True, text=True, timeout=30, check=True).stdout
    directory = None
    for line in listing.splitlines():
        match = re.fullmatch(re.escape(version) + r" \[(.+)\]", line.strip())
        if match:
            directory = Path(match.group(1)) / version
            break
    if directory is None:
        raise ValidationFailure("selected_sdk_location_not_found")
    options = json.loads((directory / "dotnet.runtimeconfig.json").read_text())["runtimeOptions"]
    return version, options["framework"]["version"]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _bundle_checksums(bundle: Path) -> bool:
    entries = (bundle / "SHA256SUMS").read_text().splitlines()
    seen = set()
    for entry in entries:
        match = re.fullmatch(r"([0-9a-f]{64}) [ *](.+)", entry)
        if not match:
            return False
        name = Path(match.group(2))
        if name.is_absolute() or ".." in name.parts or str(name) in seen:
            return False
        seen.add(str(name))
        path = bundle / name
        if path.is_symlink() or not path.is_file() or _sha256(path) != match.group(1):
            return False
    required = {
        "mediainfo", "VERSION", "sources.lock", "build-id",
        "licenses/MediaInfo.txt", "licenses/MediaInfoLib.txt", "licenses/MediaInfoLib-third-party.html",
        "licenses/ZenLib.txt", "licenses/zlib.txt", "licenses/GCC-Runtime-Exception.txt", "licenses/GPL-3.0.txt",
        "licenses/musl.txt", "licenses/README.txt",
        "licenses/aes-gladman-aes.h.txt", "licenses/sha1-gladman-sha1.h.txt", "licenses/sha2-gladman-sha2.h.txt",
        "licenses/hmac-gladman-hmac.h.txt", "licenses/fmt-format.h.txt", "licenses/tinyxml2-tinyxml2.h.txt",
        "licenses/tfsxml-tfsxml.h.txt", "licenses/md5-md5.c.txt", "licenses/base64-base64.h.txt",
    }
    # The helper build includes the source and embedded third-party notices.
    # Require checksum coverage of all delivered notices, not only the binary.
    notices = {str(path.relative_to(bundle)) for path in (bundle / "licenses").rglob("*") if path.is_file()}
    return (required | notices) <= seen


def run(runtime: Path, output: Path, repo_root: Path) -> dict:
    """Check packaging statically; hostExecutable is metadata, never emulation."""
    checks: dict[str, bool] = {}
    metadata: dict = {}
    completed = False
    before = None
    try:
        runtime, output = validate_output(runtime, output)
        private_directory(output)
        before = snapshot_runtime(runtime)
        details = runtime_details(runtime)
        metadata.update(details)
        rid = details["rid"]
        if rid not in LINUX_MACHINES:
            raise ValidationFailure("unsupported_package_runtime_identifier")
        expected_machine = LINUX_MACHINES[rid]
        required = (
            "Readarr", "Readarr.dll", "Readarr.deps.json", "Readarr.runtimeconfig.json",
            "Readarr.Core.dll", "Readarr.Common.dll", "Readarr.Host.dll", "Readarr.Mono.dll",
            "Mono.Posix.NETStandard.dll", "libMonoPosixHelper.so", "libcoreclr.so", "libhostfxr.so",
            "System.Private.CoreLib.dll", "System.Data.SQLite.dll", "libe_sqlite3.so", "UI/index.html", "LICENSE.md",
            "tools/mediainfo/mediainfo", "Readarr.Update/Readarr.Update",
            "Readarr.Update/Readarr.Update.runtimeconfig.json", "Readarr.Update/Readarr.Mono.dll",
            "Readarr.Update/Mono.Posix.NETStandard.dll", "Readarr.Update/libMonoPosixHelper.so",
            "Readarr.Update/libhostfxr.so", "Readarr.Update/libe_sqlite3.so",
        )
        checks["required_application_updater_and_platform_files_present"] = all((runtime / name).is_file() for name in required)
        if not checks["required_application_updater_and_platform_files_present"]:
            raise ValidationFailure("incomplete_delivery_package")
        sdk, runtime_version = _sdk_runtime(repo_root.resolve(), output)
        metadata.update(selectedSdk=sdk, expectedRuntimeVersion=runtime_version)
        checks["application_targets_repository_runtime_family"] = details["tfm"] == "net10.0"
        checks["application_bundles_selected_sdk_runtime_patch"] = details["frameworks"] == {
            "Microsoft.NETCore.App": runtime_version, "Microsoft.AspNetCore.App": runtime_version}
        updater = json.loads((runtime / "Readarr.Update/Readarr.Update.runtimeconfig.json").read_text())["runtimeOptions"]
        checks["updater_bundles_matching_runtime_patch"] = updater["tfm"] == details["tfm"] and {
            item["name"]: item["version"] for item in updater.get("includedFrameworks", [])
        } == {"Microsoft.NETCore.App": runtime_version}
        native = ("Readarr", "libcoreclr.so", "libhostfxr.so", "libMonoPosixHelper.so", "libe_sqlite3.so",
                  "tools/mediainfo/mediainfo", "Readarr.Update/Readarr.Update",
                  "Readarr.Update/libcoreclr.so", "Readarr.Update/libMonoPosixHelper.so",
                  "Readarr.Update/libhostfxr.so", "Readarr.Update/libe_sqlite3.so")
        headers = {name: elf_info(runtime / name) for name in native}
        codecs = list(runtime.glob("*Magick*so"))
        checks["bundled_native_raster_codec_present"] = bool(codecs)
        checks["native_architectures_match_package_rid"] = all(
            item["machine"] == expected_machine for item in headers.values()) and all(
            elf_info(path)["machine"] == expected_machine for path in codecs)
        checks["application_helper_and_updater_are_executable"] = all(
            os.access(runtime / name, os.X_OK) for name in ("Readarr", "tools/mediainfo/mediainfo", "Readarr.Update/Readarr.Update"))
        interpreter = headers["Readarr"]["interpreter"]
        checks["application_and_updater_loaders_match_package_rid"] = (
            interpreter == LINUX_INTERPRETERS[rid]
            and headers["Readarr.Update/Readarr.Update"]["interpreter"] == LINUX_INTERPRETERS[rid]
        )
        helper_header = headers["tools/mediainfo/mediainfo"]
        # Static PIE executables may retain a dynamic relocation segment, but
        # must not request an ELF loader or shared library dependency.
        checks["mediainfo_is_statically_linked"] = helper_header["interpreter"] is None and not helper_header["neededLibraries"]
        metadata["hostExecutable"] = (platform.system() == "Linux" and host_machine() == expected_machine
                                      and (interpreter is None or Path(interpreter).is_file()))
        metadata["applicationElfInterpreter"] = interpreter
        sqlite_lock = json.loads((repo_root / "scripts/sqlite-native.lock.json").read_text())
        sqlite_asset = sqlite_lock["linuxAssets"][rid]
        metadata["bundledSqlite"] = {"engineVersion": sqlite_lock["engineVersion"],
                                    "managedVersion": sqlite_lock["managedVersion"],
                                    "minimumGlibc": sqlite_asset["minimumGlibc"]}
        checks["sqlite_native_asset_matches_pinned_package"] = all(
            _sha256(directory / sqlite_asset["file"]) == sqlite_asset["sha256"]
            for directory in (runtime, runtime / "Readarr.Update")
        )
        dependencies = json.loads((runtime / "Readarr.deps.json").read_text())["libraries"]
        checks["sqlite_managed_and_native_packages_are_pinned"] = all(
            item in dependencies for item in (sqlite_lock["package"] + "/" + sqlite_lock["version"],
                                               sqlite_lock["managedPackage"] + "/" + sqlite_lock["managedVersion"]))
        notices = json.loads((repo_root / "LICENSES/managed/sources.json").read_text())
        checks["managed_licenses_and_notices_match_repository"] = all(
            (runtime / "licenses/managed" / name).is_file()
            and _sha256(runtime / "licenses/managed" / name) == item["sha256"]
            and _sha256(repo_root / "LICENSES/managed" / name) == item["sha256"]
            for name, item in notices.items())
        checks["application_license_matches_repository"] = _sha256(runtime / "LICENSE.md") == _sha256(repo_root / "LICENSE.md")
        checks["mediainfo_bundle_checksums_are_valid"] = _bundle_checksums(runtime / "tools/mediainfo")
        lock = (repo_root / "scripts/mediainfo.lock").read_text()
        version = re.search(r"^MEDIAINFO_VERSION=(\S+)$", lock, re.M)
        helper_version = (runtime / "tools/mediainfo/VERSION").read_text().splitlines()
        helper_rid = "linux-musl-x64" if rid == "linux-x64" else rid
        checks["mediainfo_version_matches_source_lock"] = bool(version and f"MediaInfo CLI {version.group(1)}" in helper_version)
        checks["mediainfo_bundle_matches_helper_rid_and_source_lock"] = (
            f"Runtime {helper_rid}" in helper_version
            and (runtime / "tools/mediainfo/sources.lock").read_bytes() == (repo_root / "scripts/mediainfo.lock").read_bytes()
        )
        checks["linux_package_excludes_windows_wrappers_and_test_assemblies"] = not (
            list(runtime.rglob("*.Test.dll")) or list(runtime.glob("Readarr.Windows.*")))
        checks["six_labors_assemblies_absent"] = not list(runtime.rglob("SixLabors*.dll"))
        completed = True
    except Exception as error:
        checks["package_inspection_completed"] = False
        metadata["failure"] = {"type": type(error).__name__, "code": getattr(error, "code", "package_inspection_failed")}
    finally:
        if before is not None:
            try:
                checks["canonical_candidate_files_unchanged"] = snapshot_runtime(runtime) == before
            except Exception:
                checks["canonical_candidate_files_unchanged"] = False
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "metadata": metadata}
