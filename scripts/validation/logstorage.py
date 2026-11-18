"""Verify optional SQL log storage using fresh SQLite and owned PostgreSQL homes.

The input is a delivery package, never existing application data. PostgreSQL is
created by this run on a private Unix socket with no network or published ports.
"""

from __future__ import annotations

from contextlib import closing
from datetime import datetime, timezone
import hashlib
from pathlib import Path
import sqlite3
import time
import zipfile

from .app import Fixture, safe_failure
from .common import ValidationFailure, ensure_host_compatible, private_directory, snapshot_runtime, validate_output
from . import datastore, restore


MARKER = "Synthetic restore log marker"
UPDATE_MARKER_ID = 9001


def file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def command(fixture: Fixture, name: str) -> None:
    operation = fixture.api("command", "POST", {"name": name})
    restore.wait_command(fixture, operation["id"])


def empty_logs(fixture: Fixture, check, prefix: str) -> None:
    page = fixture.api("log?page=3&pageSize=7&sortKey=time&sortDirection=descending&level=info")
    check(prefix + "_log_browsing_returns_empty_requested_page", page.get("records") == []
          and page.get("totalRecords") == 0 and page.get("page") == 3
          and page.get("pageSize") == 7 and page.get("sortKey") == "time"
          and str(page.get("sortDirection", "")).lower() == "descending")
    command(fixture, "ClearLog")
    command(fixture, "Housekeeping")
    check(prefix + "_clear_and_housekeeping_commands_succeed", True)


def has_log_marker(fixture: Fixture) -> bool:
    page = fixture.api("log?page=1&pageSize=1000&sortKey=time&sortDirection=ascending&level=info")
    return any(row.get("message") == MARKER for row in page.get("records", []))


def sql_logging_writes(fixture: Fixture, check, prefix: str) -> None:
    previous = fixture.api("log?page=1&pageSize=1&sortKey=time&sortDirection=descending&level=info")
    previous_id = max((row.get("id", 0) for row in previous.get("records", [])), default=0)
    command(fixture, "Housekeeping")
    deadline = time.monotonic() + 10
    found = False
    while time.monotonic() < deadline:
        page = fixture.api("log?page=1&pageSize=1000&sortKey=time&sortDirection=descending&level=info")
        found = any(row.get("message") == "Running housecleaning tasks" and row.get("id", 0) > previous_id
                    for row in page.get("records", []))
        if found:
            break
        time.sleep(0.1)
    check(prefix + "_sql_target_writes_info_events", found)


def backup(fixture: Fixture, check, prefix: str, *, sqlite: bool) -> None:
    before = {item["id"] for item in fixture.api("system/backup")}
    command(fixture, "Backup")
    records = [item for item in fixture.api("system/backup") if item["id"] not in before
               and str(item.get("type", "")).lower() == "manual"]
    check(prefix + "_manual_backup_succeeds", len(records) == 1)
    name = records[0]["name"]
    if Path(name).name != name:
        raise ValidationFailure("logstorage_backup_filename_invalid")
    path = fixture.home / "Backups" / "manual" / name
    check(prefix + "_backup_is_owned_and_present", path.is_file() and not path.is_symlink())
    with zipfile.ZipFile(path) as archive:
        expected = {"config.xml", "info"} | ({"readarr.db"} if sqlite else set())
        check(prefix + "_backup_keeps_existing_main_config_contract", {name.lower() for name in archive.namelist()} == expected
              and archive.testzip() is None)


def independent_logs(fixture: Fixture, check, prefix: str) -> None:
    # Backup emits this Info message after the application's optional SQL target
    # has been configured. It therefore proves more than early bootstrap output.
    check(prefix + "_console_logging_continues", b"Starting Backup" in (fixture.home / "server.log").read_bytes())
    path = fixture.home / "logs" / "readarr.txt"
    check(prefix + "_file_logging_continues", path.is_file() and b"Starting Backup" in path.read_bytes())


