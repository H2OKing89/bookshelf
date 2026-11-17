"""Optional native-architecture tests of the repository's actual S6 Docker image."""

from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

from .common import ValidationFailure, private_directory, snapshot_runtime, validate_output
from .media import build_probe, stage_clone
from .package import LINUX_MACHINES, elf_info, host_machine, runtime_details


OWNER_LABEL = "org.bookshelf.validation.owner"


def _command(arguments: list[str], environment: dict, timeout: int = 30) -> subprocess.CompletedProcess:
    return subprocess.run(arguments, env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          text=True, timeout=timeout)


def _local_docker_environment() -> dict:
    """Use a local Unix socket; never send these builds to a remote Docker host."""
    environment = os.environ.copy()
    if environment.get("DOCKER_CONTEXT") or not environment.get("DOCKER_HOST"):
        inspected = _command(["docker", "context", "inspect", "--format", "{{json .Endpoints.docker.Host}}"], environment)
        if inspected.returncode:
            raise ValidationFailure("docker_context_inspection_failed")
        endpoint = json.loads(inspected.stdout)
    else:
        endpoint = environment["DOCKER_HOST"]
    if not isinstance(endpoint, str) or not endpoint.startswith("unix://"):
        raise ValidationFailure("local_docker_unix_socket_required")
    environment.pop("DOCKER_CONTEXT", None)
    environment["DOCKER_HOST"] = endpoint
    return environment


def _state(identifier: str, environment: dict) -> dict | None:
    result = _command(["docker", "container", "inspect", "--format",
                       '{{json .State}}', identifier], environment, 10)
    if result.returncode == 0:
        try:
            state = json.loads(result.stdout)
        except (ValueError, TypeError):
            raise ValidationFailure("docker_container_state_invalid") from None
        if not isinstance(state, dict) or type(state.get("Running")) is not bool:
            raise ValidationFailure("docker_container_state_invalid")
        return state
    missing = r"\s*(?:error(?: response from daemon)?\s*:\s*)?no such (?:object|container)\s*:\s*"
    if re.fullmatch(missing + re.escape(identifier) + r"\s*", result.stderr, re.I):
        return None
    raise ValidationFailure("docker_container_inspection_failed")


def _owned(identifier: str, owner: str, environment: dict) -> bool:
    result = _command(["docker", "container", "inspect", "--format",
                       '{{json .Config.Labels}}', identifier], environment, 10)
    if result.returncode:
        raise ValidationFailure("docker_owned_container_inspection_failed")
    return json.loads(result.stdout).get(OWNER_LABEL) == owner


def _create(arguments: list[str], image: str, owner: str, environment: dict,
            owned_containers: list[str], command: list[str] | None = None) -> str:
    # Keep the exact owned name until Docker returns its ID. A canceled client
    # can lose the response after the daemon has already created the container.
    names = [arguments[index + 1] for index, value in enumerate(arguments[:-1]) if value == "--name"]
    if len(names) != 1 or not re.fullmatch(r"bookshelf-validation-(?:s6|codecs)-" + re.escape(owner), names[0]):
        raise ValidationFailure("unique_owned_docker_container_name_required")
    name = names[0]
    if _state(name, environment) is not None:
        raise ValidationFailure("owned_docker_container_name_already_exists")
    position = len(owned_containers)
    owned_containers.append(name)
    result = _command(["docker", "create", "--label", OWNER_LABEL + "=" + owner,
                       *arguments, image, *(command or [])], environment)
    if result.returncode or not re.fullmatch(r"[0-9a-f]{64}", result.stdout.strip()):
        raise ValidationFailure("owned_docker_container_creation_failed")
    identifier = result.stdout.strip()
    owned_containers[position] = identifier
    started = _command(["docker", "start", identifier], environment)
    if started.returncode:
        raise ValidationFailure("owned_docker_container_start_failed")
    return identifier


