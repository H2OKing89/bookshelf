"""Upgrade an application-generated synthetic baseline home and its original backup."""

from __future__ import annotations

import base64
import hashlib
import json
from pathlib import Path
import sqlite3
import urllib.parse

from .app import Fixture, result, runtime_matches, safe_failure
from .common import ValidationFailure, private_directory, snapshot_runtime, validate_output
from . import restore


def login(fixture: Fixture, username: str, password: str):
    body = urllib.parse.urlencode({"username": username, "password": password, "rememberMe": "on"}).encode()
    return fixture.request(fixture.URL_BASE + "/login?returnUrl=" + urllib.parse.quote(fixture.URL_BASE + "/books"),
                           "POST", body, headers={"Content-Type": "application/x-www-form-urlencoded"})


def authenticated(response) -> bool:
    return response[0] in (302, 303) and any(name.lower() == "set-cookie" for name, _ in response[1])


def credential_rows(fixture: Fixture) -> dict[str, tuple]:
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        rows = database.execute('SELECT "Id","Identifier","Username","Password","Salt","Iterations" FROM "Users" ORDER BY "Id"').fetchall()
    return {row[2]: row for row in rows}


def password_matches(row: tuple, password: str) -> bool:
    """Check the existing PBKDF2/SHA512 format without pinning the work factor."""
    if not isinstance(row[5], int) or not 0 < row[5] <= 10_000_000:
        return False
    try:
        salt = base64.b64decode(row[4], validate=True)
        expected = base64.b64decode(row[3], validate=True)
    except (TypeError, ValueError):
        return False
    if not salt or not expected:
        return False
    actual = hashlib.pbkdf2_hmac("sha512", password.encode(), salt, row[5], dklen=len(expected))
    return actual == expected


def seed_current_account(fixture: Fixture) -> None:
    # The actual baseline UserService generates its current salt/hash/work factor.
    fixture.current_username = "synthetic-current-reader"
    fixture.current_password = fixture.password + "-current"
    resource = fixture.api()
    resource.update(username=fixture.current_username, password=fixture.current_password,
                    passwordConfirmation=fixture.current_password,
                    authenticationMethod="forms", authenticationRequired="enabled")
    fixture.api("config/host/1", "PUT", resource)
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        fixture.check("baseline_supports_salted_credential_schema", {"Salt", "Iterations"} <= set(restore.columns(database, "Users")))
    row = credential_rows(fixture)[fixture.current_username]
    fixture.check("baseline_service_generated_working_salted_password", password_matches(row, fixture.current_password))
    fixture.start()


def seed_extra(fixture: Fixture) -> None:
    fixture.stop()
    part = fixture.home / "owned-media" / "Synthetic Author" / "Synthetic Recording Part 2.m4b"
    payload = b"Synthetic owned second audiobook part\0"
    part.write_bytes(payload)
    fixture.media_hashes[part] = hashlib.sha256(payload).hexdigest()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        copied = dict(zip(restore.columns(database, "BookFiles"), database.execute('SELECT * FROM "BookFiles" WHERE "Id"=19').fetchone()))
        copied.update(Id=20, Path=str(part), Part=2, Size=len(payload))
        restore.insert(database, "BookFiles", copied)
        restore.insert(database, "Users", {"Id": 17, "Identifier": fixture.identifier, "Username": fixture.username,
                       "Password": hashlib.sha256(fixture.password.encode()).hexdigest(), "Salt": None, "Iterations": None})
        fixture.check("baseline_contains_distinct_recording_parts", database.execute('SELECT GROUP_CONCAT("Part") FROM (SELECT "Part" FROM "BookFiles" ORDER BY "Part")').fetchone()[0] == "1,2")
    fixture.set_xml({"AuthenticationMethod": "Forms"})
    fixture.start()
    fixture.check("baseline_current_password_authenticates", authenticated(login(fixture, fixture.current_username, fixture.current_password)))