def configure(fixture: Fixture, *, enabled: bool | None = None) -> None:
    fixture.configure_home()
    values = {"LogLevel": "Info", "ConsoleLogLevel": "Info"}
    if enabled is not None:
        values["LogDbEnabled"] = str(enabled)
    fixture.set_xml(values)


def sqlite_cache(fixture: Fixture):
    with closing(sqlite3.connect(fixture.home / "cache.db")) as database:
        return database.execute('SELECT * FROM "HttpResponse" WHERE "Id"=999').fetchall()


def sqlite_update(fixture: Fixture):
    with closing(sqlite3.connect(fixture.home / "logs.db")) as database:
        return database.execute('SELECT * FROM "UpdateHistory" WHERE "Id"=?', (UPDATE_MARKER_ID,)).fetchall()


def sqlite_fresh(runtime: Path, output: Path, fixtures: list, check) -> None:
    fixture = Fixture(runtime, output / "sqlite-fresh-disabled")
    fixtures.append(fixture)
    configure(fixture, enabled=False)
    fixture.start()
    empty_logs(fixture, check, "sqlite_fresh_disabled")
    backup(fixture, check, "sqlite_fresh_disabled", sqlite=True)
    fixture.stop()
    check("sqlite_fresh_disabled_never_creates_log_database_or_sidecars",
          not any((fixture.home / ("logs.db" + suffix)).exists() for suffix in ("", "-wal", "-shm", "-journal")))
    check("sqlite_fresh_disabled_still_initializes_main_and_cache", all(
        (fixture.home / name).is_file() for name in ("readarr.db", "cache.db")))
    independent_logs(fixture, check, "sqlite_fresh_disabled")

    environment = Fixture(runtime, output / "sqlite-environment-disabled")
    fixtures.append(environment)
    configure(environment, enabled=True)
    # A directory at the database pathname would fail if anything opened or
    # migrated the unused connection. The sentinel belongs only to this fixture.
    blocked = private_directory(environment.home / "logs.db")
    sentinel = blocked / "owned-sentinel"
    sentinel.write_bytes(b"Owned unused log path\n")
    environment.start(log_db_enabled=False)
    empty_logs(environment, check, "sqlite_environment_disabled")
    environment.stop()
    check("environment_false_overrides_xml_true_without_opening_log_path",
          sentinel.read_bytes() == b"Owned unused log path\n" and list(blocked.iterdir()) == [sentinel]
          and environment.xml_value("LogDbEnabled").lower() == "true")


def sqlite_existing(runtime: Path, output: Path, fixtures: list, check) -> Fixture:
    fixture = Fixture(runtime, output / "sqlite-existing")
    fixtures.append(fixture)
    configure(fixture)
    check("sqlite_default_starts_without_an_explicit_log_switch", fixture.xml_value("LogDbEnabled") == "")
    fixture.start()
    check("sqlite_default_creates_log_database", (fixture.home / "logs.db").is_file())
    sql_logging_writes(fixture, check, "sqlite_default")
    restore.seed_library(fixture)
    fixture.stop()
    with closing(sqlite3.connect(fixture.home / "logs.db")) as database, database:
        restore.insert(database, "UpdateHistory", {"Id": UPDATE_MARKER_ID,
                       "Date": datetime.now(timezone.utc).isoformat(), "Version": "0.1.0.0", "EventType": 2})
    fixture.start()
    check("sqlite_enabled_log_api_returns_saved_rows", has_log_marker(fixture))
    fixture.stop()
    golden = restore.snapshot(fixture)
    cache = sqlite_cache(fixture)
    update = sqlite_update(fixture)
    log = fixture.home / "logs.db"
    before = file_hash(log)
    mode = log.stat().st_mode & 0o777
    fixture.set_xml({"LogDbEnabled": "False"})
    try:
        log.chmod(0)
        fixture.start()
        empty_logs(fixture, check, "sqlite_existing_disabled")
        backup(fixture, check, "sqlite_existing_disabled", sqlite=True)
        check("sqlite_disabled_library_and_download_history_remain_available",
              fixture.api("book/13").get("id") == 13
              and any(item.get("id") == 23 for item in fixture.api("history?page=1&pageSize=100").get("records", [])))
        fixture.stop()
    finally:
        try:
            fixture.stop()
        finally:
            log.chmod(mode)
    check("sqlite_disabled_does_not_change_existing_log_bytes", file_hash(log) == before)
    check("sqlite_disabled_preserves_library_history_identity_and_settings", restore.snapshot(fixture) == golden)
    check("sqlite_disabled_preserves_metadata_cache", sqlite_cache(fixture) == cache and bool(cache))
    check("sqlite_disabled_preserves_owned_media", restore.media_unchanged(fixture))
    independent_logs(fixture, check, "sqlite_existing_disabled")
    fixture.set_xml({"LogDbEnabled": "True"})
    fixture.start()
    check("sqlite_reenabled_reads_the_existing_log_database", has_log_marker(fixture))
    sql_logging_writes(fixture, check, "sqlite_reenabled")
    fixture.stop()
    check("sqlite_reenabled_preserves_existing_update_history", sqlite_update(fixture) == update and bool(update))
    return fixture


