"""Verify authentication policy and local recovery using owned synthetic homes.

No existing home or PostgreSQL endpoint is accepted. Passwords are sent only in
HTTP bodies/headers or the recovery process's standard input, never arguments.
"""

from __future__ import annotations

import base64
from contextlib import closing
import hashlib
import http.client
import json
import os
from pathlib import Path
import secrets
import selectors
import signal
import shutil
import sqlite3
import subprocess
import threading
import time
import xml.etree.ElementTree as ET

from .app import Fixture, child_environment, safe_failure
from .common import (ValidationFailure, ensure_host_compatible, private_directory,
                     snapshot_runtime, validate_output)
from . import datastore, restore, upgrade


class Storage:
    def __init__(self, fixture: Fixture, postgres=None, names=None):
        self.fixture = fixture
        self.postgres = postgres
        self.names = names

    def users(self) -> dict:
        if self.postgres:
            rows = self.postgres.json(self.names["main"],
                'SELECT COALESCE(json_agg(row_to_json(t) ORDER BY "Id"),\'[]\') FROM '
                '(SELECT "Id","Identifier","Username","Password","Salt","Iterations" FROM "Users") t')
        else:
            with closing(sqlite3.connect(self.fixture.home / "readarr.db")) as database:
                database.row_factory = sqlite3.Row
                rows = [dict(row) for row in database.execute(
                    'SELECT "Id","Identifier","Username","Password","Salt","Iterations" FROM "Users" ORDER BY "Id"')]
        return {str(row["Identifier"]): row for row in rows}

    def preserved(self):
        if self.postgres:
            value = datastore.postgres_snapshot(self.postgres, self.names)
            value["mainVersions"] = self.postgres.json(self.names["main"],
                'SELECT json_agg("Version" ORDER BY "Version") FROM "VersionInfo"')
        else:
            value = restore.snapshot(self.fixture)
            with closing(sqlite3.connect(self.fixture.home / "cache.db")) as database:
                value["cache"] = database.execute('SELECT * FROM "HttpResponse" WHERE "Id"=999').fetchall()
            with closing(sqlite3.connect(self.fixture.home / "readarr.db")) as database:
                value["mainVersions"] = database.execute('SELECT "Version" FROM "VersionInfo" ORDER BY "Version"').fetchall()
        value.pop("Users")
        return value


def api(fixture: Fixture, path="config/host", method="GET", data=None, *, headers=None):
    return fixture.request(fixture.URL_BASE + "/api/v1/" + path, method, data, key=True, headers=headers)


def resource(fixture: Fixture, path="config/host", *, headers=None):
    response = api(fixture, path, headers=headers)
    if response[0] != 200:
        raise ValidationFailure("authpolicy_resource_request_failed")
    return json.loads(response[2])


def cookie(response) -> str:
    return "; ".join(value.split(";", 1)[0] for name, value in response[1] if name.lower() == "set-cookie")


def ui_status(fixture: Fixture, headers=None) -> int:
    return fixture.request(fixture.URL_BASE + "/initialize.json", headers=headers)[0]


def basic(username: str, password: str) -> dict:
    value = base64.b64encode((username + ":" + password).encode()).decode()
    return {"Authorization": "Basic " + value}


def auth(fixture: Fixture, username: str, password: str, check, name: str) -> str:
    response = upgrade.login(fixture, username, password)
    check(name, upgrade.authenticated(response))
    return cookie(response)


def secret_free(fixture: Fixture, passwords: list[str], payload=None, extra=b"") -> bool:
    paths = [fixture.home / "server.log"] + list((fixture.home / "logs").glob("*.txt"))
    content = b"\n".join(path.read_bytes() for path in paths if path.is_file()) + extra
    if payload is not None:
        content += json.dumps(payload).encode()
    return not any(password.encode() in content for password in passwords)