def run(runtime: Path, output: Path, baseline_runtime: Path | None = None) -> dict:
    if baseline_runtime is None:
        return {"checks": {}, "passed": 0, "failed": 0, "completed": False,
                "skipped": ["upgrade_requires_explicit_baseline_runtime"]}
    runtime, output = validate_output(runtime, output)
    baseline_runtime, _ = validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise ValidationFailure("baseline_runtime_must_differ_from_candidate")
    if snapshot_runtime(baseline_runtime) == snapshot_runtime(runtime):
        raise ValidationFailure("baseline_payload_must_differ_from_candidate")
    private_directory(output)
    fixture = Fixture(baseline_runtime, output / "application")
    completed = False
    failure = None
    stage = "create_baseline"
    versions = {}
    try:
        fixture.configure_home()
        fixture.start()
        versions["baselineRuntimeVersion"] = fixture.api("system/status").get("runtimeVersion", "")
        fixture.check("baseline_reports_its_published_runtime", runtime_matches(baseline_runtime, versions["baselineRuntimeVersion"]))
        seed_current_account(fixture)
        restore.seed_library(fixture, seed_user=False)
        seed_extra(fixture)
        stage = "baseline_backup"
        _, backup = restore.create_original_backup(fixture)
        fixture.stop()
        golden = restore.snapshot(fixture)
        before = credential_rows(fixture)
        config = (fixture.home / "config.xml").read_bytes()
        key = fixture.api_key
        stage = "in_place_upgrade"
        fixture.runtime = runtime
        fixture.start()
        versions["candidateRuntimeVersion"] = fixture.api("system/status").get("runtimeVersion", "")
        fixture.check("candidate_reports_its_published_runtime", runtime_matches(runtime, versions["candidateRuntimeVersion"]))
        fixture.check("all_library_roots_parts_narrators_and_history_preserved_before_login", restore.snapshot(fixture, golden) == golden)
        fixture.check("all_credential_encodings_preserved_before_login", credential_rows(fixture) == before)
        fixture.check("config_api_key_and_media_preserved_in_place", (fixture.home / "config.xml").read_bytes() == config and fixture.xml_value("ApiKey") == key and restore.media_unchanged(fixture))
        stage = "authenticate_preserved_accounts"
        fixture.check("candidate_accepts_existing_salted_password", authenticated(login(fixture, fixture.current_username, fixture.current_password)))
        current = credential_rows(fixture)
        old_current = before[fixture.current_username]
        new_current = current[fixture.current_username]
        fixture.check("salted_account_identity_and_password_preserved", new_current[:3] == old_current[:3] and new_current[5] >= old_current[5] and password_matches(new_current, fixture.current_password))
        fixture.check("unchanged_work_factor_keeps_existing_hash", new_current[5] != old_current[5] or new_current == old_current)
        fixture.check("unrelated_legacy_account_unchanged_by_current_login", current[fixture.username] == before[fixture.username])
        fixture.check("candidate_accepts_existing_legacy_password", authenticated(login(fixture, fixture.username, fixture.password)))
        after = credential_rows(fixture)
        legacy = after[fixture.username]
        fixture.check("legacy_password_upgrades_without_identity_reset", legacy[:3] == before[fixture.username][:3] and legacy[3] != before[fixture.username][3] and password_matches(legacy, fixture.password))
        fixture.check("legacy_login_keeps_other_account_unchanged", after[fixture.current_username] == new_current)
        stage = "restore_original_baseline_backup"
        response = restore.upload(fixture, "original-baseline.zip", backup.read_bytes())
        fixture.check("candidate_accepts_application_generated_baseline_backup", response[0] == 200 and json.loads(response[2]).get("restartRequired") is True)
        fixture.stop()
        fixture.start()
        fixture.check("baseline_backup_restores_all_library_rows", restore.snapshot(fixture, golden) == golden)
        fixture.check("baseline_backup_restores_exact_credential_encodings", credential_rows(fixture) == before)
        fixture.check("backup_restore_preserves_config_key_and_media", (fixture.home / "config.xml").read_bytes() == config and fixture.api_key == key and restore.media_unchanged(fixture))
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
    finally:
        try:
            fixture.cleanup()
            fixture.checks["owned_upgrade_processes_cleaned_up"] = fixture.process is None and fixture.forced_stops == 0
        except Exception:
            fixture.checks["owned_upgrade_processes_cleaned_up"] = False
    return result(fixture, completed, failure=failure, skipped=[],
                  originalApplicationGeneratedBackupUsed=completed,
                  inPlaceRuntimeSwapVerified=completed, **versions)
