"""Owned process and loopback HTTP fixtures for local package validation.

Importing this module starts nothing. Every application invocation receives a
new private home explicitly; callers cannot supply an existing application home.
"""

from __future__ import annotations

import http.client
import json
import os
from pathlib import Path
import secrets
import signal
import socket
import sqlite3
import subprocess
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET

from .common import ValidationFailure, ensure_host_compatible, private_directory, validate_output


def free_loopback_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def child_environment(home: Path, temporary: Path) -> dict[str, str]:
    """Keep app overrides, native injection and test-only JIT flags out of runs."""
    allowed = ("PATH", "LANG", "LC_ALL", "LC_CTYPE", "TZ", "SSL_CERT_FILE", "SSL_CERT_DIR")
    environment = {name: os.environ[name] for name in allowed if name in os.environ}
    environment.update({
        "HOME": str(home), "XDG_CONFIG_HOME": str(home / ".config"),
        "XDG_CACHE_HOME": str(home / ".cache"), "TMPDIR": str(temporary),
        "TMP": str(temporary), "TEMP": str(temporary), "DOTNET_EnableDiagnostics": "0",
        "DOTNET_DbgEnableMiniDump": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1",
    })
    return environment


def runtime_expectation(runtime: Path) -> tuple[str, bool]:
    """Return the configured runtime and whether publication was self-contained."""
    options = json.loads((runtime / "Readarr.runtimeconfig.json").read_text())["runtimeOptions"]
    included = options.get("includedFrameworks", [])
    frameworks = included or options.get("frameworks", []) or [options.get("framework", {})]
    version = next(item["version"] for item in frameworks if item.get("name") == "Microsoft.NETCore.App")
    return version, bool(included)


def runtime_matches(runtime: Path, actual: str) -> bool:
    expected, exact = runtime_expectation(runtime)
    return actual == expected if exact else actual.split(".", 1)[0] == expected.split(".", 1)[0]


def safe_failure(stage: str, error: BaseException) -> dict[str, str]:
    # Exception messages, tracebacks, request bodies and locals can contain credentials.
    return {"stage": stage, "exceptionType": type(error).__name__}


def result(fixture: Fixture, completed: bool, **metadata) -> dict:
    checks = dict(fixture.checks)
    return {"checks": checks, "passed": sum(checks.values()),
            "failed": sum(not value for value in checks.values()), "completed": completed,
            "syntheticHomesOnly": True, **metadata}


