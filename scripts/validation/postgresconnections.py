"""Verify PostgreSQL connection-string migration with owned synthetic databases.

The server runs without a network or published ports. A private Unix socket and
SCRAM-authenticated owned role exercise real quoted-password authentication;
TLS parameter retention is a source-test concern, not a transport claim here.
"""

from __future__ import annotations

import json
from pathlib import Path
import secrets
import xml.etree.ElementTree as ET

from .app import Fixture, safe_failure
from .common import ValidationFailure, ensure_host_compatible, private_directory, snapshot_runtime, validate_output
from . import datastore, logstorage, restore, upgrade


COMPONENTS = ("Host", "Port", "User", "Password", "MainDb", "LogDb", "CacheDb")
STRINGS = {"main": "MainDbConnectionString", "log": "LogDbConnectionString", "cache": "CacheDbConnectionString"}
ROLE = "p14_app"


def quoted(value: str) -> str:
    """ADO.NET double-quoted values, including literal quotes and semicolons."""
    return '"' + value.replace('"', '""') + '"'


def dsn(postgres: datastore.OwnedPostgres, database: str, password: str, *,
        username=ROLE, application="Bookshelf P14 owned proof") -> str:
    datastore.identifier(database)
    return ";".join(key + "=" + quoted(value) for key, value in (
        ("Host", str(postgres.socket)), ("Port", "5432"), ("Database", database),
        ("Username", username), ("Password", password), ("SSL Mode", "Disable"),
        ("Timeout", "3"), ("Command Timeout", "5"), ("Application Name", application),
        ("Enlist", "true")))


def replace_postgres(fixture: Fixture, values: dict[str, str]) -> None:
    """Remove actual component entries instead of replacing them with empty values."""
    path = fixture.home / "config.xml"
    tree = ET.parse(path)
    for element in list(tree.getroot()):
        if element.tag.startswith("Postgres"):
            tree.getroot().remove(element)
    tree.write(path, encoding="utf-8", xml_declaration=True)
    path.chmod(0o600)
    fixture.set_xml(values)


def configure_component(fixture: Fixture, postgres: datastore.OwnedPostgres,
                        names: dict, password: str) -> None:
    fixture.configure_home()
    fixture.set_xml({"PostgresHost": str(postgres.socket), "PostgresPort": "5432", "PostgresUser": ROLE,
                     "PostgresPassword": password, "PostgresMainDb": names["main"],
                     "PostgresLogDb": names["log"], "PostgresCacheDb": names["cache"]})


def strings(postgres: datastore.OwnedPostgres, names: dict, password: str, *, application="Bookshelf P14 owned proof") -> dict:
    return {"Postgres" + STRINGS[kind]: dsn(postgres, name, password, application=application)
            for kind, name in names.items()}


def owned_databases(postgres: datastore.OwnedPostgres, prefix: str, *, include_log=True) -> dict:
    names = {kind: prefix + "_" + kind for kind in ("main", "cache", "log") if include_log or kind != "log"}
    for name in names.values():
        postgres.sql("postgres", "CREATE DATABASE " + datastore.identifier(name) + " OWNER " + datastore.identifier(ROLE))
    return names


def scram_role(postgres: datastore.OwnedPostgres, password: str) -> None:
    postgres.sql("postgres", "CREATE ROLE " + datastore.identifier(ROLE) + " LOGIN PASSWORD " + datastore.literal(password))
    path = postgres.output / "data" / "pg_hba.conf"
    original = path.read_text()
    path.write_text("local all " + ROLE + " scram-sha-256\n" + original)
    path.chmod(0o600)
    if postgres.json("postgres", "SELECT to_json(pg_reload_conf())") is not True:
        raise ValidationFailure("postgresconnections_owned_auth_reload_failed")


def no_sqlite(fixture: Fixture) -> bool:
    return not any((fixture.home / name).exists() for name in ("readarr.db", "logs.db", "cache.db"))


