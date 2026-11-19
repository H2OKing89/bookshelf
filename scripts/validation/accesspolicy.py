"""Owned host, redirect and update-isolation checks; imports start nothing."""

from __future__ import annotations

import json
from pathlib import Path
import socket
import subprocess
import time
import urllib.parse

from .app import Fixture, child_environment, safe_failure
from .authpolicy import Storage, api, resource, secret_free
from .common import (ValidationFailure, ensure_host_compatible, private_directory,
                     snapshot_runtime, validate_output)
from . import restore, upgrade


ALLOWED = "library.example.test,*.listen.example.test;[2001:db8::1]"
UPDATE_MESSAGE = "In-app updates are unavailable for this Bookshelf build. Install a verified Bookshelf release manually."


def response_location(response) -> str | None:
    return next((value for name, value in response[1] if name.lower() == "location"), None)


def host_request(fixture, host, path="api/v1/system/status", *, method="GET", headers=None, key=True):
    headers = {**(headers or {}), "Host": host}
    return fixture.request(fixture.URL_BASE + "/" + path, method, key=key, headers=headers)


def login(fixture, return_url):
    query = "" if return_url is None else "?" + urllib.parse.urlencode({"returnUrl": return_url})
    body = urllib.parse.urlencode({"username": fixture.current_username,
                                  "password": fixture.current_password, "rememberMe": "on"}).encode()
    return fixture.request(fixture.URL_BASE + "/login" + query, "POST", body,
                           headers={"Content-Type": "application/x-www-form-urlencoded",
                                    "Host": "library.example.test"})


