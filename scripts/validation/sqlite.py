"""Measure the published SQLite provider using owned synthetic workload files."""

from __future__ import annotations

import json
import os
from pathlib import Path
import shutil

from . import media
from .common import (ValidationFailure, ensure_host_compatible, file_sha256, private_directory,
                     snapshot_runtime, validate_output, write_json)


PROJECT = Path(__file__).resolve().parent / "SqliteProbe" / "SqliteProbe.csproj"
PROBE_ASSEMBLY = "Bookshelf.SqliteProbe.dll"


def _frameworks(runtime: Path) -> dict[str, str]:
    """This probe targets .NET 10, including its pre-P13 provider baseline."""
    try:
        options = json.loads((runtime / "Readarr.runtimeconfig.json").read_text())["runtimeOptions"]
        result = {item["name"]: item["version"] for item in options.get("includedFrameworks", [])}
        core = result.get("Microsoft.NETCore.App")
        if options.get("tfm") != "net10.0" or not isinstance(core, str) or not core.startswith("10."):
            raise ValidationFailure("sqlite_requires_self_contained_net10_package")
        return result
    except (KeyError, TypeError, ValueError, OSError) as error:
        raise ValidationFailure("sqlite_runtime_config_invalid") from error


def build_probe(runtime: Path, output: Path, *, dotnet="dotnet") -> Path:
    runtime, output = validate_output(runtime, output)
    _frameworks(runtime)
    private_directory(output)
    intermediate = private_directory(output / "obj")
    binaries = private_directory(output / "bin")
    result = media._execute([
        dotnet, "build", str(PROJECT), "--configuration", "Release", "--disable-build-servers",
        "-p:CandidateDir=" + media._msbuild_value(str(runtime)),
        "-p:ValidationOutputDir=" + media._msbuild_value(str(output)),
        "-p:BaseIntermediateOutputPath=" + media._msbuild_value(str(intermediate) + os.sep),
        "-p:MSBuildProjectExtensionsPath=" + media._msbuild_value(str(intermediate) + os.sep),
        "-p:OutputPath=" + media._msbuild_value(str(binaries) + os.sep),
    ], cwd=PROJECT.parents[3], log=output / "build.log", timeout=180)
    write_json(output / "build-result.json", result)
    probe = binaries / PROBE_ASSEMBLY
    if result["timedOut"] or result["exitCode"] != 0 or not probe.is_file():
        raise ValidationFailure("sqlite_probe_build_failed")
    return probe


def stage_probe(runtime: Path, probe: Path, output: Path) -> Path:
    """Use the existing guarded clone operation, without altering shared globals.

    Its accepted source filename is an implementation detail; the entry bytes
    remain the newly compiled SQLite probe, not the media probe.
    """
    if probe.name != PROBE_ASSEMBLY or not probe.is_file():
        raise ValidationFailure("sqlite_probe_assembly_invalid")
    alias = private_directory(output.parent / (output.name + "-entry"))
    entry = alias / media.PROBE_ASSEMBLY
    shutil.copy2(probe, entry)
    return media.stage_clone(runtime, entry, output)


def read_report(path: Path) -> dict:
    if not path.is_file() or path.is_symlink() or path.stat().st_size > 1024 * 1024:
        raise ValidationFailure("sqlite_probe_report_missing_or_invalid")
    try:
        report = json.loads(path.read_text())
    except (ValueError, UnicodeError):
        raise ValidationFailure("sqlite_probe_report_missing_or_invalid") from None
    if not isinstance(report, dict):
        raise ValidationFailure("sqlite_probe_report_missing_or_invalid")
    values = report.get("checks")
    if not isinstance(values, dict) or not values or not all(isinstance(k, str) and type(v) is bool for k, v in values.items()):
        raise ValidationFailure("sqlite_probe_checks_invalid")
    if not isinstance(report.get("metadata"), dict):
        raise ValidationFailure("sqlite_probe_metadata_invalid")
    return report