def public_tables(postgres: datastore.OwnedPostgres, names: dict) -> dict:
    return {kind: postgres.json(name, "SELECT COALESCE(json_agg(tablename ORDER BY tablename),'[]') "
                               "FROM pg_tables WHERE schemaname='public'") for kind, name in names.items()}


def leaked(fixture: Fixture, canaries: list[str], payload=None) -> bool:
    # Config/backup bytes legitimately contain saved credentials; application
    # outputs and API resources must not. Never include a canary in a report.
    files = [fixture.home / "server.log"] + list((fixture.home / "logs").glob("*.txt"))
    content = b"\n".join(path.read_bytes() for path in files if path.is_file())
    if payload is not None:
        content += json.dumps(payload).encode()
    return any(value.encode() in content for value in canaries)


def failed_start(fixture: Fixture, *, overrides=None) -> bool:
    try:
        fixture.start(postgres_connection_strings=overrides)
    except ValidationFailure as error:
        return (error.code == "owned_application_exited_before_healthy" and fixture.process is not None
                and fixture.process.poll() not in (None, 0))
    finally:
        fixture.stop()
    return False


def configure_fixture(runtime: Path, output: Path, fixtures: list) -> Fixture:
    fixture = Fixture(runtime, output, startup_timeout=30)
    fixtures.append(fixture)
    fixture.configure_home()
    return fixture


def source_library(baseline: Path, output: Path, fixtures: list) -> Fixture:
    fixture = configure_fixture(baseline, output / "sqlite-source", fixtures)
    fixture.start()
    upgrade.seed_current_account(fixture)
    restore.seed_library(fixture, seed_user=False)
    upgrade.seed_extra(fixture)
    fixture.stop()
    return fixture


def credential_auth(fixture: Fixture, check, prefix: str) -> None:
    check(prefix + "_legacy_account_authenticates", upgrade.authenticated(upgrade.login(fixture, fixture.username, fixture.password)))
    check(prefix + "_salted_account_authenticates", upgrade.authenticated(
        upgrade.login(fixture, fixture.current_username, fixture.current_password)))


def switch_existing(runtime: Path, baseline: Path, output: Path, fixtures: list,
                    postgres: datastore.OwnedPostgres, password: str, canary: str, check) -> tuple[Fixture, dict]:
    source = source_library(baseline, output, fixtures)
    names = owned_databases(postgres, "p14_existing")
    fixture = Fixture(baseline, output / "component-upgrade")
    fixtures.append(fixture)
    configure_component(fixture, postgres, names, password)
    fixture.start()
    fixture.stop()
    datastore.transfer_seed(postgres, names, source)
    fixture.media_hashes = dict(source.media_hashes)
    fixture.username, fixture.password, fixture.identifier = source.username, source.password, source.identifier
    fixture.current_username, fixture.current_password = source.current_username, source.current_password
    fixture.set_xml({"AuthenticationMethod": "Forms"})
    fixture.start()
    check("genuine_pre_p14_component_binary_reads_seeded_recording", fixture.api("book/13")["id"] == 13)
    credential_auth(fixture, check, "component_baseline")
    fixture.stop()
    golden = datastore.postgres_snapshot(postgres, names)
    schema = datastore.postgres_schema(postgres, names)
    check("component_baseline_has_two_recording_parts", len(golden["BookFiles"]) == 2)
    mapping = strings(postgres, names, password, application=canary)
    replace_postgres(fixture, mapping)
    fixture.runtime = runtime
    fixture.start()
    check("full_strings_never_select_sqlite_instead_of_postgres", no_sqlite(fixture))
    check("full_strings_open_same_main_database", fixture.api("book/13")["id"] == 13)
    check("full_strings_retain_recording_files", len(fixture.api("bookfile?bookId=13")) == 2)
    check("full_strings_keep_library_download_history", any(item["id"] == 23
          for item in fixture.api("history?page=1&pageSize=100").get("records", [])))
    credential_auth(fixture, check, "full_strings")
    status = fixture.api("system/status")
    check("connection_strings_are_absent_from_status_resources", not leaked(fixture, [canary], status))
    check("dsn_options_reach_the_owned_server", postgres.json("postgres", "SELECT COALESCE(json_agg(DISTINCT datname),'[]') "
          "FROM pg_stat_activity WHERE usename=" + datastore.literal(ROLE) + " AND application_name=" + datastore.literal(canary))
          and set(postgres.json("postgres", "SELECT COALESCE(json_agg(DISTINCT datname),'[]') FROM pg_stat_activity "
                  "WHERE usename=" + datastore.literal(ROLE) + " AND application_name=" + datastore.literal(canary))) == set(names.values()))
    logstorage.backup(fixture, check, "full_strings", sqlite=False)
    fixture.stop()
    check("dsn_upgrade_preserves_catalog_settings_dates_guids_cache_and_logs", datastore.postgres_snapshot(postgres, names) == golden)
    check("dsn_upgrade_preserves_all_three_database_schemas", datastore.postgres_schema(postgres, names) == schema)
    check("dsn_upgrade_never_falls_back_to_sqlite", no_sqlite(fixture))
    check("dsn_upgrade_preserves_owned_recording_bytes", restore.media_unchanged(fixture))
    check("runtime_logs_do_not_expose_dsn_credentials", not leaked(fixture, [canary]))
    return fixture, names