def _write_config(home: Path) -> str:
    key = secrets.token_hex(16)
    values = {"BindAddress": "127.0.0.1", "Port": "8787", "SslPort": "9898", "EnableSsl": "False",
              "UrlBase": "", "LaunchBrowser": "False", "AuthenticationMethod": "None",
              "AuthenticationRequired": "Enabled", "ApiKey": key, "InstanceName": "Bookshelf Local Validation",
              "LogLevel": "Warn", "ConsoleLogLevel": "Warn", "LogSql": "False",
              "UpdateAutomatically": "False", "UpdateMechanism": "External", "Branch": "develop",
              "TrustCgnatIpAddresses": "False", "TrustedNetworks": ""}
    root = ET.Element("Config")
    for name, value in values.items():
        ET.SubElement(root, name).text = value
    path = home / "config.xml"
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
    path.chmod(0o600)
    return key


def _request(identifier: str, key: str, path: str, environment: dict):
    response = _command(["docker", "exec", identifier, "/bin/busybox", "wget", "-q", "-O-", "-T", "3",
                         "--header", "X-Api-Key: " + key, "http://127.0.0.1:8787/api/v1/" + path], environment, 8)
    return json.loads(response.stdout) if response.returncode == 0 else None


def _store_logs(identifier: str, destination: Path, environment: dict) -> None:
    logs = _command(["docker", "logs", "--tail", "300", identifier], environment, 15)
    # Only generated synthetic data can appear here, and the directory is 0700.
    destination.write_text((logs.stdout + logs.stderr)[-2 * 1024 * 1024:])
    destination.chmod(0o600)