def configure_postgres(fixture: Fixture, postgres: datastore.OwnedPostgres, names: dict, *, enabled=None) -> None:
    configure(fixture, enabled=enabled)
    fixture.set_xml({"PostgresHost": str(postgres.socket), "PostgresPort": "5432", "PostgresUser": "postgres",
                     "PostgresPassword": "", "PostgresMainDb": names["main"], "PostgresLogDb": names["log"],
                     "PostgresCacheDb": names["cache"]})


def postgres_log_snapshot(postgres: datastore.OwnedPostgres, name: str):
    return {table: postgres.json(name, 'SELECT COALESCE(json_agg(row_to_json(t) ORDER BY '
                                + datastore.identifier("Version" if table == "VersionInfo" else "Id") + '),\'[]\') FROM '
                                + datastore.identifier(table) + ' t') for table in ("Logs", "UpdateHistory", "VersionInfo")}


def postgres_preserved_snapshot(postgres: datastore.OwnedPostgres, names: dict):
    # Existing migration snapshots already cover all preserved catalog fields;
    # exclude the SQL log marker because this switch intentionally hides it.
    value = datastore.postgres_snapshot(postgres, names)
    value.pop("log")
    return value


def postgres_checks(runtime: Path, output: Path, fixtures: list, check,
                    postgres: datastore.OwnedPostgres, source: Fixture) -> None:
    names = {"main": "p15_fresh_main", "cache": "p15_fresh_cache", "log": "p15_unused_log"}
    for kind in ("main", "cache"):
        postgres.sql("postgres", "CREATE DATABASE " + datastore.identifier(names[kind]))
    fresh = Fixture(runtime, output / "postgres-fresh-disabled")
    fixtures.append(fresh)
    configure_postgres(fresh, postgres, names, enabled=False)
    fresh.start()
    empty_logs(fresh, check, "postgres_fresh_disabled")
    backup(fresh, check, "postgres_fresh_disabled", sqlite=False)
    fresh.stop()
    check("postgres_disabled_does_not_create_the_unused_log_database", postgres.json("postgres",
          "SELECT to_json(NOT EXISTS(SELECT 1 FROM pg_database WHERE datname=" + datastore.literal(names["log"]) + "))") is True)
    check("postgres_disabled_initializes_main_and_cache_history", all(postgres.json(names[kind],
          'SELECT json_agg("Version" ORDER BY "Version") FROM "VersionInfo"') == sorted(datastore.HISTORY)
          for kind in ("main", "cache")))
    check("postgres_disabled_does_not_fall_back_to_sqlite", not any(
        (fresh.home / name).exists() for name in ("readarr.db", "cache.db", "logs.db")))
    independent_logs(fresh, check, "postgres_fresh_disabled")

    names = postgres.databases("p15_existing")
    fixture = Fixture(runtime, output / "postgres-existing")
    fixtures.append(fixture)
    configure_postgres(fixture, postgres, names)
    fixture.start()
    fixture.stop()
    datastore.transfer_seed(postgres, names, source)
    postgres.sql(names["log"], 'INSERT INTO "UpdateHistory" ("Id","Date","Version","EventType") VALUES ('
                 + str(UPDATE_MARKER_ID) + ",CURRENT_TIMESTAMP,'0.1.0.0',2)")
    fixture.media_hashes = dict(source.media_hashes)
    fixture.start()
    check("postgres_default_keeps_sql_log_browsing_available", has_log_marker(fixture))
    sql_logging_writes(fixture, check, "postgres_default")
    fixture.stop()
    golden = postgres_preserved_snapshot(postgres, names)
    logs = postgres_log_snapshot(postgres, names["log"])
    fixture.set_xml({"LogDbEnabled": "False"})
    postgres.sql("postgres", "ALTER DATABASE " + datastore.identifier(names["log"]) + " ALLOW_CONNECTIONS false")
    try:
        fixture.start()
        empty_logs(fixture, check, "postgres_existing_disabled")
        backup(fixture, check, "postgres_existing_disabled", sqlite=False)
        check("postgres_disabled_library_and_download_history_remain_available",
              fixture.api("book/13").get("id") == 13
              and any(item.get("id") == 23 for item in fixture.api("history?page=1&pageSize=100").get("records", [])))
        fixture.stop()
    finally:
        try:
            fixture.stop()
        finally:
            postgres.sql("postgres", "ALTER DATABASE " + datastore.identifier(names["log"]) + " ALLOW_CONNECTIONS true")
    check("postgres_disabled_preserves_existing_log_and_update_rows", postgres_log_snapshot(postgres, names["log"]) == logs)
    check("postgres_disabled_preserves_library_history_metadata_cache_and_settings",
          postgres_preserved_snapshot(postgres, names) == golden)
    check("postgres_disabled_preserves_owned_media", restore.media_unchanged(fixture))
    independent_logs(fixture, check, "postgres_existing_disabled")
    fixture.set_xml({"LogDbEnabled": "True"})
    fixture.start()
    check("postgres_reenabled_reads_existing_log_database", has_log_marker(fixture))
    sql_logging_writes(fixture, check, "postgres_reenabled")
    fixture.stop()
    check("postgres_reenabled_keeps_existing_update_history", postgres.json(names["log"],
          'SELECT COUNT(*) FROM "UpdateHistory" WHERE "Id"=' + str(UPDATE_MARKER_ID)) == 1)