def fresh_and_disabled(runtime: Path, output: Path, fixtures: list,
                       postgres: datastore.OwnedPostgres, password: str, canary: str, check) -> None:
    names = owned_databases(postgres, "p14_env")
    fixture = configure_fixture(runtime, output / "environment-strings", fixtures)
    bogus = {"Postgres" + key: "invalid ignored XML " + canary for key in STRINGS.values()}
    fixture.set_xml(bogus)
    overrides = {key.removeprefix("Postgres"): value for key, value in strings(postgres, names, password, application=canary).items()}
    fixture.start(postgres_connection_strings=overrides)
    check("environment_strings_override_saved_xml_values", fixture.api("author") == [] and no_sqlite(fixture))
    host = fixture.api()
    check("connection_strings_are_absent_from_host_config_resources", not leaked(fixture, [canary], host))
    host["instanceName"] += " P14 environment proof"
    fixture.api("config/host/1", "PUT", host)
    check("environment_general_save_does_not_materialize_component_defaults", not any(
        ET.parse(fixture.home / "config.xml").getroot().find("Postgres" + key) is not None for key in COMPONENTS))
    fixture.stop()
    check("environment_strings_create_all_three_owned_schemas", all(
        set(postgres.json(name, 'SELECT json_agg("Version") FROM "VersionInfo"')) == datastore.HISTORY for name in names.values()))
    check("environment_values_are_not_persisted_over_saved_xml", all(fixture.xml_value(key) == value for key, value in bogus.items()))
    check("environment_runtime_logs_omit_connection_values", not leaked(fixture, [canary]))

    names = owned_databases(postgres, "p14_disabled", include_log=False)
    fixture = configure_fixture(runtime, output / "disabled-log-string", fixtures)
    saved = strings(postgres, names, password, application=canary)
    saved.update(LogDbEnabled="False", PostgresLogDbConnectionString="invalid unused " + canary,
                 PostgresLogDb="ignored legacy log name " + canary)
    fixture.set_xml(saved)
    fixture.start()
    page = fixture.api("log?page=2&pageSize=7&sortKey=time&sortDirection=descending")
    check("disabled_log_allows_missing_or_malformed_unused_log_settings", page.get("records") == [] and page.get("totalRecords") == 0)
    host = fixture.api()
    host["instanceName"] += " P14 saved strings proof"
    fixture.api("config/host/1", "PUT", host)
    check("general_save_preserves_exact_saved_connection_strings", all(
        fixture.xml_value(key) == value for key, value in saved.items() if key.startswith("Postgres")))
    check("disabled_general_save_does_not_materialize_active_component_defaults", not any(
        ET.parse(fixture.home / "config.xml").getroot().find("Postgres" + key) is not None
        for key in COMPONENTS if key != "LogDb"))
    logstorage.backup(fixture, check, "disabled_full_strings", sqlite=False)
    fixture.stop()
    check("disabled_strings_create_only_main_and_cache", all(
        postgres.json(name, 'SELECT json_agg("Version") FROM "VersionInfo"') for name in names.values()) and no_sqlite(fixture))
    check("disabled_log_values_never_enter_runtime_outputs", not leaked(fixture, [canary]))
    # The same required pair must reject startup after enabling without its log DSN.
    replace_postgres(fixture, {key: value for key, value in saved.items()
                              if key in ("PostgresMainDbConnectionString", "PostgresCacheDbConnectionString")})
    fixture.set_xml({"LogDbEnabled": "True"})
    check("reenabling_requires_the_log_connection_string", failed_start(fixture))
    check("rejected_reenable_does_not_fall_back_to_sqlite", no_sqlite(fixture))

    sqlite = configure_fixture(runtime, output / "disabled-log-only-sqlite", fixtures)
    sqlite.set_xml({"LogDbEnabled": "False", "PostgresLogDbConnectionString": "unused malformed " + canary})
    sqlite.start()
    check("inactive_log_only_string_does_not_select_postgres", all(
        (sqlite.home / name).is_file() for name in ("readarr.db", "cache.db")) and not (sqlite.home / "logs.db").exists())
    sqlite.stop()
    check("ignored_log_only_string_is_not_logged", not leaked(sqlite, [canary]))


