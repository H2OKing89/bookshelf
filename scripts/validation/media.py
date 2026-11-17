"""Run synthetic media checks through the published application's runtime.

Only a new private clone receives the probe entry assembly. The input package
is hashed before and after execution, and its DLLs/native libraries are copied
without substitution. No installed MediaInfo/ImageMagick command is used.
"""

from __future__ import annotations

from contextlib import contextmanager
import json
import os
from pathlib import Path
import platform
import signal
import subprocess
import time
from typing import Any

from .common import (
    ValidationFailure,
    ensure_host_compatible,
    private_directory,
    resolve_runtime,
    snapshot_runtime,
    validate_output,
)

PROJECT = Path(__file__).resolve().parent / "MediaProbe" / "MediaProbe.csproj"
PROBE_ASSEMBLY = "Bookshelf.MediaProbe.dll"


def _write_json(path: Path, value: Any) -> None:
    with path.open("x", encoding="utf-8") as stream:
        os.chmod(path, 0o600)
        json.dump(value, stream, indent=2)
        stream.write("\n")


def _group_has_running_members(group_id: int) -> bool:
    for directory in Path("/proc").iterdir():
        if not directory.name.isdecimal():
            continue
        try:
            # comm may contain spaces and parentheses; fields after its final
            # closing parenthesis start with state, ppid, pgrp and session.
            fields = (directory / "stat").read_text(errors="replace").rpartition(")")[2].split()
            if int(fields[2]) == group_id and int(fields[3]) == group_id and fields[0] not in ("Z", "X"):
                return True
        except (OSError, ValueError, IndexError):
            continue
    return False


def _wait_group_stopped(group_id: int, timeout: float) -> bool:
    deadline = time.monotonic() + timeout
    while _group_has_running_members(group_id):
        if time.monotonic() >= deadline:
            return False
        time.sleep(0.02)
    return True


@contextmanager
def _defer_termination_signals():
    previous = signal.pthread_sigmask(signal.SIG_BLOCK, {signal.SIGINT, signal.SIGTERM})
    try:
        yield
    finally:
        signal.pthread_sigmask(signal.SIG_SETMASK, previous)