def run(runtime: Path, output: Path) -> dict:
    runtime, output = validate_output(runtime, output)
    ensure_host_compatible(runtime)
    private_directory(output)
    before = snapshot_runtime(runtime)
    checks = {}
    fixtures = []
    postgres = datastore.OwnedPostgres(output / "postgres")
    completed = False
    failure = None
    stage = "sqlite_fresh_disabled"

    def check(name, condition):
        checks[name] = bool(condition)
        if not condition:
            raise ValidationFailure(name)

    try:
        sqlite_fresh(runtime, output, fixtures, check)
        stage = "sqlite_existing_toggle"
        source = sqlite_existing(runtime, output, fixtures, check)
        stage = "owned_postgres_start"
        postgres.start()
        check("owned_postgres_uses_private_socket_without_published_ports", postgres.socket.stat().st_mode & 0o777 == 0o700)
        check("real_postgres_16_is_running", postgres.sql("postgres", "SHOW server_version_num").startswith("16"))
        stage = "postgres_disabled_and_existing_toggle"
        postgres_checks(runtime, output, fixtures, check, postgres, source)
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
            checks["owned_postgres_container_and_private_socket_removed"] = postgres.close()
        except Exception:
            checks["owned_postgres_container_and_private_socket_removed"] = False
        try:
            checks["input_package_unchanged"] = snapshot_runtime(runtime) == before
        except Exception:
            checks["input_package_unchanged"] = False
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "failure": failure, "syntheticHomesOnly": True,
            "postgresImage": datastore.IMAGE, "postgresNetworkMode": "none", "publishedPorts": False}