class Fixture:
    URL_BASE = "/bookshelf-validation"

    def __init__(self, runtime: Path, output: Path, *, startup_timeout: float = 90):
        self.runtime, self.output = validate_output(runtime, output)
        private_directory(self.output)
        self.home: Path | None = None
        self.private_tmp: Path | None = None
        self.backend_port: int | None = None
        self.backend_host: str | None = None
        self.api_key = secrets.token_hex(16)
        self.username = "synthetic-legacy-reader"
        self.password = secrets.token_urlsafe(30)
        self.identifier = str(uuid.uuid4())
        self.process: subprocess.Popen | None = None
        self.process_log = None
        self.startup_timeout = startup_timeout
        self.checks: dict[str, bool] = {}
        self.media_hashes: dict[Path, str] = {}
        self.exit_codes: list[int] = []
        self.forced_stops = 0

    def check(self, name: str, condition) -> None:
        self.checks[name] = bool(condition)
        if not condition:
            raise ValidationFailure(name)

    def configure_home(self) -> None:
        if self.home is not None:
            raise ValidationFailure("fixture_home_already_created")
        self.home = Path(tempfile.mkdtemp(prefix="home-", dir=self.output))
        self.home.chmod(0o700)
        self.private_tmp = private_directory(self.home / "tmp")
        self.backend_port = free_loopback_port()
        ssl_port = free_loopback_port()
        while ssl_port == self.backend_port:
            ssl_port = free_loopback_port()
        self.backend_host = "127.0.0.1:" + str(self.backend_port)
        config = ET.Element("Config")
        values = {
            "BindAddress": "127.0.0.1", "Port": str(self.backend_port), "SslPort": str(ssl_port),
            "EnableSsl": "False", "UrlBase": self.URL_BASE, "LaunchBrowser": "False",
            "AuthenticationMethod": "None", "AuthenticationRequired": "Enabled", "ApiKey": self.api_key,
            "InstanceName": "Bookshelf Synthetic Validation", "LogLevel": "Warn", "ConsoleLogLevel": "Warn",
            "LogSql": "False", "UpdateAutomatically": "False", "UpdateMechanism": "External",
            "Branch": "develop", "AnalyticsEnabled": "False", "TrustCgnatIpAddresses": "False",
            "TrustedNetworks": "",
        }
        for name, value in values.items():
            ET.SubElement(config, name).text = value
        ET.ElementTree(config).write(self.home / "config.xml", encoding="utf-8", xml_declaration=True)
        (self.home / "config.xml").chmod(0o600)

    def set_xml(self, values: dict[str, str]) -> None:
        path = self.home / "config.xml"
        tree = ET.parse(path)
        for name, value in values.items():
            element = tree.getroot().find(name)
            if element is None:
                element = ET.SubElement(tree.getroot(), name)
            element.text = value
        tree.write(path, encoding="utf-8", xml_declaration=True)
        path.chmod(0o600)

    def xml_value(self, name: str) -> str:
        return ET.parse(self.home / "config.xml").getroot().findtext(name) or ""

    def request(self, path: str, method="GET", data=None, *, key=False, headers=None, timeout=15):
        request_headers = dict(headers or {})
        request_headers.setdefault("Host", self.backend_host)
        request_headers.setdefault("X-Requested-With", "XMLHttpRequest")
        if key:
            request_headers["X-Api-Key"] = self.api_key
        if data is None:
            body = None
        elif isinstance(data, bytes):
            body = data
        else:
            body = json.dumps(data).encode("utf-8")
            request_headers.setdefault("Content-Type", "application/json")
        connection = http.client.HTTPConnection("127.0.0.1", self.backend_port, timeout=timeout)
        try:
            connection.request(method, path, body=body, headers=request_headers)
            response = connection.getresponse()
            return response.status, response.getheaders(), response.read()
        finally:
            connection.close()

    def api(self, path="config/host", method="GET", data=None):
        status, _, body = self.request(self.URL_BASE + "/api/v1/" + path, method, data, key=True)
        self.check("authenticated_api_request_succeeded", status in (200, 201, 202))
        return json.loads(body) if body else None

    def start(self, *, log_db_enabled: bool | None = None,
              postgres_connection_strings: dict[str, str] | None = None,
              allowed_hosts: str | None = None) -> None:
        if log_db_enabled is not None and type(log_db_enabled) is not bool:
            raise ValidationFailure("invalid_owned_log_db_override")
        if postgres_connection_strings is not None and (
                type(postgres_connection_strings) is not dict or
                any(key not in {"MainDbConnectionString", "LogDbConnectionString", "CacheDbConnectionString"}
                    or type(value) is not str or "\0" in value
                    for key, value in postgres_connection_strings.items())):
            raise ValidationFailure("invalid_owned_postgres_connection_override")
        if allowed_hosts is not None and (type(allowed_hosts) is not str or "\0" in allowed_hosts):
            raise ValidationFailure("invalid_owned_allowed_hosts_override")
        if self.process is not None or self.home is None:
            raise ValidationFailure("owned_process_lifecycle")
        ensure_host_compatible(self.runtime)
        self.process_log = (self.home / "server.log").open("ab")
        (self.home / "server.log").chmod(0o600)
        # Fixed shell program; all paths are passed as positional arguments, never interpolated.
        # Limit native crash dumps without changing the caller's limits or using preexec_fn.
        # Startup failures must exit instead of waiting for interactive intervention.
        command = ["/bin/sh", "-c", 'ulimit -c 0; exec "$@"', "bookshelf-validation",
                   str(self.runtime / "Readarr"), "-nobrowser", "-nosingleinstancecheck", "-exitimmediately", "-data=" + str(self.home)]
        environment = child_environment(self.home, self.private_tmp)
        if log_db_enabled is not None:
            environment["Readarr__Log__DbEnabled"] = str(log_db_enabled).lower()
        if allowed_hosts is not None:
            environment["Readarr__Server__AllowedHosts"] = allowed_hosts
        for key, value in (postgres_connection_strings or {}).items():
            environment["Readarr__Postgres__" + key] = value
        self.process = subprocess.Popen(command, cwd=self.runtime,
                                        env=environment,
                                        stdin=subprocess.DEVNULL, stdout=self.process_log,
                                        stderr=subprocess.STDOUT, start_new_session=True)
        deadline = time.monotonic() + self.startup_timeout
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise ValidationFailure("owned_application_exited_before_healthy")
            try:
                if self.request(self.URL_BASE + "/api/v1/system/status", key=True, timeout=2)[0] == 200:
                    return
            except (OSError, http.client.HTTPException):
                pass
            time.sleep(0.2)
        raise ValidationFailure("owned_application_startup_timed_out")

    def stop(self) -> None:
        process = self.process
        try:
            if process is not None:
                # A descendant may outlive an already-exited leader. The group
                # was created by this Popen, and no process-name discovery is used.
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                if process.poll() is None:
                    try:
                        process.wait(timeout=20)
                    except subprocess.TimeoutExpired:
                        self.forced_stops += 1
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=5)
                try:
                    os.killpg(process.pid, 0)
                except ProcessLookupError:
                    pass
                else:
                    os.killpg(process.pid, signal.SIGKILL)
                self.exit_codes.append(process.returncode)
                self.process = None
        except BaseException:
            if process is not None:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait(timeout=5)
                self.process = None
            raise
        finally:
            if self.process_log is not None:
                self.process_log.close()
                self.process_log = None

    def cleanup(self) -> None:
        self.stop()