def missing_native_probe(runtime: Path, probe: Path, output: Path, *, timeout=20) -> dict:
    """Remove only the bundled engine in an owned clone; never alter the input."""
    private_directory(output)
    clone = stage_probe(runtime, probe, output / "runtime")
    removed = {}
    for path in clone.rglob("libe_sqlite3.so"):
        removed[str(path.relative_to(clone))] = file_sha256(path)
        path.unlink()
    if not removed:
        raise ValidationFailure("sqlite_missing_native_requires_bundled_engine")
    fixture = private_directory(output / "fixtures")
    private_directory(fixture / "tmp")
    execution = media._execute([str(clone / "Readarr"), str(fixture)], cwd=clone,
                               log=output / "probe.log", timeout=timeout,
                               environment=media._candidate_environment(fixture))
    report = read_report(fixture / "sqlite-probe-result.json")
    metadata = report["metadata"]
    failed_closed = (not execution["timedOut"] and execution["exitCode"] == 1
                     and "DllNotFoundException" in metadata.get("failureChain", [])
                     and metadata.get("nativeSqliteMappings") == []
                     and report["checks"].get("probe_completed") is False)
    result = {"checks": {"missing_bundle_fails_closed_without_host_fallback": failed_closed},
              "removedBundledFiles": removed, "execution": execution, "probe": metadata}
    write_json(output / "result.json", result)
    return result


def run_one(runtime: Path, output: Path, *, dotnet="dotnet", timeout=90) -> dict:
    runtime, output = validate_output(runtime, output)
    ensure_host_compatible(runtime)
    frameworks = _frameworks(runtime)
    private_directory(output)
    before = snapshot_runtime(runtime)
    checks = {}
    metadata = {"syntheticDataOnly": True, "expectedFrameworks": frameworks}
    completed = False
    try:
        probe = build_probe(runtime, output / "build", dotnet=dotnet)
        clone = stage_probe(runtime, probe, output / "runtime")
        fixture = private_directory(output / "fixtures")
        private_directory(fixture / "tmp")
        execution = media._execute([str(clone / "Readarr"), str(fixture)], cwd=clone,
                                   log=output / "probe.log", timeout=timeout,
                                   environment=media._candidate_environment(fixture))
        metadata["execution"] = execution
        checks["probe_exits_without_timeout"] = not execution["timedOut"]
        checks["probe_exits_successfully"] = execution["exitCode"] == 0
        report = read_report(fixture / "sqlite-probe-result.json")
        checks.update(report["checks"])
        metadata["probe"] = report["metadata"]
        checks["runtime_patch_matches_package"] = metadata["probe"].get("runtimeVersion") == frameworks["Microsoft.NETCore.App"]
        completed = not execution["timedOut"] and execution["exitCode"] == 0
        bundled = list(runtime.rglob("libe_sqlite3.so"))
        if bundled:
            mapped = metadata["probe"].get("nativeSqliteChecksums", {})
            expected = {str(clone / path.relative_to(runtime)): file_sha256(path) for path in bundled}
            checks["mapped_engine_matches_owned_bundle"] = bool(mapped) and all(expected.get(path) == digest for path, digest in mapped.items())
            negative = missing_native_probe(runtime, probe, output / "missing-native", timeout=min(timeout, 20))
            metadata["missingBundleNegative"] = negative
            checks.update(negative["checks"])
        else:
            metadata["missingBundleNegative"] = {"notApplicable": "This baseline predates the bundled engine resolver."}
    except Exception as error:
        checks["sqlite_execution_completed"] = False
        metadata["errorType"] = type(error).__name__
        if isinstance(error, ValidationFailure):
            metadata["failureCode"] = error.code
    finally:
        try:
            checks["input_package_unchanged"] = snapshot_runtime(runtime) == before
        except Exception:
            checks["input_package_unchanged"] = False
    result = {"checks": checks, "passed": sum(checks.values()),
              "failed": sum(not v for v in checks.values()), "completed": completed,
              "metadata": metadata}
    write_json(output / "result.json", result)
    return result


def run(runtime: Path, output: Path, *, baseline_runtime: Path | None = None,
        dotnet="dotnet", timeout=90) -> dict:
    runtime, output = validate_output(runtime, output)
    _frameworks(runtime)
    if baseline_runtime is not None:
        baseline_runtime, _ = validate_output(baseline_runtime, output)
        if baseline_runtime == runtime:
            raise ValidationFailure("sqlite_comparison_requires_distinct_baseline")
        _frameworks(baseline_runtime)
    private_directory(output)
    results = {}
    if baseline_runtime is not None:
        results["baseline"] = run_one(baseline_runtime, output / "baseline", dotnet=dotnet, timeout=timeout)
    results["candidate"] = run_one(runtime, output / "candidate", dotnet=dotnet, timeout=timeout)
    checks = {label + "_" + key: value for label, result in results.items() for key, value in result["checks"].items()}
    report = {"checks": checks, "passed": sum(checks.values()), "failed": sum(not v for v in checks.values()),
              "completed": all(result["completed"] for result in results.values()),
              "results": results, "syntheticDataOnly": True,
              "comparisonInterpretation": "Native busy timeout and managed command timeout are separate; timings describe this workload rather than a throughput guarantee."}
    write_json(output / "result.json", report)
    return report