def run(runtime: Path, output: Path, repo_root: Path, probe_dll: Path | None = None) -> dict:
    """Build and test a native musl delivery package without publishing anything."""
    checks: dict[str, bool] = {}
    metadata: dict = {"actualRepositoryDockerfile": True, "networkMode": "none", "publishedPorts": False,
                      "nativeArchitectureOnly": True, "jitCompatibilityFlagsUsed": False}
    completed = False
    before = None
    environment = None
    owned_containers: list[str] = []
    owner = uuid.uuid4().hex
    image_tag = "bookshelf-local-validation:" + owner
    image_created = False
    clone = None
    try:
        runtime, output = validate_output(runtime, output)
        private_directory(output)
        before = snapshot_runtime(runtime)
        details = runtime_details(runtime)
        rid = details["rid"]
        if rid not in ("linux-musl-x64", "linux-musl-arm64"):
            raise ValidationFailure("docker_requires_a_musl_delivery_package")
        if host_machine() != LINUX_MACHINES[rid]:
            raise ValidationFailure("docker_requires_native_host_architecture")
        if elf_info(runtime / "Readarr")["machine"] != LINUX_MACHINES[rid]:
            raise ValidationFailure("docker_apphost_architecture_does_not_match_rid")
        environment = _local_docker_environment()
        server = _command(["docker", "info", "--format", "{{.OSType}}/{{.Architecture}}"], environment)
        if server.returncode:
            raise ValidationFailure("local_docker_daemon_unavailable")
        expected_arch = "amd64" if rid.endswith("x64") else "arm64"
        reported = server.stdout.strip().split("/")
        if len(reported) != 2 or reported[0] != "linux" or {
                "x86_64": "amd64", "aarch64": "arm64", "amd64": "amd64", "arm64": "arm64"}.get(reported[1]) != expected_arch:
            raise ValidationFailure("docker_daemon_architecture_mismatch")
        metadata.update(runtimeIdentifier=rid, runtimeFrameworks=details["frameworks"])
        context = output / "context"
        private_directory(context)
        shutil.copytree(repo_root / "docker", context / "docker")
        (context / "scripts").mkdir()
        for name in ("build-mediainfo.sh", "mediainfo.lock"):
            shutil.copy2(repo_root / "scripts" / name, context / "scripts" / name)
        shutil.copytree(repo_root / "scripts/mediainfo-licenses", context / "scripts/mediainfo-licenses")
        shutil.copytree(runtime, context / "_output" / details["tfm"] / rid / "publish")
        shutil.copytree(runtime / "UI", context / "_output/UI")
        metadata["dockerfileSha256"] = hashlib.sha256((repo_root / "docker/Dockerfile").read_bytes()).hexdigest()
        head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=repo_root, capture_output=True,
                              text=True, timeout=10, check=True).stdout.strip()
        with (output / "docker-build.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(["docker", "build", "--platform", "linux/" + expected_arch,
                                     "-f", str(context / "docker/Dockerfile"), "-t", image_tag,
                                     "--build-arg", "GIT_BRANCH=local-validation", "--build-arg", "COMMIT_HASH=" + head,
                                     "--build-arg", "BUILD_DATE=" + datetime.now(timezone.utc).date().isoformat(),
                                     str(context)], env=environment, stdout=log, stderr=subprocess.STDOUT, timeout=900)
        (output / "docker-build.log").chmod(0o600)
        checks["actual_repository_dockerfile_builds"] = result.returncode == 0
        if result.returncode:
            raise ValidationFailure("docker_image_build_failed")
        image_created = True
        image = _command(["docker", "image", "inspect", "--format", "{{.Id}}", image_tag], environment)
        image_id = image.stdout.strip()
        if image.returncode or not re.fullmatch(r"sha256:[0-9a-f]{64}", image_id):
            raise ValidationFailure("built_docker_image_identity_unavailable")
        metadata["builtImageId"] = image_id
        image_environment = _command(["docker", "image", "inspect", "--format", "{{json .Config.Env}}", image_id], environment)
        if image_environment.returncode:
            raise ValidationFailure("built_image_environment_unavailable")
        flags = {"DOTNET_TieredCompilation=0", "COMPlus_TieredCompilation=0", "DOTNET_EnableWriteXorExecute=0",
                 "COMPlus_EnableWriteXorExecute=0"}
        checks["image_does_not_set_emulation_jit_workarounds"] = not flags.intersection(json.loads(image_environment.stdout) or [])
        home = output / "config"
        private_directory(home)
        private_directory(home / "tmp")
        key = _write_config(home)
        identifier = _create(["--name", "bookshelf-validation-s6-" + owner, "--network", "none", "--ulimit", "core=0",
                              "--mount", "type=bind,src=" + str(home) + ",dst=/config", "--env", "PUID=" + str(os.getuid()),
                              "--env", "PGID=" + str(os.getgid()), "--env", "TZ=UTC", "--env", "UPDATE=false"],
                             image_id, owner, environment, owned_containers)
        checks["actual_s6_image_starts"] = True
        status = None
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            state = _state(identifier, environment)
            if state is None or state.get("Running") is not True:
                break
            try:
                status = _request(identifier, key, "system/status", environment)
            except (ValueError, subprocess.TimeoutExpired):
                status = None
            if isinstance(status, dict):
                break
            time.sleep(0.5)
        checks["actual_s6_http_is_healthy"] = isinstance(status, dict)
        if isinstance(status, dict):
            checks["actual_s6_runtime_matches_delivery"] = status.get("runtimeVersion") == details["frameworks"]["Microsoft.NETCore.App"]
            checks["actual_s6_synthetic_library_is_empty"] = all(
                _request(identifier, key, path, environment) == [] for path in ("author", "book", "rootfolder"))
        checks["synthetic_api_key_preserved"] = ET.parse(home / "config.xml").findtext("ApiKey") == key
        if not _owned(identifier, owner, environment):
            raise ValidationFailure("docker_container_ownership_mismatch")
        stopped = _command(["docker", "stop", "--time", "35", identifier], environment, 45)
        state = _state(identifier, environment)
        checks["actual_s6_image_stops_gracefully"] = stopped.returncode == 0 and state is not None and state.get("ExitCode") == 0

        if probe_dll is None:
            probe_dll = build_probe(runtime, output / "probe-build")
        clone = stage_clone(runtime, Path(probe_dll), output / "probe-clone")
        fixture = output / "codec-data"
        private_directory(fixture)
        private_directory(fixture / "tmp")
        codec = _create(["--name", "bookshelf-validation-codecs-" + owner, "--network", "none", "--read-only",
                        "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true", "--memory", "1g", "--cpus", "2",
                        "--pids-limit", "256", "--ulimit", "core=0", "--user", str(os.getuid()) + ":" + str(os.getgid()),
                        "--mount", "type=bind,src=" + str(clone) + ",dst=/probe,readonly",
                        "--mount", "type=bind,src=" + str(fixture) + ",dst=/data", "--tmpfs", "/tmp:rw,nosuid,nodev,size=64m",
                        "--env", "HOME=/data", "--env", "XDG_CONFIG_HOME=/data/xdg", "--env", "TMPDIR=/data/tmp",
                        "--env", "TMP=/data/tmp", "--env", "TEMP=/data/tmp", "--env", "DOTNET_EnableDiagnostics=0",
                        "--workdir", "/probe", "--entrypoint", "/probe/Readarr"],
                       image_id, owner, environment, owned_containers, ["/data"])
        waited = _command(["docker", "wait", codec], environment, 150)
        checks["actual_image_media_probe_exits_successfully"] = waited.returncode == 0 and waited.stdout.strip() == "0"
        report_path = fixture / "native-probe-result.json"
        checks["actual_image_media_report_present"] = report_path.is_file()
        if report_path.is_file():
            report = json.loads(report_path.read_text())
            native_checks = report.get("checks")
            checks["actual_image_media_report_has_boolean_checks"] = isinstance(native_checks, dict) and bool(native_checks) and all(
                isinstance(name, str) and isinstance(value, bool) for name, value in native_checks.items())
            if checks["actual_image_media_report_has_boolean_checks"]:
                for name, value in native_checks.items():
                    checks["actual_image_media_" + name] = value
                checks["actual_image_media_exercises_mediainfo_child"] = (
                    native_checks.get("bundled_mediainfo_child_exits_successfully") is True
                    and native_checks.get("bundled_mediainfo_reads_synthetic_pcm") is True)
                metadata["nativeMediaVersions"] = report.get("metadata", {})
        completed = True
    except Exception as error:
        checks["docker_validation_completed"] = False
        metadata["failure"] = {"type": type(error).__name__, "code": getattr(error, "code", "docker_validation_failed")}
    finally:
        if environment is not None:
            cleanup_ok = True
            for index, identifier in enumerate(owned_containers):
                try:
                    if _state(identifier, environment) is None:
                        continue
                    if not _owned(identifier, owner, environment):
                        cleanup_ok = False
                        continue
                except Exception:
                    cleanup_ok = False
                    continue
                try:
                    _store_logs(identifier, output / ("container-" + str(index) + ".log"), environment)
                except Exception:
                    checks["owned_container_" + str(index) + "_logs_saved"] = False
                try:
                    removed = _command(["docker", "rm", "--force", "--volumes", identifier], environment)
                    cleanup_ok = removed.returncode == 0 and _state(identifier, environment) is None and cleanup_ok
                except Exception:
                    cleanup_ok = False
            checks["all_owned_docker_containers_removed"] = cleanup_ok
            if image_created:
                try:
                    removed = _command(["docker", "image", "rm", image_tag], environment)
                    checks["owned_image_tag_removed"] = removed.returncode == 0
                except Exception:
                    checks["owned_image_tag_removed"] = False
        if before is not None:
            try:
                checks["canonical_candidate_files_unchanged"] = snapshot_runtime(runtime) == before
            except Exception:
                checks["canonical_candidate_files_unchanged"] = False
        metadata["ownedContainerIdentifiers"] = owned_containers
        metadata["containerOwnerLabel"] = {OWNER_LABEL: owner}
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "metadata": metadata}