def _stop(process: subprocess.Popen, group_id: int) -> None:
    """Stop the recorded group before reaping its leader.

    _execute observes leader exit with WNOWAIT. Keeping that child unreaped
    reserves its PID while TERM/KILL target the group, preventing a recycled
    unrelated PID/group from being signalled after a fast leader exit.
    """
    with _defer_termination_signals():
        stopped = False
        try:
            try:
                os.killpg(group_id, signal.SIGTERM)
            except ProcessLookupError:
                pass
            stopped = _wait_group_stopped(group_id, 5)
            if not stopped:
                try:
                    os.killpg(group_id, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                stopped = _wait_group_stopped(group_id, 5)
        finally:
            # Only now may wait() reap the reserved leader PID. Grandchildren
            # that became orphans are terminated above and reaped by their adopter.
            process.wait(timeout=5)
        if not stopped:
            raise ValidationFailure("media_owned_process_group_cleanup_failed")


def _wait_leader_without_reaping(process: subprocess.Popen, timeout: float) -> bool:
    deadline = time.monotonic() + timeout
    while os.waitid(os.P_PID, process.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT) is None:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return False
        time.sleep(min(remaining, 0.02))
    return True


def _disable_core_dumps() -> None:
    import resource

    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    # The parent defers termination while recording the new group. Children
    # restore normal termination handling before executing their command.
    signal.pthread_sigmask(signal.SIG_UNBLOCK, {signal.SIGINT, signal.SIGTERM})


def _execute(command: list[str], *, cwd: Path, log: Path, timeout: float,
             environment: dict[str, str] | None = None) -> dict[str, Any]:
    if timeout <= 0:
        raise ValidationFailure("media_timeout_must_be_positive")
    if not hasattr(os, "WNOWAIT") or not Path("/proc").is_dir():
        raise ValidationFailure("media_linux_process_tracking_required")
    if signal.getsignal(signal.SIGCHLD) != signal.SIG_DFL:
        raise ValidationFailure("media_requires_waitable_owned_children")
    process = None
    group_id = None
    timed_out = False
    with log.open("xb") as output:
        os.chmod(log, 0o600)
        try:
            with _defer_termination_signals():
                process = subprocess.Popen(
                    command, cwd=cwd, env=environment, stdout=output,
                    stderr=subprocess.STDOUT, start_new_session=True,
                    preexec_fn=_disable_core_dumps,
                )
                group_id = process.pid  # start_new_session makes this its PGID/SID.
            timed_out = not _wait_leader_without_reaping(process, timeout)
        finally:
            if process is not None:
                _stop(process, group_id)
    return {"exitCode": process.returncode, "timedOut": timed_out}


def _frameworks(runtime: Path) -> dict[str, str]:
    try:
        options = json.loads((runtime / "Readarr.runtimeconfig.json").read_text())["runtimeOptions"]
        result = {item["name"]: item["version"] for item in options.get("includedFrameworks", [])}
        if not result.get("Microsoft.NETCore.App", "").startswith("10."):
            raise ValidationFailure("media_requires_self_contained_net10_package")
        return result
    except (KeyError, TypeError, ValueError, OSError) as error:
        raise ValidationFailure("media_runtime_config_invalid") from error


def _msbuild_value(value: str) -> str:
    # A subprocess argv does not prevent MSBuild's own semicolon/property
    # parsing. Escape literal paths before passing them as global properties.
    reserved = set("%*?@$();'\"")
    return "".join("%" + format(ord(character), "02X") if character in reserved else character
                   for character in value)


def build_probe(runtime: Path, output: Path, *, dotnet: str = "dotnet",
                timeout: float = 180) -> Path:
    """Build direct references against runtime into a NEW owned directory.

    Both MSBuild intermediates and outputs stay under output. This function
    invokes the local SDK; it never writes to the candidate or source project.
    """
    runtime, output = validate_output(runtime, output)
    _frameworks(runtime)
    private_directory(output)
    intermediate = output / "obj"
    binaries = output / "bin"
    private_directory(intermediate)
    private_directory(binaries)
    completed = _execute(
        [dotnet, "build", str(PROJECT), "--configuration", "Release", "--disable-build-servers",
         "-p:CandidateDir=" + _msbuild_value(str(runtime)),
         "-p:ValidationOutputDir=" + _msbuild_value(str(output)),
         "-p:BaseIntermediateOutputPath=" + _msbuild_value(str(intermediate) + os.sep),
         "-p:MSBuildProjectExtensionsPath=" + _msbuild_value(str(intermediate) + os.sep),
         "-p:OutputPath=" + _msbuild_value(str(binaries) + os.sep)],
        cwd=PROJECT.parents[3], log=output / "build.log", timeout=timeout,
    )
    probe = binaries / PROBE_ASSEMBLY
    _write_json(output / "build-result.json", completed)
    if completed["timedOut"] or completed["exitCode"] != 0 or not probe.is_file():
        raise ValidationFailure("media_probe_build_failed")
    return probe


def stage_clone(runtime: Path, probe_dll: Path, output: Path) -> Path:
    """Clone the package into NEW output and replace only its entry DLL."""
    import shutil

    runtime, output = validate_output(runtime, output)
    probe_dll = Path(probe_dll).resolve(strict=True)
    if probe_dll.name != PROBE_ASSEMBLY or not probe_dll.is_file():
        raise ValidationFailure("media_probe_assembly_invalid")
    if probe_dll == runtime or runtime in probe_dll.parents:
        raise ValidationFailure("media_probe_must_be_outside_candidate")
    before = snapshot_runtime(runtime)
    if "Readarr.Application.dll" in before:
        raise ValidationFailure("media_reserved_clone_filename_exists")
    private_directory(output)
    # Preserve package-internal links; snapshot_runtime rejects escaping links.
    shutil.copytree(runtime, output, dirs_exist_ok=True, symlinks=True)
    output.chmod(0o700)
    original = output / "Readarr.dll"
    if original.is_symlink():
        raise ValidationFailure("media_entry_assembly_must_be_a_regular_file")
    shutil.copy2(original, output / "Readarr.Application.dll")
    replacement = output / ".media-probe-entry.dll"
    if replacement.exists() or replacement.is_symlink():
        raise ValidationFailure("media_reserved_clone_filename_exists")
    shutil.copy2(probe_dll, replacement)
    os.replace(replacement, original)
    cloned = snapshot_runtime(output)
    preserved = {name: value for name, value in before.items() if name != "Readarr.dll"}
    if any(cloned.get(name) != value for name, value in preserved.items()):
        raise ValidationFailure("media_clone_did_not_preserve_package")
    if snapshot_runtime(runtime) != before:
        raise ValidationFailure("media_candidate_changed_during_staging")
    _write_json(output / "probe-staging-evidence.json", {
        "originalCandidateUnchanged": True,
        "probeEntryOnlyReplacedInOwnedClone": True,
        "candidateSnapshot": before,
        "preservedPackageFileCount": len(preserved),
    })
    return output


# Retained as an explicitly named staging operation for other local runners.
stage_probe = stage_clone


def _candidate_environment(fixture: Path) -> dict[str, str]:
    # Do not inherit app connection settings, loader injection, JIT overrides,
    # proxy tokens or a host-framework selection into a packaged-runtime check.
    environment = {
        name: os.environ[name]
        for name in ("PATH", "LANG", "LC_ALL", "TZ")
        if name in os.environ
    }
    environment.update({
        "HOME": str(fixture), "TMPDIR": str(fixture / "tmp"),
        "TMP": str(fixture / "tmp"), "TEMP": str(fixture / "tmp"),
        "DOTNET_EnableDiagnostics": "0",
    })
    return environment


def run(runtime: Path, output: Path, *, timeout: float = 120,
        dotnet: str = "dotnet") -> dict[str, Any]:
    """Build and execute this host's packaged native/media checks."""
    runtime, output = validate_output(runtime, output)
    runtime = resolve_runtime(runtime)
    ensure_host_compatible(runtime)
    frameworks = _frameworks(runtime)
    private_directory(output)
    before = snapshot_runtime(runtime)
    checks: dict[str, bool] = {}
    metadata: dict[str, Any] = {
        "hostArchitecture": platform.machine(),
        "expectedFrameworks": frameworks,
        "calibreVerification": "production client with a synthetic HTTP response",
        "privateSyntheticFixturesOnly": True,
    }
    completed = False
    try:
        probe = build_probe(runtime, output / "build", dotnet=dotnet)
        clone = stage_clone(runtime, probe, output / "runtime")
        metadata.update(probeDll=str(probe), cloneDirectory=str(clone))
        fixture = output / "fixtures"
        private_directory(fixture)
        private_directory(fixture / "tmp")
        execution = _execute(
            [str(clone / "Readarr"), str(fixture)], cwd=clone,
            log=output / "probe.log", timeout=timeout,
            environment=_candidate_environment(fixture),
        )
        metadata["execution"] = execution
        checks["probe_process_completed_without_timeout"] = not execution["timedOut"]
        checks["probe_process_exited_successfully"] = execution["exitCode"] == 0
        result_path = fixture / "native-probe-result.json"
        if not result_path.is_file() or result_path.is_symlink() or result_path.stat().st_size > 1024 * 1024:
            raise ValidationFailure("media_probe_report_missing_or_invalid")
        result = json.loads(result_path.read_text())
        native = result.get("checks")
        if not isinstance(native, dict) or not native or not all(
            isinstance(name, str) and isinstance(value, bool) for name, value in native.items()
        ):
            raise ValidationFailure("media_probe_checks_missing_or_invalid")
        checks.update(native)
        reported = result.get("metadata", {})
        metadata["nativeProbe"] = reported
        checks["runtime_patch_matches_candidate"] = (
            reported.get("runtimeVersion") == frameworks["Microsoft.NETCore.App"]
        )
        completed = not execution["timedOut"] and execution["exitCode"] == 0
    except Exception as error:
        checks["media_execution_completed"] = False
        metadata["errorType"] = type(error).__name__
        if isinstance(error, ValidationFailure):
            metadata["failureCode"] = str(error)
    finally:
        try:
            checks["candidate_package_unchanged"] = snapshot_runtime(runtime) == before
        except Exception:
            checks["candidate_package_unchanged"] = False
    result = {
        "checks": checks, "metadata": metadata,
        "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
        "completed": completed,
    }
    _write_json(output / "result.json", result)
    return result