def recovery_process(fixture: Fixture):
    if not fixture.home or not fixture.private_tmp:
        raise ValidationFailure("authpolicy_owned_home_required")
    return subprocess.Popen([str(fixture.runtime / "Readarr"), "-passwordreset", "-exitimmediately",
                             "-data=" + str(fixture.home)], cwd=fixture.runtime,
                            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            env=child_environment(fixture.home, fixture.private_tmp), start_new_session=True)


def finish_recovery(process):
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    if process.poll() is None:
        process.communicate(timeout=5)


def recovery(fixture: Fixture, selector: str, password: str, confirmation: str, *, timeout=20):
    process = recovery_process(fixture)
    try:
        stdout, _ = process.communicate((selector + "\n" + password + "\n" + confirmation + "\n").encode(), timeout=timeout)
        return process.returncode, stdout
    finally:
        finish_recovery(process)


def wait_for_recovery_prompt(process, *, timeout=10):
    output = bytearray()
    deadline = time.monotonic() + timeout
    with selectors.DefaultSelector() as ready:
        ready.register(process.stdout, selectors.EVENT_READ)
        while time.monotonic() < deadline:
            if not ready.select(min(0.25, max(0, deadline - time.monotonic()))):
                continue
            chunk = os.read(process.stdout.fileno(), 4096)
            output.extend(chunk)
            if b"Existing account username or identifier:" in output:
                return bytes(output)
            if not chunk or len(output) > 32768:
                break
    raise ValidationFailure("authpolicy_recovery_prompt_not_reached")


def recovery_startup_handoff(fixture: Fixture, storage: Storage, check, prefix: str):
    """Keep both processes owned while exercising the same-home startup retry."""
    if fixture.process is not None:
        raise ValidationFailure("authpolicy_handoff_requires_stopped_home")
    before = storage.users()
    config = (fixture.home / "config.xml").read_bytes()
    process = recovery_process(fixture)
    thread = None
    error = []

    def startup():
        try:
            fixture.start()
        except BaseException as failure:
            error.append(failure)

    try:
        output = wait_for_recovery_prompt(process)
        thread = threading.Thread(target=startup, daemon=True)
        thread.start()
        deadline = time.monotonic() + 5
        while fixture.process is None and thread.is_alive() and time.monotonic() < deadline:
            time.sleep(0.02)
        time.sleep(0.75)
        waiting = fixture.process is not None and fixture.process.poll() is None and thread.is_alive()
        try:
            serving = api(fixture, "system/status")[0] == 200
        except (OSError, http.client.HTTPException):
            serving = False
        check(prefix + "_startup_waits_while_recovery_owns_home", waiting and not serving)
        tail, _ = process.communicate(b"missing-owned-handoff-account\n", timeout=10)
        check(prefix + "_handoff_recovery_rejects_unknown_account", process.returncode != 0
              and secret_free(fixture, [fixture.password, fixture.current_password], extra=output + tail))
        deadline = time.monotonic() + fixture.startup_timeout + 1
        while thread.is_alive() and time.monotonic() < deadline:
            thread.join(timeout=0.25)
        check(prefix + "_startup_becomes_healthy_after_recovery_releases_home",
              not thread.is_alive() and not error and api(fixture, "system/status")[0] == 200)
        check(prefix + "_handoff_preserves_accounts_config_and_api_key", storage.users() == before
              and (fixture.home / "config.xml").read_bytes() == config)
    finally:
        try:
            finish_recovery(process)
        finally:
            if thread is not None and thread.is_alive():
                fixture.stop()
                thread.join(timeout=5)
                if thread.is_alive():
                    raise ValidationFailure("authpolicy_owned_startup_thread_not_joined")



def baseline_sqlite(baseline: Path, output: Path, fixtures: list, check) -> Fixture:
    fixture = Fixture(baseline, output / "sqlite-upgrade")
    fixtures.append(fixture)
    fixture.configure_home()
    fixture.start()
    upgrade.seed_current_account(fixture)
    restore.seed_library(fixture, seed_user=False)
    upgrade.seed_extra(fixture)
    fixture.baseline_cookie = cookie(upgrade.login(fixture, fixture.current_username, fixture.current_password))
    fixture.stop()
    check("baseline_created_distinct_legacy_and_salted_accounts", len(Storage(fixture).users()) == 2)
    return fixture