def invalid_configurations(runtime: Path, output: Path, fixtures: list,
                           postgres: datastore.OwnedPostgres, password: str, canary: str, check) -> None:
    names = owned_databases(postgres, "p14_invalid")
    valid = strings(postgres, names, password, application=canary)
    cases = []
    for selected in (("main",), ("cache",), ("log",), ("main", "cache"), ("main", "log"), ("cache", "log")):
        cases.append(("missing_" + "_".join(selected), {"Postgres" + STRINGS[kind]: valid["Postgres" + STRINGS[kind]] for kind in selected}))
    for name, value in (("Host", ""), ("User", ROLE), ("Password", canary), ("Port", "0"),
                        ("MainDb", names["main"]), ("CacheDb", names["cache"]), ("LogDb", names["log"])):
        cases.append(("mixed_" + name.lower(), {**valid, "Postgres" + name: value}))
    for name, value in (("syntax", "not a connection string " + canary),
                        ("unknown_keyword", valid["PostgresMainDbConnectionString"] + ";Unrecognized Option=" + canary),
                        ("invalid_value", valid["PostgresMainDbConnectionString"] + ";Port=" + canary),
                        ("invalid_tls_value", valid["PostgresMainDbConnectionString"] + ";SSL Mode=" + canary)):
        cases.append((name, {**valid, "PostgresMainDbConnectionString": value}))
    for kind in ("cache", "log"):
        cases.append(("invalid_" + kind + "_syntax", {**valid, "Postgres" + STRINGS[kind]: "invalid ignored? " + canary}))
    for label, values in cases:
        fixture = configure_fixture(runtime, output / ("reject-" + label), fixtures)
        fixture.set_xml(values)
        check("invalid_" + label + "_is_rejected_before_startup", failed_start(fixture))
        check("invalid_" + label + "_does_not_create_sqlite_fallback", no_sqlite(fixture))
        check("invalid_" + label + "_does_not_publish_connection_values", not leaked(fixture, [canary]))
    check("invalid_settings_leave_postgres_databases_uninitialized", all(not rows for rows in public_tables(postgres, names).values()))

    # These are actual owned-server failures, beyond connection-string parsing.
    for label, changes in (("missing_role", {"username": "p14_missing_role_" + secrets.token_hex(5)}),
                           ("wrong_password", {"password": password + "invalid"}),
                           ("missing_database", {"database": "p14_missing_database_" + secrets.token_hex(5)})):
        fixture = configure_fixture(runtime, output / ("server-reject-" + label), fixtures)
        local_canaries = [canary]
        if "username" in changes:
            local_canaries.append(changes["username"])
        if "database" in changes:
            local_canaries.append(changes["database"])
        failing = dsn(postgres, changes.get("database", names["main"]), changes.get("password", password),
                      username=changes.get("username", ROLE), application=canary) + ";Include Error Detail=true"
        fixture.set_xml({**valid, "PostgresMainDbConnectionString": failing})
        check("owned_server_" + label + "_is_rejected", failed_start(fixture))
        check("owned_server_" + label + "_errors_hide_connection_values", not leaked(fixture, local_canaries))
        check("owned_server_" + label + "_does_not_fall_back_to_sqlite", no_sqlite(fixture))
    check("failed_connections_leave_owned_schemas_uninitialized", all(not rows for rows in public_tables(postgres, names).values()))