def wait_command(fixture, identifier, *, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        command = fixture.api("command/" + str(identifier))
        if str(command.get("status", "")).lower() in ("completed", "failed", "cancelled", "aborted"):
            return command
        time.sleep(0.1)
    raise ValidationFailure("accesspolicy_owned_command_timed_out")


def rejected_start(fixture) -> bool:
    try:
        fixture.start()
        return False
    except ValidationFailure:
        # A health timeout alone is not proof that invalid configuration was rejected.
        return fixture.process is not None and fixture.process.poll() is not None and fixture.process.returncode != 0
    finally:
        fixture.stop()


def redirects(fixture, check):
    base = fixture.URL_BASE
    cases = (
        ("absent", None, base + "/"),
        ("empty", "", base + "/"),
        ("root", "/", base + "/"),
        ("exact", base, base),
        ("case", base.upper() + "/book/13", base.upper() + "/book/13"),
        ("exact_query_fragment", base + "?name=a+b%20c#files", base + "?name=a+b%20c#files"),
        ("child", base + "/book/13?edition=us#files", base + "/book/13?edition=us#files"),
        ("sibling", base + "-sibling/book/13", base + base + "-sibling/book/13"),
        ("encoded_slash", base + "%2Fbook/13", base + base + "%2Fbook/13"),
        ("encoded_bytes", "/book/A%252FB?name=a%2Bb#files", base + "/book/A%252FB?name=a%2Bb#files"),
        ("application_relative", "~/calendar?date=2026-11#recordings", base + "/calendar?date=2026-11#recordings"),
        ("external", "https://other.invalid/book/13", base + "/"),
        ("protocol_relative", "//other.invalid/book/13", base + "/"),
        ("malformed_relative", "book/13", base + "/"),
    )
    for label, target, expected in cases:
        response = login(fixture, target)
        check("login_redirect_" + label, upgrade.authenticated(response) and response_location(response) == expected)
    response = fixture.request(base + "/logout", headers={"Host": "library.example.test"})
    check("logout_preserves_base_path", response[0] in (302, 303) and response_location(response) == base + "/")


def host_filter(fixture, check):
    for label, host in (
            ("exact", "library.example.test"), ("case_and_port", "LIBRARY.EXAMPLE.TEST:8443"),
            ("wildcard", "reader.listen.example.test"), ("deep_wildcard", "deep.reader.listen.example.test"),
            ("ipv6_and_port", "[2001:db8::1]:443"), ("localhost", "localhost:8787"),
            ("loopback_ipv4", "127.0.0.1:8787"), ("loopback_ipv6", "[::1]:8787"),
            ("server_hostname", socket.gethostname())):
        check("host_allow_" + label, host_request(fixture, host)[0] == 200)
    for label, host in (("unknown", "unexpected.example.test"), ("wildcard_apex", "listen.example.test"),
                        ("unlisted_ipv6", "[2001:db8::2]:443")):
        check("host_reject_" + label, host_request(fixture, host)[0] == 400)
    for label, path, method in (("login", "login", "GET"), ("initialize", "initialize.json", "GET"),
                                ("api", "api/v1/author", "GET"),
                                ("signalr", "signalr/messages/negotiate?negotiateVersion=1", "POST")):
        check("bad_host_rejected_before_" + label,
              host_request(fixture, "unexpected.example.test", path, method=method, key=False)[0] == 400)
    check("allowed_host_keeps_api_key_requirement",
          host_request(fixture, "library.example.test", key=False)[0] == 401)
    forwarded = {"X-Forwarded-For": "198.51.100.24", "X-Forwarded-Proto": "https",
                 "X-Forwarded-Host": "library.example.test"}
    check("trusted_loopback_forwarded_host_is_filtered_after_forwarding",
          host_request(fixture, "unlisted-proxy.example.test", headers=forwarded)[0] == 200)
    check("trusted_loopback_cannot_forward_a_disallowed_host",
          host_request(fixture, "library.example.test", headers={**forwarded,
                       "X-Forwarded-Host": "unlisted-forwarded.example.test"})[0] == 400)


def saved_host_settings(fixture, check):
    configured = fixture.xml_value("AllowedHosts")
    before = Storage(fixture).users()
    key = fixture.xml_value("ApiKey")
    value = resource(fixture)
    check("host_api_exposes_allowed_hosts", value.get("allowedHosts") == configured)
    untouched = (fixture.home / "config.xml").read_bytes()
    invalid = {**value, "allowedHosts": "https://not-a-host.example.test"}
    response = api(fixture, "config/host/1", "PUT", invalid)
    check("invalid_host_save_is_rejected_before_config_or_account_writes", response[0] == 400
          and (fixture.home / "config.xml").read_bytes() == untouched and Storage(fixture).users() == before)
    for label in ("omitted", "null"):
        updated = resource(fixture)
        if label == "omitted":
            updated.pop("allowedHosts", None)
        else:
            updated["allowedHosts"] = None
        response = api(fixture, "config/host/1", "PUT", updated)
        check("host_api_" + label + "_preserves_saved_list", response[0] in (200, 202)
              and fixture.xml_value("AllowedHosts") == configured and Storage(fixture).users() == before)
    updated = resource(fixture)
    updated["allowedHosts"] = "LIBRARY.EXAMPLE.TEST ; *.listen.example.test, [2001:db8::1]"
    response = api(fixture, "config/host/1", "PUT", updated)
    normalized = "library.example.test, *.listen.example.test, [2001:db8::1]"
    check("host_api_normalizes_valid_list", response[0] in (200, 202)
          and fixture.xml_value("AllowedHosts") == normalized)
    updated = resource(fixture)
    updated["allowedHosts"] = "  "
    response = api(fixture, "config/host/1", "PUT", updated)
    check("host_api_explicit_empty_clears_saved_list", response[0] in (200, 202)
          and fixture.xml_value("AllowedHosts") == "")
    check("host_filter_change_requires_restart", host_request(fixture, "unexpected.example.test")[0] == 400)
    fixture.stop()
    fixture.start()
    check("cleared_host_filter_is_permissive_after_restart", host_request(fixture, "unexpected.example.test")[0] == 200)
    check("host_settings_preserve_credentials_api_key_and_auth_mode", Storage(fixture).users() == before
          and fixture.xml_value("ApiKey") == key and fixture.xml_value("AuthenticationMethod").lower() == "forms")


def environment_precedence(fixture, check):
    fixture.stop()
    fixture.set_xml({"AllowedHosts": "xml.example.test"})
    fixture.start(allowed_hosts="env.example.test")
    check("host_environment_overrides_xml", host_request(fixture, "env.example.test")[0] == 200
          and host_request(fixture, "xml.example.test")[0] == 400)
    check("host_api_reports_effective_environment_value", resource(fixture).get("allowedHosts") == "env.example.test")
    for label in ("omitted", "null"):
        updated = resource(fixture)
        if label == "omitted":
            updated.pop("allowedHosts", None)
        else:
            updated["allowedHosts"] = None
        response = api(fixture, "config/host/1", "PUT", updated)
        check("host_environment_" + label + "_save_preserves_xml", response[0] in (200, 202)
              and fixture.xml_value("AllowedHosts") == "xml.example.test")
    fixture.stop()
    fixture.set_xml({"AllowedHosts": "https://invalid-xml.example.test"})
    fixture.start(allowed_hosts="")
    check("host_explicit_empty_environment_overrides_even_invalid_xml",
          host_request(fixture, "unexpected.example.test")[0] == 200
          and fixture.xml_value("AllowedHosts") == "https://invalid-xml.example.test")
    fixture.stop()
    before = Storage(fixture).preserved()
    users = Storage(fixture).users()
    config = (fixture.home / "config.xml").read_bytes()
    check("invalid_saved_host_config_rejects_startup", rejected_start(fixture))
    check("invalid_host_startup_keeps_schema_library_and_credentials", Storage(fixture).preserved() == before
          and Storage(fixture).users() == users and restore.media_unchanged(fixture)
          and (fixture.home / "config.xml").read_bytes() == config)
    fixture.set_xml({"AllowedHosts": ""})
    fixture.start()


def update_isolation(fixture, check):
    fixture.stop()
    fixture.set_xml({"UpdateMechanism": "BuiltIn", "UpdateAutomatically": "True", "Branch": "synthetic-legacy-channel"})
    marker = fixture.home / "update_required"
    marker.write_bytes(b"owned synthetic update-required marker\n")
    update_settings = {name: fixture.xml_value(name) for name in ("UpdateMechanism", "UpdateAutomatically", "Branch")}
    fixture.start()
    status = fixture.api("system/status")
    check("builtin_updates_are_explicitly_unavailable", status.get("builtInUpdatesAvailable") is False
          and status.get("builtInUpdateMessage") == UPDATE_MESSAGE)
    check("update_history_endpoint_returns_no_feed_packages", fixture.api("update") == [])
    command = fixture.api("command", "POST", {"name": "ApplicationUpdateCheck"})
    checked = wait_command(fixture, command["id"])
    check("update_check_finishes_without_installation", str(checked.get("status", "")).lower() == "completed")
    command = fixture.api("command", "POST", {"name": "ApplicationUpdate"})
    refused = wait_command(fixture, command["id"])
    check("manual_builtin_update_is_refused", str(refused.get("status", "")).lower() == "failed"
          and UPDATE_MESSAGE in json.dumps(refused))
    check("disabled_updates_preserve_saved_update_settings_and_marker",
          {name: fixture.xml_value(name) for name in update_settings} == update_settings
          and marker.read_bytes() == b"owned synthetic update-required marker\n")
    working = private_directory(fixture.output / "updater-invocation")
    updater = fixture.runtime / "Readarr.Update" / "Readarr.Update"
    process = subprocess.run([str(updater)], cwd=working, env=child_environment(fixture.home, fixture.private_tmp),
                             stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)
    check("standalone_updater_is_refused_without_creating_files", process.returncode != 0
          and b"unavailable" in process.stdout.lower() and not list(working.iterdir()))


def exercise(runtime, output, fixtures, check):
    fixture = Fixture(runtime, output / "owned-application")
    fixtures.append(fixture)
    fixture.configure_home()
    fixture.start()
    check("empty_host_default_preserves_existing_access", host_request(fixture, "unexpected.example.test")[0] == 200)
    upgrade.seed_current_account(fixture)
    restore.seed_library(fixture, seed_user=False)
    fixture.stop()
    fixture.set_xml({"AllowedHosts": ALLOWED})
    golden = Storage(fixture).preserved()
    users = Storage(fixture).users()
    key = fixture.xml_value("ApiKey")
    fixture.start()
    host_filter(fixture, check)
    redirects(fixture, check)
    saved_host_settings(fixture, check)
    environment_precedence(fixture, check)
    update_isolation(fixture, check)
    check("access_settings_preserve_schema_library_cache_media_and_accounts", Storage(fixture).preserved() == golden
          and Storage(fixture).users() == users and restore.media_unchanged(fixture)
          and fixture.xml_value("ApiKey") == key and fixture.xml_value("AuthenticationMethod").lower() == "forms")
    check("access_errors_do_not_expose_password_canaries", secret_free(fixture, [fixture.current_password]))


def run(runtime: Path, output: Path) -> dict:
    runtime, output = validate_output(runtime, output)
    ensure_host_compatible(runtime)
    private_directory(output)
    before = snapshot_runtime(runtime)
    fixtures = []
    checks = {}
    completed = False
    failure = None

    def check(name, condition):
        checks[name] = bool(condition)
        if not condition:
            raise ValidationFailure(name)

    try:
        exercise(runtime, output, fixtures, check)
        completed = True
    except Exception as error:
        failure = safe_failure("access_policy", error)
        if isinstance(error, ValidationFailure):
            failure["code"] = error.code
        checks["access_policy_completed"] = False
    finally:
        cleaned = True
        for fixture in reversed(fixtures):
            try:
                fixture.cleanup()
                cleaned = cleaned and fixture.process is None and fixture.forced_stops == 0
            except Exception:
                cleaned = False
        checks["all_owned_application_processes_stopped"] = cleaned
        try:
            checks["candidate_package_unchanged"] = snapshot_runtime(runtime) == before
        except Exception:
            checks["candidate_package_unchanged"] = False
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "failure": failure, "syntheticHomesOnly": True,
            "externalListeners": False, "updateNetworkVerification": "source_unit_http_mock_boundaries"}