def safe_accounts(value: dict, users: dict) -> bool:
    accounts = value.get("accounts")
    return (isinstance(accounts, list) and len(accounts) == len(users)
            and {str(item.get("identifier")) for item in accounts} == set(users)
            and all(set(item) <= {"identifier", "username", "hasPassword"} for item in accounts)
            and all(item.get("username") == users[str(item.get("identifier"))]["Username"] for item in accounts))


def selected_path(identifier: str) -> str:
    return "config/host?userIdentifier=" + identifier


def exercise(fixture: Fixture, storage: Storage, check, prefix: str) -> None:
    before = storage.users()
    target = next(key for key, row in before.items() if row["Username"] == fixture.username)
    other = next(key for key, row in before.items() if row["Username"] == fixture.current_username)
    original_key = fixture.xml_value("ApiKey")
    golden = storage.preserved()
    legacy_schema = not storage.postgres and {row[0] for row in golden["mainVersions"]} == datastore.LEGACY_HISTORY
    if legacy_schema:
        golden = datastore.expected_acquisition_sqlite_snapshot(golden)
        golden["mainVersions"] = [(version,) for version in sorted(datastore.HISTORY)]
    fixture.set_xml({"AuthenticationMethod": "Forms", "AuthenticationRequired": "Enabled"})
    fixture.start()
    check(prefix + "_upgrade_preserves_all_credential_encodings_before_login", storage.users() == before)
    if legacy_schema:
        check(prefix + "_upgrade_applies_only_expected_catalog_and_version_changes", storage.preserved() == golden)
    if getattr(fixture, "baseline_cookie", None):
        check(prefix + "_pre_stamp_cookie_requires_fresh_login", ui_status(fixture, {"Cookie": fixture.baseline_cookie}) in (302, 401))
    current_cookie = auth(fixture, fixture.current_username, fixture.current_password, check, prefix + "_salted_forms_login")
    legacy_cookie = auth(fixture, fixture.username, fixture.password, check, prefix + "_legacy_forms_login")
    after_login = storage.users()
    check(prefix + "_legacy_upgrade_preserves_account_identity", after_login[target]["Id"] == before[target]["Id"]
          and after_login[target]["Username"] == before[target]["Username"] and after_login[target]["Salt"]
          and after_login[other] == before[other])
    check(prefix + "_forms_cookie_grants_ui_access", ui_status(fixture, {"Cookie": current_cookie}) == 200)
    check(prefix + "_forms_cookie_does_not_replace_required_api_key", fixture.request(
          fixture.URL_BASE + "/api/v1/config/host", headers={"Cookie": current_cookie})[0] == 401)
    page = resource(fixture)
    check(prefix + "_multi_account_get_is_safe_and_requires_explicit_selection", page.get("accountSelectionRequired") is True
          and page.get("userIdentifier") is None and not page.get("username") and not page.get("password")
          and safe_accounts(page, after_login))
    secrets_to_hide = [fixture.password, fixture.current_password]
    check(prefix + "_account_resource_does_not_expose_credential_metadata", secret_free(fixture, secrets_to_hide, page)
          and not any(key in json.dumps(page).lower() for key in ('"salt"', '"iterations"'))
          and not any(row["Password"] in json.dumps(page) for row in after_login.values()))
    own = resource(fixture, headers={"Cookie": current_cookie})
    check(prefix + "_forms_actor_selects_its_own_account", str(own.get("userIdentifier")) == other
          and own.get("username") == fixture.current_username)
    check(prefix + "_forms_actor_cannot_select_another_account", api(fixture, selected_path(target),
          headers={"Cookie": current_cookie})[0] == 400)
    selected = resource(fixture, selected_path(target))
    check(prefix + "_api_key_can_explicitly_select_existing_account", str(selected.get("userIdentifier")) == target
          and selected.get("username") == fixture.username and selected.get("password") == "********")
    untouched = storage.users()
    config = (fixture.home / "config.xml").read_bytes()
    ambiguous = dict(page, username="ambiguous-owned-rename", password="********", passwordConfirmation="")
    check(prefix + "_ambiguous_credential_save_is_rejected", api(fixture, "config/host/1", "PUT", ambiguous)[0] == 400)
    check(prefix + "_ambiguous_save_changes_neither_accounts_nor_config", storage.users() == untouched
          and (fixture.home / "config.xml").read_bytes() == config)
    ordinary = dict(page, logLevel="warn")
    check(prefix + "_unrelated_settings_save_does_not_require_an_account", api(fixture, "config/host/1", "PUT", ordinary)[0] in (200, 202))
    check(prefix + "_unrelated_save_keeps_credentials_and_api_key", storage.users() == untouched and fixture.xml_value("ApiKey") == original_key)
    new_password = "Owned-P16-settings-" + secrets.token_urlsafe(24)
    secrets_to_hide.append(new_password)
    mismatch = "Owned-P16-confirmation-" + secrets.token_urlsafe(24)
    secrets_to_hide.append(mismatch)
    invalid = dict(selected, password=new_password, passwordConfirmation=mismatch)
    before_invalid = (fixture.home / "config.xml").read_bytes()
    rejected = api(fixture, "config/host/1", "PUT", invalid)
    check(prefix + "_password_confirmation_mismatch_is_rejected_before_writes", rejected[0] == 400
          and storage.users() == untouched and (fixture.home / "config.xml").read_bytes() == before_invalid)
    check(prefix + "_password_validation_response_does_not_echo_secrets", secret_free(fixture, secrets_to_hide,
          json.loads(rejected[2]) if rejected[2] else None))
    selected.update(password=new_password, passwordConfirmation=new_password)
    check(prefix + "_explicit_account_password_save_succeeds", api(fixture, "config/host/1", "PUT", selected)[0] in (200, 202))
    updated = storage.users()
    check(prefix + "_explicit_save_updates_only_selected_account", updated[other] == untouched[other]
          and updated[target]["Id"] == untouched[target]["Id"] and updated[target]["Username"] == fixture.username
          and updated[target]["Password"] != untouched[target]["Password"])
    check(prefix + "_password_change_revokes_previous_target_cookie", ui_status(fixture, {"Cookie": legacy_cookie}) in (302, 401))
    check(prefix + "_password_change_keeps_other_cookie_and_api_key", ui_status(fixture, {"Cookie": current_cookie}) == 200
          and api(fixture, "system/status")[0] == 200)
    check(prefix + "_old_target_password_no_longer_authenticates", not upgrade.authenticated(upgrade.login(fixture, fixture.username, fixture.password)))
    reset_cookie = auth(fixture, fixture.username, new_password, check, prefix + "_new_target_password_authenticates")
    fixture.stop()
    fixture.set_xml({"AuthenticationMethod": "Basic"})
    fixture.start()
    check(prefix + "_basic_target_and_other_credentials_still_work", ui_status(fixture, basic(fixture.username, new_password)) == 200
          and ui_status(fixture, basic(fixture.current_username, fixture.current_password)) == 200)
    check(prefix + "_basic_credentials_do_not_replace_required_api_key", fixture.request(
          fixture.URL_BASE + "/api/v1/config/host", headers=basic(fixture.current_username, fixture.current_password))[0] == 401)
    own = resource(fixture, headers=basic(fixture.current_username, fixture.current_password))
    check(prefix + "_basic_actor_selects_itself", str(own.get("userIdentifier")) == other)
    check(prefix + "_basic_actor_cannot_select_another_account", api(fixture, selected_path(target),
          headers=basic(fixture.current_username, fixture.current_password))[0] == 400)
    fixture.stop()
    fixture.set_xml({"AuthenticationMethod": "Forms"})
    fixture.start()
    running_before = storage.users()
    recovery_password = "Owned-P16-recovery-" + secrets.token_urlsafe(24)
    secrets_to_hide.append(recovery_password)
    code, output = recovery(fixture, target, recovery_password, recovery_password)
    check(prefix + "_recovery_refuses_a_running_home", code != 0 and storage.users() == running_before)
    check(prefix + "_running_recovery_rejection_does_not_print_secrets", secret_free(fixture, secrets_to_hide, extra=output))
    fixture.stop()
    previous = storage.users()
    saved_config = (fixture.home / "config.xml").read_bytes()
    for label, selector, confirm in (("unknown_account", "missing-owned-account", recovery_password),
                                      ("confirmation_mismatch", target, "does-not-match")):
        code, output = recovery(fixture, selector, recovery_password, confirm)
        check(prefix + "_recovery_rejects_" + label, code != 0 and storage.users() == previous)
        check(prefix + "_recovery_" + label + "_does_not_print_secrets", secret_free(fixture, secrets_to_hide, extra=output))
    code, output = recovery(fixture, target, recovery_password, recovery_password)
    check(prefix + "_stopped_local_recovery_succeeds", code == 0)
    recovered = storage.users()
    check(prefix + "_recovery_preserves_target_identity_and_other_account", recovered[other] == previous[other]
          and recovered[target]["Id"] == previous[target]["Id"] and recovered[target]["Username"] == previous[target]["Username"]
          and recovered[target]["Password"] != previous[target]["Password"])
    check(prefix + "_recovery_preserves_config_auth_mode_and_api_key", (fixture.home / "config.xml").read_bytes() == saved_config
          and fixture.xml_value("ApiKey") == original_key and fixture.xml_value("AuthenticationMethod").lower() == "forms")
    check(prefix + "_recovery_never_prints_passwords", secret_free(fixture, secrets_to_hide, extra=output))
    fixture.start()
    check(prefix + "_pre_recovery_cookie_is_revoked", ui_status(fixture, {"Cookie": reset_cookie}) in (302, 401))
    check(prefix + "_recovery_old_password_is_rejected", not upgrade.authenticated(upgrade.login(fixture, fixture.username, new_password)))
    auth(fixture, fixture.username, recovery_password, check, prefix + "_recovered_password_authenticates")
    auth(fixture, fixture.current_username, fixture.current_password, check, prefix + "_other_account_password_unchanged")
    check(prefix + "_api_key_remains_valid_after_recovery", api(fixture, "system/status")[0] == 200)
    check(prefix + "_library_history_cache_and_schema_preserved", storage.preserved() == golden)
    check(prefix + "_owned_media_preserved", restore.media_unchanged(fixture))
    fixture.stop()
    recovery_startup_handoff(fixture, storage, check, prefix)
    check(prefix + "_handoff_keeps_library_history_cache_and_schema", storage.preserved() == golden)
    check(prefix + "_application_outputs_have_no_password_canaries", secret_free(fixture, secrets_to_hide))
    fixture.stop()


