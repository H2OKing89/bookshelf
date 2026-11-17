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
        for index in range(count):
            stream.seek(offset + index * entry_size)
            entry = stream.read(56)
            if len(entry) != 56:
                raise ValidationFailure("truncated_elf_program_headers")
            if struct.unpack_from("<I", entry)[0] == 3:
                start = struct.unpack_from("<Q", entry, 8)[0]
                length = struct.unpack_from("<Q", entry, 32)[0]
                if not 1 <= length <= 4096:
                    raise ValidationFailure("invalid_elf_interpreter")
                stream.seek(start)
                raw = stream.read(length)
                if len(raw) != length or not raw.endswith(b"\0"):
                    raise ValidationFailure("invalid_elf_interpreter")
                interpreter = raw[:-1].decode("utf-8")
        return {"machine": machine, "interpreter": interpreter}


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
    return {"mediainfo", "VERSION", "sources.lock", "build-id"} <= seen


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
            "System.Private.CoreLib.dll", "System.Data.SQLite.dll", "UI/index.html", "LICENSE.md",
            "tools/mediainfo/mediainfo", "Readarr.Update/Readarr.Update",
            "Readarr.Update/Readarr.Update.runtimeconfig.json", "Readarr.Update/Readarr.Mono.dll",
            "Readarr.Update/Mono.Posix.NETStandard.dll", "Readarr.Update/libMonoPosixHelper.so",
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
        native = ("Readarr", "libcoreclr.so", "libhostfxr.so", "libMonoPosixHelper.so",
                  "tools/mediainfo/mediainfo", "Readarr.Update/Readarr.Update",
                  "Readarr.Update/libcoreclr.so", "Readarr.Update/libMonoPosixHelper.so")
        headers = {name: elf_info(runtime / name) for name in native}
        codecs = list(runtime.glob("*Magick*so"))
        checks["bundled_native_raster_codec_present"] = bool(codecs)
        checks["native_architectures_match_package_rid"] = all(
            item["machine"] == expected_machine for item in headers.values()) and all(
            elf_info(path)["machine"] == expected_machine for path in codecs)
        checks["application_helper_and_updater_are_executable"] = all(
            os.access(runtime / name, os.X_OK) for name in ("Readarr", "tools/mediainfo/mediainfo", "Readarr.Update/Readarr.Update"))
        interpreter = headers["Readarr"]["interpreter"]
        metadata["hostExecutable"] = (platform.system() == "Linux" and host_machine() == expected_machine
                                      and (interpreter is None or Path(interpreter).is_file()))
        metadata["applicationElfInterpreter"] = interpreter
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
        checks["mediainfo_version_matches_source_lock"] = bool(version and version.group(1) in (runtime / "tools/mediainfo/VERSION").read_text())
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
