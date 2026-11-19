"""Verify published acquisition/completion code with owned synthetic fixtures.

Only a new private package clone receives the probe entry assembly. The probe
uses a private SQLite-backed interface fixture and owned source files; it never
contacts a download client or accepts a saved application home.
"""

from __future__ import annotations

import json
import os
from pathlib import Path
import shutil

from . import media
from .common import (ValidationFailure, ensure_host_compatible, private_directory,
                     snapshot_runtime, validate_output, write_json)


PROJECT = Path(__file__).resolve().parent / "AcquisitionProbe/AcquisitionProbe.csproj"
PROBE_ASSEMBLY = "Bookshelf.AcquisitionProbe.dll"


def build_probe(runtime: Path, output: Path, *, dotnet="dotnet", timeout=180) -> Path:
    runtime, output = validate_output(runtime, output)
    media._frameworks(runtime)
    private_directory(output)
    intermediate = private_directory(output / "obj")
    binaries = private_directory(output / "bin")
    execution = media._execute(
        [dotnet, "build", str(PROJECT), "--configuration", "Release", "--disable-build-servers",
         "-p:CandidateDir=" + media._msbuild_value(str(runtime)),
         "-p:ValidationOutputDir=" + media._msbuild_value(str(output)),
         "-p:BaseIntermediateOutputPath=" + media._msbuild_value(str(intermediate) + os.sep),
         "-p:MSBuildProjectExtensionsPath=" + media._msbuild_value(str(intermediate) + os.sep),
         "-p:OutputPath=" + media._msbuild_value(str(binaries) + os.sep)],
        cwd=PROJECT.parents[3], log=output / "build.log", timeout=timeout)
    probe = binaries / PROBE_ASSEMBLY
    write_json(output / "build-result.json", execution)
    if execution["timedOut"] or execution["exitCode"] != 0 or not probe.is_file():
        raise ValidationFailure("acquisition_probe_build_failed")
    return probe


def stage_clone(runtime: Path, probe: Path, output: Path) -> Path:
    runtime, output = validate_output(runtime, output)
    probe = Path(probe).resolve(strict=True)
    if not probe.is_file() or probe.name != PROBE_ASSEMBLY or probe.is_relative_to(runtime):
        raise ValidationFailure("acquisition_probe_assembly_invalid")
    before = snapshot_runtime(runtime)
    if "Readarr.Application.dll" in before:
        raise ValidationFailure("acquisition_reserved_clone_filename_exists")
    private_directory(output)
    shutil.copytree(runtime, output, dirs_exist_ok=True, symlinks=True)
    output.chmod(0o700)
    original = output / "Readarr.dll"
    if original.is_symlink():
        raise ValidationFailure("acquisition_entry_must_be_regular_file")
    shutil.copy2(original, output / "Readarr.Application.dll")
    replacement = output / ".acquisition-probe-entry.dll"
    if replacement.exists() or replacement.is_symlink():
        raise ValidationFailure("acquisition_reserved_clone_filename_exists")
    shutil.copy2(probe, replacement)
    os.replace(replacement, original)
    after = snapshot_runtime(output)
    if any(after.get(name) != value for name, value in before.items() if name != "Readarr.dll"):
        raise ValidationFailure("acquisition_clone_did_not_preserve_package")
    if snapshot_runtime(runtime) != before:
        raise ValidationFailure("acquisition_input_changed_during_staging")
    write_json(output / "probe-staging-evidence.json", {
        "inputPackageUnchanged": True, "entryOnlyReplacedInOwnedClone": True,
        "preservedPackageFileCount": len(before) - 1})
    return output


def run(runtime: Path, output: Path, *, timeout=120, dotnet="dotnet"):
    runtime, output = validate_output(runtime, output)
    ensure_host_compatible(runtime)
    frameworks = media._frameworks(runtime)
    private_directory(output)
    before = snapshot_runtime(runtime)
    checks, metadata = {}, {"privateSyntheticFixturesOnly": True,
                            "downloadClientsContacted": False,
                            "storageBoundary": "owned SQLite-backed interface fixture",
                            "sourceRetirementBoundary": "published source helper with owned physical filesystem operations"}
    completed = False
    try:
        probe = build_probe(runtime, output / "build", dotnet=dotnet)
        clone = stage_clone(runtime, probe, output / "runtime")
        fixture = private_directory(output / "fixtures")
        private_directory(fixture / "tmp")
        execution = media._execute([str(clone / "Readarr"), str(fixture)], cwd=clone,
                                   log=output / "probe.log", timeout=timeout,
                                   environment=media._candidate_environment(fixture))
        metadata["execution"] = execution
        checks["probe_process_completed_without_timeout"] = execution["timedOut"] is False
        checks["probe_process_exited_successfully"] = execution["exitCode"] == 0
        report = fixture / "acquisition-probe-result.json"
        if not report.is_file() or report.is_symlink() or report.stat().st_size > 1024 * 1024:
            raise ValidationFailure("acquisition_probe_report_missing_or_invalid")
        value = json.loads(report.read_text())
        published = value.get("checks")
        if not isinstance(published, dict) or not published or not all(
                isinstance(name, str) and type(condition) is bool for name, condition in published.items()):
            raise ValidationFailure("acquisition_probe_checks_invalid")
        checks.update(published)
        metadata["publishedProbe"] = value.get("metadata", {})
        checks["runtime_patch_matches_candidate"] = metadata["publishedProbe"].get("runtimeVersion") == frameworks["Microsoft.NETCore.App"]
        completed = execution["timedOut"] is False and execution["exitCode"] == 0
    except Exception as error:
        checks["acquisition_execution_completed"] = False
        metadata["errorType"] = type(error).__name__
        if isinstance(error, ValidationFailure):
            metadata["failureCode"] = error.code
    finally:
        try:
            checks["candidate_package_unchanged"] = snapshot_runtime(runtime) == before
        except Exception:
            checks["candidate_package_unchanged"] = False
    result = {"checks": checks, "passed": sum(checks.values()), "failed": sum(not item for item in checks.values()),
              "completed": completed, "metadata": metadata}
    write_json(output / "result.json", result)
    return result