def defaults(runtime: Path, output: Path, fixtures: list, check) -> None:
    fixture = Fixture(runtime, output / "default-policy")
    fixtures.append(fixture)
    fixture.configure_home()
    tree = ET.parse(fixture.home / "config.xml")
    tree.getroot().remove(tree.getroot().find("AuthenticationMethod"))
    tree.write(fixture.home / "config.xml", encoding="utf-8", xml_declaration=True)
    fixture.start()
    check("missing_auth_method_retains_none_default_without_creating_accounts", str(resource(fixture).get("authenticationMethod")).lower() == "none"
          and not Storage(fixture).users() and ui_status(fixture) == 200)
    check("none_mode_keeps_api_key_enforcement", fixture.request(fixture.URL_BASE + "/api/v1/system/status")[0] == 401)
    fixture.stop()


def run(runtime: Path, output: Path, baseline_runtime: Path | None = None) -> dict:
    if baseline_runtime is None:
        raise ValidationFailure("authpolicy_requires_pre_p16_baseline")
    runtime, output = validate_output(runtime, output)
    baseline_runtime, _ = validate_output(baseline_runtime, output)
    if runtime == baseline_runtime or snapshot_runtime(runtime) == snapshot_runtime(baseline_runtime):
        raise ValidationFailure("authpolicy_requires_distinct_pre_p16_baseline")
    ensure_host_compatible(runtime)
    ensure_host_compatible(baseline_runtime)
    private_directory(output)
    snapshots = {"candidate": snapshot_runtime(runtime), "baseline": snapshot_runtime(baseline_runtime)}
    fixtures = []
    checks = {}
    postgres = datastore.OwnedPostgres(output / "postgres")
    failure = None
    completed = False
    stage = "baseline_sqlite"

    def check(name, condition):
        checks[name] = bool(condition)
        if not condition:
            raise ValidationFailure(name)

    try:
        # The cookie data-protection discriminator uses the content root. Keep
        # that path stable across this genuine binary upgrade so a rejected old
        # cookie proves policy, rather than a changed package directory.
        owned_runtime = output / "upgrade-runtime"
        shutil.copytree(baseline_runtime, owned_runtime)
        source = baseline_sqlite(owned_runtime, output, fixtures, check)
        shutil.rmtree(owned_runtime)
        shutil.copytree(runtime, owned_runtime)
        source.runtime = owned_runtime
        stage = "sqlite_policy"
        exercise(source, Storage(source), check, "sqlite")
        stage = "owned_postgres_start"
        postgres.start()
        check("postgres_uses_owned_private_socket_without_published_ports", postgres.socket.stat().st_mode & 0o777 == 0o700)
        names = postgres.databases("p16_auth")
        target = Fixture(runtime, output / "postgres-policy")
        fixtures.append(target)
        datastore.configure_postgres(target, postgres, names)
        target.start()
        target.stop()
        datastore.transfer_seed(postgres, names, source)
        target.media_hashes = dict(source.media_hashes)
        target.username, target.password = source.username, source.password
        target.current_username, target.current_password = source.current_username, source.current_password
        # The SQLite account was changed/recovered above; seed the old legacy
        # snapshot explicitly for an independent PostgreSQL authentication proof.
        postgres.sql(names["main"], 'UPDATE "Users" SET "Password"=' + datastore.literal(hashlib.sha256(target.password.encode()).hexdigest())
                     + ',"Salt"=NULL,"Iterations"=NULL WHERE "Username"=' + datastore.literal(target.username))
        stage = "postgres_policy"
        exercise(target, Storage(target, postgres, names), check, "postgres")
        stage = "default_policy"
        defaults(runtime, output, fixtures, check)
        completed = True
    except Exception as error:
        checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
        if isinstance(error, ValidationFailure):
            failure["code"] = error.code
    finally:
        clean = True
        for fixture in reversed(fixtures):
            try:
                fixture.cleanup()
                clean = clean and fixture.process is None and fixture.forced_stops == 0
            except Exception:
                clean = False
        checks["all_owned_application_processes_stopped"] = clean
        try:
            checks["owned_postgres_container_and_socket_removed"] = postgres.close()
        except Exception:
            checks["owned_postgres_container_and_socket_removed"] = False
        for label, package in (("candidate", runtime), ("baseline", baseline_runtime)):
            try:
                checks[label + "_package_unchanged"] = snapshot_runtime(package) == snapshots[label]
            except Exception:
                checks[label + "_package_unchanged"] = False
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "failure": failure, "syntheticHomesOnly": True,
            "publishedPorts": False, "postgresNetworkMode": "none"}
