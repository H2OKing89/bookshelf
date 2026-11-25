"""Synthetic, owned certificate and restart fixtures; no external endpoints."""

from __future__ import annotations

import hashlib
import http.client
import json
import os
from pathlib import Path
import secrets
import signal
import ssl
import sqlite3
import subprocess
import time

from .app import Fixture, result, safe_failure
from .common import ValidationFailure, private_directory, snapshot_runtime, validate_output


def openssl(folder: Path, *args: str, password: str | None = None) -> None:
    subprocess.run(["openssl", *args], cwd=folder, check=True, timeout=20,
                   input=(password + "\n").encode() if password is not None else b"",
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def certificates(folder: Path, password: str) -> dict[str, Path]:
    folder = private_directory(folder)
    openssl(folder, "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", "root.key", "-out", "root.pem",
            "-days", "2", "-subj", "/CN=Owned Synthetic Root", "-addext", "basicConstraints=critical,CA:TRUE",
            "-addext", "keyUsage=critical,keyCertSign,cRLSign")
    openssl(folder, "req", "-newkey", "rsa:2048", "-nodes", "-keyout", "intermediate.key", "-out", "intermediate.csr",
            "-subj", "/CN=Owned Synthetic Intermediate")
    (folder / "intermediate.ext").write_text("basicConstraints=critical,CA:TRUE,pathlen:0\nkeyUsage=critical,keyCertSign,cRLSign\n")
    openssl(folder, "x509", "-req", "-in", "intermediate.csr", "-CA", "root.pem", "-CAkey", "root.key",
            "-CAcreateserial", "-out", "intermediate.pem", "-days", "1", "-extfile", "intermediate.ext")
    openssl(folder, "req", "-newkey", "rsa:2048", "-nodes", "-keyout", "leaf.key", "-out", "leaf.csr",
            "-subj", "/CN=127.0.0.1")
    (folder / "leaf.ext").write_text("basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n"
                                      "extendedKeyUsage=serverAuth\nsubjectAltName=IP:127.0.0.1\n")
    openssl(folder, "x509", "-req", "-in", "leaf.csr", "-CA", "intermediate.pem", "-CAkey", "intermediate.key",
            "-CAcreateserial", "-out", "leaf.pem", "-days", "1", "-extfile", "leaf.ext")
    openssl(folder, "pkcs8", "-topk8", "-in", "leaf.key", "-out", "encrypted.key", "-v2", "aes-256-cbc", "-passout", "stdin", password=password)
    openssl(folder, "pkcs12", "-export", "-in", "leaf.pem", "-inkey", "leaf.key", "-certfile", "intermediate.pem",
            "-out", "legacy.p12", "-passout", "stdin", password=password)
    paths = {name: folder / name for name in ("root.pem", "root.key", "intermediate.pem", "leaf.pem", "leaf.key", "encrypted.key", "legacy.p12")}
    paths["chain.pem"] = folder / "chain.pem"
    paths["chain.pem"].write_bytes(paths["leaf.pem"].read_bytes() + paths["intermediate.pem"].read_bytes())
    paths["combined.pem"] = folder / "combined.pem"
    paths["combined.pem"].write_bytes(paths["intermediate.pem"].read_bytes() + paths["leaf.pem"].read_bytes() + paths["encrypted.key"].read_bytes())
    paths["legacy-trust.pem"] = folder / "legacy-trust.pem"
    paths["legacy-trust.pem"].write_bytes(paths["root.pem"].read_bytes() + paths["intermediate.pem"].read_bytes())
    for path in folder.iterdir():
        path.chmod(0o600)
    return paths


def tls_status(fixture: Fixture, trust: Path) -> int:
    # Only the root is trusted in candidate checks: the intermediate must be
    # transmitted by Kestrel, not fetched by AIA or trusted by the test client.
    context = ssl.create_default_context(cafile=str(trust))
    connection = http.client.HTTPSConnection("127.0.0.1", int(fixture.xml_value("SslPort")), context=context, timeout=5)
    try:
        connection.request("GET", fixture.URL_BASE + "/api/v1/system/status", headers={"X-Api-Key": fixture.api_key})
        response = connection.getresponse()
        response.read()
        return response.status
    finally:
        connection.close()


def save(fixture: Fixture, settings: dict, *omit: str):
    config = fixture.api("config/host")
    config.update(settings)
    for name in omit:
        config.pop(name, None)
    return fixture.request(fixture.URL_BASE + "/api/v1/config/host/1", "PUT", config, key=True)


def restart(fixture: Fixture, trust: Path) -> None:
    old_pid = int((fixture.home / "readarr.pid").read_text())
    owned_group = fixture.process.pid
    fixture.check("restart_is_requested_on_owned_process", os.getpgid(old_pid) == owned_group)
    old_start = fixture.api("system/status")["startTime"]
    status, _, _ = fixture.request(fixture.URL_BASE + "/api/v1/system/restart", "POST", {}, key=True)
    fixture.check("restart_request_accepted", status in (200, 202))
    deadline = time.monotonic() + fixture.startup_timeout
    while time.monotonic() < deadline:
        try:
            next_pid = int((fixture.home / "readarr.pid").read_text())
            if (next_pid != old_pid and os.getpgid(next_pid) == owned_group and
                    fixture.request(fixture.URL_BASE + "/api/v1/system/status", key=True, timeout=2)[0] == 200 and
                    fixture.api("system/status")["startTime"] != old_start and tls_status(fixture, trust) == 200):
                return
        except (OSError, ValueError, http.client.HTTPException):
            pass
        time.sleep(0.2)
    raise ValidationFailure("owned_restart_did_not_become_healthy")


def stop_replacement(fixture: Fixture) -> None:
    # Only the process group started by Fixture is signalled; the pid file is
    # used to observe that its replacement has stopped, never as a kill target.
    pid = int((fixture.home / "readarr.pid").read_text())
    fixture.check("replacement_remains_in_owned_group", os.getpgid(pid) == fixture.process.pid)
    os.killpg(fixture.process.pid, signal.SIGTERM)
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        try:
            state = (Path("/proc") / str(pid) / "stat").read_text().rsplit(")", 1)[1].strip().split()[0]
            if state == "Z":
                break
        except FileNotFoundError:
            break
        time.sleep(0.1)
    else:
        raise ValidationFailure("owned_replacement_did_not_exit_after_sigterm")
    fixture.stop()
    fixture.check("original_restart_process_exited_normally", fixture.exit_codes[-1] == 0)
    try:
        fixture.request(fixture.URL_BASE + "/api/v1/system/status", key=True, timeout=0.5)
    except (OSError, http.client.HTTPException):
        fixture.check("http_port_released_after_sigterm", True)
    else:
        fixture.check("http_port_released_after_sigterm", False)


def run(runtime: Path, output: Path, baseline_runtime: Path | None = None) -> dict:
    runtime, output = validate_output(runtime, output)
    if baseline_runtime is not None:
        baseline_runtime, _ = validate_output(baseline_runtime, output)
        if baseline_runtime == runtime:
            raise ValidationFailure("baseline_runtime_must_differ_from_candidate")
    private_directory(output)
    candidate_snapshot = snapshot_runtime(runtime)
    baseline_snapshot = snapshot_runtime(baseline_runtime) if baseline_runtime else None
    fixture = Fixture(baseline_runtime or runtime, output / "application")
    password = "owned-" + secrets.token_hex(20)
    stage = "certificate_generation"
    failure = None
    completed = False
    try:
        paths = certificates(output / "certificates with spaces", password)
        original = {path: hashlib.sha256(path.read_bytes()).digest() for path in paths.values()}
        stage = "pfx_compatibility"
        fixture.configure_home()
        fixture.set_xml({"EnableSsl": "True", "SslCertPath": str(paths["legacy.p12"]), "SslCertPassword": password,
                         "LogLevel": "Info", "ConsoleLogLevel": "Info"})
        fixture.start()
        fixture.check("pfx_http_listener_works", fixture.api("system/status") is not None)
        fixture.check("initial_password_protected_pfx_https", tls_status(fixture, paths["legacy-trust.pem"]) == 200)
        fixture.stop()
        fixture.runtime = runtime
        # Include both separators that previously broke preserved arguments.
        new_home = fixture.home.with_name(fixture.home.name + " with spaces=edition")
        fixture.home.rename(new_home)
        fixture.home = new_home
        fixture.private_tmp = new_home / "tmp"
        fixture.start()
        fixture.check("candidate_pfx_https_sends_intermediate", tls_status(fixture, paths["root.pem"]) == 200)
        fixture.check("candidate_pfx_credential_is_masked", fixture.api("config/host").get("sslCertPassword") == "********")
        stage = "pem_separate"
        saved = save(fixture, {"sslCertPath": str(paths["chain.pem"]), "sslKeyPath": str(paths["leaf.key"]), "sslCertPassword": ""})
        fixture.check("separate_pem_can_be_saved", saved[0] in (200, 202))
        fixture.stop()
        fixture.start()
        fixture.check("separate_pem_tls_and_chain", tls_status(fixture, paths["root.pem"]) == 200)
        stage = "pem_encrypted"
        saved = save(fixture, {"sslKeyPath": str(paths["encrypted.key"]), "sslCertPassword": password})
        fixture.check("encrypted_private_key_can_be_saved", saved[0] in (200, 202))
        fixture.stop()
        fixture.start()
        fixture.check("encrypted_pem_tls_and_chain", tls_status(fixture, paths["root.pem"]) == 200)
        fixture.check("encrypted_key_password_masked", fixture.api("config/host").get("sslCertPassword") == "********")
        saved = save(fixture, {}, "sslKeyPath", "sslCertPassword", "performanceProfilingEnabled")
        fixture.check("legacy_client_omission_retains_key_and_password", saved[0] in (200, 202) and
                      fixture.xml_value("SslKeyPath") == str(paths["encrypted.key"]) and fixture.xml_value("SslCertPassword") == password)
        untouched = (fixture.home / "config.xml").read_bytes()
        wrong = "wrong-" + secrets.token_hex(16)
        rejected = save(fixture, {"sslCertPassword": wrong})
        fixture.check("wrong_pem_password_rejected_without_write_or_echo", rejected[0] == 400 and
                      (fixture.home / "config.xml").read_bytes() == untouched and wrong.encode() not in rejected[2] and password.encode() not in rejected[2])
        rejected = save(fixture, {"sslKeyPath": str(paths["root.key"]), "sslCertPassword": ""})
        fixture.check("mismatched_private_key_rejected_without_write", rejected[0] == 400 and
                      (fixture.home / "config.xml").read_bytes() == untouched and b"BEGIN PRIVATE KEY" not in rejected[2])
        stage = "pem_bundle_restart"
        saved = save(fixture, {"sslCertPath": str(paths["combined.pem"]), "sslKeyPath": ""})
        fixture.check("combined_pem_can_be_saved_with_masked_password", saved[0] in (200, 202))
        fixture.stop()
        with sqlite3.connect(fixture.home / "readarr.db") as database:
            database.execute("CREATE TABLE OwnedLifecycleProbe (Marker TEXT)")
            database.execute("INSERT INTO OwnedLifecycleProbe VALUES ('owned-restart-marker')")
        fixture.start()
        fixture.check("combined_pem_tls_sends_intermediate", tls_status(fixture, paths["root.pem"]) == 200)
        stage = "repeated_host_restart"
        for step in range(2):
            restart(fixture, paths["root.pem"])
            fixture.check(f"restart_{step + 1}_serves_http_https_same_home", fixture.xml_value("ApiKey") == fixture.api_key)
        shutdown_offset = (fixture.home / "server.log").stat().st_size
        stop_replacement(fixture)
        for database_name in ("readarr.db", "logs.db", "cache.db"):
            with sqlite3.connect(fixture.home / database_name) as database:
                fixture.check("integrity_after_repeated_restart_" + database_name.replace(".", "_"), database.execute("PRAGMA quick_check").fetchone() == ("ok",))
                if database_name == "readarr.db":
                    fixture.check("library_database_keeps_owned_marker", database.execute("SELECT Marker FROM OwnedLifecycleProbe").fetchone() == ("owned-restart-marker",))
        logs = (fixture.home / "server.log").read_bytes()[shutdown_offset:].decode(errors="replace")
        fixture.check("sigterm_uses_domain_shutdown_handlers_once", logs.count("Shutting down task execution") == 1 and logs.count("Shutting down scheduler") == 1)
        fixture.start()
        fixture.check("same_home_ports_and_lock_reusable_after_sigterm", tls_status(fixture, paths["root.pem"]) == 200)
        fixture.stop()
        stage = "invalid_startup"
        fixture.set_xml({"SslCertPassword": wrong})
        try:
            fixture.start()
            fixture.check("invalid_https_start_rejected", False)
        except ValidationFailure as error:
            fixture.check("invalid_https_start_rejected", error.code == "owned_application_exited_before_healthy" and fixture.process.poll() not in (None, 0))
        finally:
            fixture.stop()
        log_bytes = (fixture.home / "server.log").read_bytes()
        fixture.check("startup_errors_have_no_private_values", password.encode() not in log_bytes and wrong.encode() not in log_bytes and b"BEGIN PRIVATE KEY" not in log_bytes)
        fixture.check("user_owned_certificate_files_unchanged", all(hashlib.sha256(path.read_bytes()).digest() == digest for path, digest in original.items()))
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
    finally:
        try:
            fixture.cleanup()
            fixture.checks["owned_processes_cleaned_up"] = fixture.process is None and fixture.forced_stops == 0
        finally:
            fixture.checks["candidate_package_unchanged"] = snapshot_runtime(runtime) == candidate_snapshot
            if baseline_snapshot is not None:
                fixture.checks["baseline_package_unchanged"] = snapshot_runtime(baseline_runtime) == baseline_snapshot
    return result(fixture, completed, failure=failure, baselinePfxVerified=baseline_runtime is not None and fixture.checks.get("initial_password_protected_pfx_https", False),
                  notRun=["native_windows_scm_tls", "live_updater_handoff_disabled_by_fork_policy"])