def run(runtime: Path, output: Path, *, baseline_runtime: Path | None = None) -> dict:
    if baseline_runtime is None:
        raise ValidationFailure("postgresconnections_requires_pre_p14_baseline")
    runtime, output = validate_output(runtime, output)
    baseline_runtime, _ = validate_output(baseline_runtime, output)
    if runtime == baseline_runtime:
        raise ValidationFailure("postgresconnections_requires_distinct_baseline")
    ensure_host_compatible(runtime)
    ensure_host_compatible(baseline_runtime)
    before = {"candidate": snapshot_runtime(runtime), "baseline": snapshot_runtime(baseline_runtime)}
    if before["candidate"] == before["baseline"]:
        raise ValidationFailure("postgresconnections_requires_different_payloads")
    private_directory(output)
    postgres = datastore.OwnedPostgres(output / "postgres")
    fixtures = []
    checks = {}
    completed = False
    failure = None
    stage = "owned_postgres_start"
    canary = "p14_private_" + secrets.token_hex(8)
    password = canary + ";quoted\"value'\\tail"

    def check(name, condition):
        checks[name] = bool(condition)
        if not condition:
            raise ValidationFailure(name)

    try:
        postgres.start()
        check("owned_postgres_uses_private_socket_without_published_ports", postgres.socket.stat().st_mode & 0o777 == 0o700)
        check("owned_postgres_16_is_running", postgres.sql("postgres", "SHOW server_version_num").startswith("16"))
        stage = "owned_scram_authentication"
        scram_role(postgres, password)
        check("owned_app_role_uses_real_scram_authentication", (postgres.output / "data" / "pg_hba.conf").read_text().startswith("local all " + ROLE + " scram-sha-256\n"))
        stage = "genuine_component_to_full_string_upgrade"
        switch_existing(runtime, baseline_runtime, output, fixtures, postgres, password, canary, check)
        stage = "fresh_environment_and_disabled_log_strings"
        fresh_and_disabled(runtime, output, fixtures, postgres, password, canary, check)
        stage = "invalid_and_failed_connections"
        invalid_configurations(runtime, output, fixtures, postgres, password, canary, check)
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
        for label, original in before.items():
            try:
                checks[label + "_package_unchanged"] = snapshot_runtime(runtime if label == "candidate" else baseline_runtime) == original
            except Exception:
                checks[label + "_package_unchanged"] = False
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "failure": failure, "syntheticHomesOnly": True,
            "postgresNetworkMode": "none", "publishedPorts": False,
            "scramAuthentication": True, "tlsTransportExercised": False}