def run(runtime: Path, output: Path) -> dict:
    fixture = Fixture(runtime, output)
    completed = False
    failure = None
    stage = "configure"
    try:
        fixture.configure_home()
        stage = "startup"
        fixture.start()
        status = fixture.api("system/status")
        fixture.check("reported_runtime_matches_published_runtime", runtime_matches(fixture.runtime, status.get("runtimeVersion", "")))
        for route in ("author", "book", "rootfolder"):
            fixture.check("synthetic_" + route + "_list_starts_empty", fixture.api(route) == [])
        fixture.check("missing_api_key_is_rejected", fixture.request(fixture.URL_BASE + "/api/v1/author")[0] == 401)
        fixture.check("incorrect_api_key_is_rejected", fixture.request(
            fixture.URL_BASE + "/api/v1/author", headers={"X-Api-Key": secrets.token_hex(16)})[0] == 401)
        fixture.check("main_cache_and_log_databases_created", all((fixture.home / name).is_file() for name in ("readarr.db", "cache.db", "logs.db")))
        fixture.check("generated_api_key_preserved", fixture.xml_value("ApiKey") == fixture.api_key)
        fixture.check("private_home_and_temp_permissions", fixture.home.stat().st_mode & 0o777 == 0o700 and fixture.private_tmp.stat().st_mode & 0o777 == 0o700)
        stage = "shutdown"
        fixture.stop()
        fixture.check("application_stops_gracefully", fixture.exit_codes[-1] == 0 and fixture.forced_stops == 0)
        for name in ("readarr.db", "cache.db", "logs.db"):
            with sqlite3.connect(fixture.home / name) as database:
                fixture.check(name.replace(".", "_") + "_integrity_is_ok", database.execute("PRAGMA integrity_check").fetchall() == [("ok",)])
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
    finally:
        try:
            fixture.cleanup()
            fixture.checks["owned_process_cleaned_up"] = fixture.process is None
        except Exception:
            fixture.checks["owned_process_cleaned_up"] = False
    return result(fixture, completed, failure=failure)
