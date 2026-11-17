"""Verify migration-engine upgrades against new SQLite/PostgreSQL libraries.

No existing application home or PostgreSQL endpoint is accepted. PostgreSQL runs
as the current uid in an owned, network-isolated container and exposes only a
private Unix socket. Importing this module starts nothing.
"""

from __future__ import annotations

from contextlib import closing
import csv
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import subprocess
import tempfile
import time
import uuid
import zipfile

from .app import Fixture, safe_failure
from .common import ValidationFailure, ensure_host_compatible, private_directory, snapshot_runtime, validate_output
from . import docker, restore


IMAGE = "postgres:16-alpine"
MARKER = ".bookshelf-migration-tests"
HISTORY = set(range(1, 34)) | set(range(35, 49)) | {158}
TABLES = restore.TABLES + ("AudiobookMetadataOverrides", "AudiobookMetadataCleanupRules")
SETTING_KEYS = restore.SETTING_KEYS + ("rescanafterrefresh",)
DATABASE_KINDS = ("main", "log", "cache")


def identifier(value: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", value):
        raise ValidationFailure("datastore_sql_identifier_invalid")
    return '"' + value + '"'


def literal(value, data_type: str = "") -> str:
    """Render owned fixture values; names are validated separately."""
    if value is None:
        return "NULL"
    if data_type == "boolean":
        if value not in (False, True, 0, 1):
            raise ValidationFailure("datastore_boolean_fixture_invalid")
        return "TRUE" if value else "FALSE"
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"
    if isinstance(value, (int, float)):
        if str(value) in ("nan", "inf", "-inf"):
            raise ValidationFailure("datastore_numeric_fixture_invalid")
        return str(value)
    if not isinstance(value, str) or "\x00" in value:
        raise ValidationFailure("datastore_text_fixture_invalid")
    return "'" + value.replace("'", "''") + "'"


def mount(source: Path, target: str, *, readonly=False) -> str:
    if any(character in str(source) for character in ("\x00", "\n", "\r")):
        raise ValidationFailure("datastore_mount_path_invalid")
    fields = ["type=bind", "src=" + str(source), "dst=" + target]
    if readonly:
        fields.append("readonly")
    output = io.StringIO()
    csv.writer(output, lineterminator="").writerow(fields)
    return output.getvalue()


def migrator_versions(runtime: Path):
    libraries = json.loads((runtime / "Readarr.deps.json").read_text())["libraries"]
    return sorted({name.split("/", 1)[1] for name in libraries
                   if name.split("/", 1)[0].endswith("FluentMigrator.Runner.Core")})


class OwnedPostgres:
    """The only accepted PostgreSQL server is one created by this instance."""

    def __init__(self, output: Path, *, image=IMAGE):
        self.output = Path(output).expanduser().resolve()
        self.image = image
        self.owner = uuid.uuid4().hex
        self.name = "bookshelf-validation-postgres-" + self.owner
        self.container = None
        self.environment = None
        self.socket: Path | None = None
        self.created = False

    def command(self, arguments, *, text=True, input=None, timeout=30, stdout=subprocess.PIPE):
        return subprocess.run(arguments, env=self.environment, input=input, stdout=stdout,
                              stderr=subprocess.PIPE, text=text, timeout=timeout)

    def start(self):
        private_directory(self.output)
        self.environment = docker._local_docker_environment()
        image = self.command(["docker", "image", "inspect", "--format", "{{.Id}}", self.image])
        if image.returncode or not re.fullmatch(r"sha256:[0-9a-f]{64}", image.stdout.strip()):
            raise ValidationFailure("datastore_cached_postgres_image_required")
        if docker._state(self.name, self.environment) is not None:
            raise ValidationFailure("datastore_owned_container_name_exists")
        data = private_directory(self.output / "data")
        # Unix socket paths have a small kernel length limit. The short directory
        # is private to the host uid, which also runs postgres inside the container.
        self.socket = Path(tempfile.mkdtemp(prefix="bs-pg-", dir="/tmp"))
        self.socket.chmod(0o700)
        (self.socket / MARKER).write_text("Bookshelf synthetic migration validation\n")
        (self.socket / MARKER).chmod(0o600)
        passwd = self.output / "passwd"
        passwd.write_text("postgres:x:" + str(os.getuid()) + ":" + str(os.getgid()) +
                          ":Bookshelf validation:/validation:/bin/sh\n")
        passwd.chmod(0o600)
        script = ("initdb -D /validation/data --username=postgres --encoding=UTF8 --no-locale "
                  "--auth-local=trust --auth-host=reject && "
                  "exec postgres -D /validation/data -c listen_addresses= "
                  "-c unix_socket_directories=/validation/socket -c unix_socket_permissions=0700 -c timezone=UTC")
        # Record the generated name before create, including interrupted creates.
        self.container = self.name
        created = self.command([
            "docker", "create", "--name", self.name, "--label", docker.OWNER_LABEL + "=" + self.owner,
            "--network", "none", "--user", str(os.getuid()) + ":" + str(os.getgid()),
            "--read-only", "--tmpfs", "/tmp", "--ulimit", "core=0", "--entrypoint", "/bin/sh",
            "--mount", mount(data, "/validation/data"),
            "--mount", mount(self.socket, "/validation/socket"),
            "--mount", mount(passwd, "/etc/passwd", readonly=True),
            image.stdout.strip(), "-c", script,
        ])
        if created.returncode or not re.fullmatch(r"[0-9a-f]{64}", created.stdout.strip()):
            raise ValidationFailure("datastore_postgres_create_failed")
        self.created = True
        self.container = created.stdout.strip()
        if self.command(["docker", "start", self.container]).returncode:
            raise ValidationFailure("datastore_postgres_start_failed")
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            status = docker._state(self.container, self.environment)
            if status is None or not status["Running"]:
                raise ValidationFailure("datastore_postgres_exited_before_ready")
            ready = self.command(["docker", "exec", self.container, "pg_isready", "--host=/validation/socket",
                                  "--username=postgres", "--dbname=postgres"], timeout=5)
            if ready.returncode == 0 and (self.socket / ".s.PGSQL.5432").exists():
                return self
            time.sleep(0.1)
        raise ValidationFailure("datastore_postgres_ready_timed_out")

    def sql(self, database: str, statement: str):
        identifier(database)
        completed = self.command(["docker", "exec", "-i", self.container, "psql", "--no-psqlrc",
                                  "--host=/validation/socket", "--username=postgres", "--dbname=" + database,
                                  "--tuples-only", "--no-align", "--set=ON_ERROR_STOP=1", "--set=VERBOSITY=terse"],
                                 input=statement, timeout=30)
        if completed.returncode:
            raise ValidationFailure("datastore_owned_postgres_sql_failed")
        return completed.stdout.strip()

    def json(self, database: str, query: str):
        return json.loads(self.sql(database, query))

    def databases(self, prefix: str):
        names = {kind: prefix + "_" + kind for kind in DATABASE_KINDS}
        for name in names.values():
            self.sql("postgres", "CREATE DATABASE " + identifier(name) + ";")
        return names

    def backup(self, names: dict, output: Path):
        private_directory(output)
        for kind, name in names.items():
            path = output / (kind + ".dump")
            with path.open("xb") as stream:
                path.chmod(0o600)
                completed = self.command(["docker", "exec", self.container, "pg_dump", "--host=/validation/socket",
                                          "--username=postgres", "--format=custom", "--no-owner", "--no-acl", name],
                                         text=False, stdout=stream)
            if completed.returncode or path.stat().st_size == 0:
                raise ValidationFailure("datastore_postgres_backup_failed")

    def restore(self, names: dict, output: Path):
        for kind, name in names.items():
            completed = self.command(["docker", "exec", "-i", self.container, "pg_restore", "--host=/validation/socket",
                                      "--username=postgres", "--dbname=" + name, "--clean", "--if-exists",
                                      "--no-owner", "--no-acl", "--exit-on-error"],
                                     text=False, input=(output / (kind + ".dump")).read_bytes())
            if completed.returncode:
                raise ValidationFailure("datastore_postgres_restore_failed")

    def close(self):
        clean = self.container is None
        try:
            if self.container is not None and self.environment is not None:
                state = docker._state(self.container, self.environment)
                if state is not None:
                    if not docker._owned(self.container, self.owner, self.environment):
                        raise ValidationFailure("datastore_refuses_unowned_container_cleanup")
                    try:
                        try:
                            logs = self.command(["docker", "logs", self.container], timeout=10)
                            log = self.output / "postgres.log"
                            log.write_text((logs.stdout + logs.stderr)[-2 * 1024 * 1024:])
                            log.chmod(0o600)
                        except Exception:
                            # Diagnostic collection must not prevent cleanup.
                            pass
                    finally:
                        removed = self.command(["docker", "rm", "--force", "--volumes", self.container], timeout=20)
                        clean = removed.returncode == 0 and docker._state(self.container, self.environment) is None
                else:
                    clean = True
                if clean:
                    self.container = None
        finally:
            if self.socket is not None and clean:
                shutil.rmtree(self.socket)
                self.socket = None
        return clean

    def __enter__(self):
        try:
            return self.start()
        except BaseException:
            self.close()
            raise

    def __exit__(self, kind, value, traceback):
        self.close()


def configure_postgres(fixture: Fixture, postgres: OwnedPostgres, names: dict):
    fixture.configure_home()
    fixture.set_xml({"PostgresHost": str(postgres.socket), "PostgresPort": "5432", "PostgresUser": "postgres",
                     "PostgresPassword": "", "PostgresMainDb": names["main"], "PostgresLogDb": names["log"],
                     "PostgresCacheDb": names["cache"]})


def sqlite_history(home: Path):
    result = {}
    for kind, filename in (("main", "readarr.db"), ("log", "logs.db"), ("cache", "cache.db")):
        with closing(sqlite3.connect(home / filename)) as database, database:
            result[kind] = {row[0] for row in database.execute('SELECT "Version" FROM "VersionInfo"')}
    return result


def postgres_history(postgres: OwnedPostgres, names: dict):
    return {kind: set(postgres.json(name, 'SELECT json_agg("Version") FROM "VersionInfo"')) for kind, name in names.items()}


def seed_extra(fixture: Fixture):
    fixture.stop()
    extra = fixture.home / "owned-media" / "Synthetic Author" / "Synthetic Recording Part 2.m4b"
    payload = b"Synthetic migration second part\x00"
    extra.write_bytes(payload)
    fixture.media_hashes[extra] = hashlib.sha256(payload).hexdigest()
    with closing(sqlite3.connect(fixture.home / "readarr.db")) as database, database:
        original = dict(zip(restore.columns(database, "BookFiles"), database.execute('SELECT * FROM "BookFiles" WHERE "Id"=19').fetchone()))
        original.update(Id=20, Part=2, Path=str(extra), Size=len(payload), Modified="2026-11-01T06:30:00.123456+00:00")
        restore.insert(database, "BookFiles", original)
        database.execute('UPDATE "Books" SET "ReleaseDate" = ? WHERE "Id" = 13', ("2027-02-02T00:00:00+00:00",))
        database.execute('UPDATE "Editions" SET "ReleaseDate" = ?, "Narrators" = ? WHERE "Id" = 17',
                         ("2025-11-04T00:00:00+00:00", '["Fixture Narrator","Second Narrator"]'))
        database.execute('UPDATE "AuthorMetadata" SET "Born" = ? WHERE "Id" = 7', ("1980-02-29T23:15:00+00:00",))
        restore.insert(database, "AudiobookMetadataOverrides", {
            "Id": 9001, "Kind": "narrator", "Region": "us", "Asin": None, "Name": "Owned O'Neil",
            "AlternateAsins": "[]", "Aliases": '["Owned Narrator"]', "SourceUrl": "https://synthetic.invalid/override",
            "Reason": "Synthetic migration preservation", "Enabled": 0, "UpdatedAt": "2026-10-31T12:13:14.123456+00:00"})
        restore.insert(database, "AudiobookMetadataCleanupRules", {
            "Id": 9001, "Phrase": "Synthetic owned phrase", "Labels": '["synopsis"]', "Enabled": 0,
            "Reason": "Synthetic migration preservation", "UpdatedAt": "2026-10-31T12:13:14.123456+00:00"})
        database.execute('INSERT OR REPLACE INTO "Config" ("Key","Value") VALUES (?,?)', ("rescanafterrefresh", "Never"))


def sqlite_snapshot(fixture: Fixture):
    result = {}
    with closing(sqlite3.connect(fixture.home / "readarr.db")) as database, database:
        for table in TABLES:
            names = restore.columns(database, table)
            result[table] = {"columns": names, "rows": database.execute('SELECT * FROM ' + identifier(table) + ' ORDER BY "Id"').fetchall()}
        result["settings"] = database.execute('SELECT "Key","Value" FROM "Config" WHERE "Key" IN (' +
                                               ','.join('?' for _ in SETTING_KEYS) + ') ORDER BY "Key"', SETTING_KEYS).fetchall()
    return result


def postgres_snapshot(postgres: OwnedPostgres, names: dict):
    result = {}
    for table in TABLES:
        result[table] = postgres.json(names["main"], 'SELECT COALESCE(json_agg(row_to_json(t) ORDER BY "Id"),\'[]\') FROM ' + identifier(table) + ' t')
    result["settings"] = postgres.json(names["main"], 'SELECT json_agg(row_to_json(t) ORDER BY "Key") FROM (SELECT "Key","Value" FROM "Config" WHERE "Key" IN (' +
                                      ','.join(literal(key) for key in SETTING_KEYS) + ')) t')
    result["cache"] = postgres.json(names["cache"], 'SELECT json_agg(row_to_json(t)) FROM "HttpResponse" t WHERE "Id"=999')
    result["log"] = postgres.json(names["log"], 'SELECT json_agg(row_to_json(t)) FROM "Logs" t WHERE "Id"=999')
    return result


def logical_columns(columns):
    # PostgreSQL keeps physical attnum gaps after DROP COLUMN. pg_dump recreates
    # only visible columns, compacting those numbers while preserving their
    # relative order. Compare that visible order and every other schema field.
    result = []
    positions = {}
    for column in columns or []:
        copy = dict(column)
        table = copy["table_name"]
        positions[table] = positions.get(table, 0) + 1
        copy["ordinal_position"] = positions[table]
        result.append(copy)
    return result


def postgres_schema(postgres: OwnedPostgres, names: dict):
    result = {}
    for kind, name in names.items():
        result[kind] = {
            "columns": logical_columns(postgres.json(name, "SELECT json_agg(row_to_json(t) ORDER BY table_name,ordinal_position) FROM "
                "(SELECT table_name,column_name,ordinal_position,data_type,is_nullable,column_default FROM information_schema.columns "
                "WHERE table_schema='public') t")),
            "indexes": postgres.json(name, "SELECT json_agg(row_to_json(t) ORDER BY tablename,indexname) FROM "
                "(SELECT tablename,indexname,indexdef FROM pg_indexes WHERE schemaname='public') t"),
            "constraints": postgres.json(name, "SELECT json_agg(row_to_json(t) ORDER BY tablename,conname) FROM "
                "(SELECT c.relname AS tablename,conname,pg_get_constraintdef(p.oid) AS definition FROM pg_constraint p "
                "JOIN pg_class c ON c.oid=p.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public') t"),
        }
    return result


def transfer_seed(postgres: OwnedPostgres, names: dict, source: Fixture):
    for kind, filename, tables in (("main", "readarr.db", TABLES), ("cache", "cache.db", ("HttpResponse",)), ("log", "logs.db", ("Logs",))):
        statements = ["BEGIN;"]
        with closing(sqlite3.connect(source.home / filename)) as database, database:
            for table in tables:
                columns = restore.columns(database, table)
                if table in ("AudiobookMetadataOverrides", "AudiobookMetadataCleanupRules"):
                    selection = ' WHERE "Id"=9001'
                elif kind in ("cache", "log"):
                    selection = ' WHERE "Id"=999'
                else:
                    selection = ''
                rows = database.execute('SELECT * FROM ' + identifier(table) + selection + ' ORDER BY "Id"').fetchall()
                types = postgres.json(names[kind], "SELECT json_object_agg(column_name,data_type) FROM information_schema.columns WHERE table_schema='public' AND table_name=" + literal(table))
                selected = [(index, name) for index, name in enumerate(columns) if name in types]
                for row in rows:
                    statements.append('INSERT INTO ' + identifier(table) + ' (' + ','.join(identifier(name) for _, name in selected) + ') VALUES (' +
                                      ','.join(literal(row[index], types[name]) for index, name in selected) + ');')
                if rows:
                    statements.append("SELECT setval(pg_get_serial_sequence(" + literal(identifier(table)) + ",'Id'),(SELECT MAX(\"Id\") FROM " + identifier(table) + "),true);")
            if kind == "main":
                placeholders = ','.join('?' for _ in SETTING_KEYS)
                for key, value in database.execute('SELECT "Key","Value" FROM "Config" WHERE "Key" IN (' + placeholders + ')', SETTING_KEYS):
                    statements.append('INSERT INTO "Config" ("Key","Value") VALUES (' + literal(key) + ',' + literal(value) + ') ON CONFLICT ("Key") DO UPDATE SET "Value"=EXCLUDED."Value";')
                statements.append('UPDATE "ScheduledTasks" SET "LastExecution"=CURRENT_TIMESTAMP;')
        statements.append("COMMIT;")
        postgres.sql(names[kind], '\n'.join(statements))


def failure_is_rejected(fixture: Fixture):
    try:
        fixture.start()
    except ValidationFailure as error:
        accepted = error.code == "owned_application_exited_before_healthy" and fixture.process is not None and fixture.process.poll() not in (None, 0)
    else:
        accepted = False
    finally:
        fixture.stop()
    return accepted


def run(runtime: Path, output: Path, baseline_runtime: Path | None = None) -> dict:
    if baseline_runtime is None:
        raise ValidationFailure("datastore_requires_explicit_pre_engine_baseline")
    runtime, output = validate_output(runtime, output)
    baseline_runtime, _ = validate_output(baseline_runtime, output)
    if runtime == baseline_runtime:
        raise ValidationFailure("datastore_requires_distinct_baseline")
    ensure_host_compatible(runtime)
    ensure_host_compatible(baseline_runtime)
    candidate_versions = migrator_versions(runtime)
    baseline_versions = migrator_versions(baseline_runtime)
    if not candidate_versions or any(not version.startswith("8.") for version in candidate_versions):
        raise ValidationFailure("datastore_candidate_requires_fluentmigrator_8")
    if not baseline_versions or any(int(version.split(".", 1)[0]) >= 8 for version in baseline_versions):
        raise ValidationFailure("datastore_baseline_requires_pre_fluentmigrator_8_binary")
    private_directory(output)
    checks = {}
    completed = False
    failure = None
    stage = "owned_postgres_start"
    postgres = OwnedPostgres(output / "postgres")
    fixtures = []
    before = {"candidate": snapshot_runtime(runtime), "baseline": snapshot_runtime(baseline_runtime)}
    if before["candidate"] == before["baseline"]:
        raise ValidationFailure("datastore_requires_different_baseline_payload")

    def check(name, value):
        checks[name] = bool(value)
        if not value:
            raise ValidationFailure(name)

    try:
        postgres.start()
        check("owned_postgres_uses_private_socket_without_published_ports", postgres.socket.stat().st_mode & 0o777 == 0o700)
        check("real_postgres_16_is_running", postgres.sql("postgres", "SHOW server_version_num;").startswith("16"))
        stage = "genuine_sqlite_baseline"
        sqlite = Fixture(baseline_runtime, output / "sqlite-upgrade")
        fixtures.append(sqlite)
        sqlite.configure_home()
        sqlite.start()
        check("old_binary_created_complete_sqlite_history", all(value == HISTORY for value in sqlite_history(sqlite.home).values()))
        restore.seed_library(sqlite)
        seed_extra(sqlite)
        sqlite.start()
        _, sqlite_backup = restore.create_original_backup(sqlite)
        sqlite.stop()
        golden_sqlite = sqlite_snapshot(sqlite)
        check("sqlite_seed_contains_two_recording_parts", len(golden_sqlite["BookFiles"]["rows"]) == 2)
        sqlite.runtime = runtime
        sqlite.start()
        check("candidate_reads_preserved_sqlite_book", sqlite.api("book/13")["id"] == 13)
        sqlite.stop()
        check("sqlite_catalog_overrides_narrators_dates_and_ids_survive_engine_upgrade", sqlite_snapshot(sqlite) == golden_sqlite)
        check("sqlite_history_is_not_renumbered", all(value == HISTORY for value in sqlite_history(sqlite.home).values()))
        check("sqlite_owned_media_survives_engine_upgrade", restore.media_unchanged(sqlite))

        stage = "fresh_postgres"
        fresh_names = postgres.databases("p12_fresh")
        fresh = Fixture(runtime, output / "postgres-fresh")
        fixtures.append(fresh)
        configure_postgres(fresh, postgres, fresh_names)
        fresh.start()
        check("candidate_creates_empty_postgres_library", fresh.api("author") == [] and fresh.api("book") == [])
        fresh.stop()
        check("candidate_creates_complete_postgres_main_log_cache_history", all(value == HISTORY for value in postgres_history(postgres, fresh_names).values()))
        check("postgres_home_did_not_fall_back_to_sqlite", not any((fresh.home / name).exists() for name in ("readarr.db", "logs.db", "cache.db")))

        stage = "genuine_postgres_baseline"
        names = postgres.databases("p12_upgrade")
        application = Fixture(baseline_runtime, output / "postgres-upgrade")
        fixtures.append(application)
        configure_postgres(application, postgres, names)
        application.start()
        check("old_binary_created_complete_postgres_history", all(value == HISTORY for value in postgres_history(postgres, names).values()))
        application.stop()
        transfer_seed(postgres, names, sqlite)
        application.media_hashes = dict(sqlite.media_hashes)
        application.start()
        check("old_binary_reads_seeded_postgres_recording", application.api("book/13")["id"] == 13)
        application.stop()
        golden = postgres_snapshot(postgres, names)
        schema = postgres_schema(postgres, names)
        backup = output / "postgres-point-in-time-backup"
        postgres.backup(names, backup)
        check("point_in_time_backups_exist_for_all_three_postgres_databases", all((backup / (kind + ".dump")).stat().st_size > 0 for kind in DATABASE_KINDS))
        stage = "postgres_engine_upgrade"
        application.runtime = runtime
        application.start()
        check("candidate_reads_preserved_postgres_recording", application.api("book/13")["id"] == 13)
        application.stop()
        check("postgres_catalog_overrides_narrators_dates_guids_and_ids_survive_engine_upgrade", postgres_snapshot(postgres, names) == golden)
        check("postgres_columns_defaults_indexes_and_constraints_survive_engine_upgrade", postgres_schema(postgres, names) == schema)
        check("postgres_main_log_cache_history_is_not_renumbered", all(value == HISTORY for value in postgres_history(postgres, names).values()))
        check("postgres_owned_media_survives_engine_upgrade", restore.media_unchanged(application))
        check("postgres_release_dates_and_utc_instants_are_preserved", postgres.json(names["main"],
            'SELECT json_build_array(EXTRACT(EPOCH FROM "ReleaseDate")) FROM "Books" WHERE "Id"=13') == [datetime(2027, 2, 2, tzinfo=timezone.utc).timestamp()])

        stage = "postgres_failure_recovery"
        # This is an explicit failure injection, not a manufactured old-schema
        # upgrade. Replaying historical158 adds one column, then hits a duplicate.
        postgres.sql(names["main"], 'BEGIN; ALTER TABLE "DownloadClients" DROP COLUMN "RemoveCompletedDownloads"; DELETE FROM "VersionInfo" WHERE "Version"=158; COMMIT;')
        damaged_schema = postgres_schema(postgres, names)
        check("candidate_rejects_injected_postgres_migration_failure", failure_is_rejected(application))
        check("failed_postgres_migration_preserves_saved_catalog", postgres_snapshot(postgres, names) == golden)
        check("failed_postgres_migration_rolls_back_its_partial_ddl", postgres_schema(postgres, names) == damaged_schema)
        postgres.restore(names, backup)
        application.start()
        check("point_in_time_postgres_backup_starts_after_failure", application.api("book/13")["id"] == 13)
        application.stop()
        check("point_in_time_postgres_restore_preserves_data_and_schema", postgres_snapshot(postgres, names) == golden and postgres_schema(postgres, names) == schema)
        check("point_in_time_postgres_restore_preserves_all_owned_media", restore.media_unchanged(application))

        stage = "sqlite_failure_recovery"
        with closing(sqlite3.connect(sqlite.home / "readarr.db")) as database, database:
            database.execute('ALTER TABLE "DownloadClients" DROP COLUMN "RemoveCompletedDownloads"')
            database.execute('DELETE FROM "VersionInfo" WHERE "Version"=158')
        check("candidate_rejects_injected_sqlite_migration_failure", failure_is_rejected(sqlite))
        check("failed_sqlite_migration_preserves_saved_catalog", sqlite_snapshot(sqlite) == golden_sqlite)
        with closing(sqlite3.connect(sqlite.home / "readarr.db")) as database, database:
            check("failed_sqlite_migration_rolls_back_its_partial_ddl", "RemoveCompletedDownloads" not in restore.columns(database, "DownloadClients"))
            database.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        with zipfile.ZipFile(sqlite_backup) as archive:
            restored_database = archive.read("readarr.db")
        for suffix in ("-wal", "-shm"):
            (sqlite.home / ("readarr.db" + suffix)).unlink(missing_ok=True)
        (sqlite.home / "readarr.db").write_bytes(restored_database)
        (sqlite.home / "readarr.db").chmod(0o600)
        sqlite.start()
        check("application_generated_sqlite_backup_starts_after_failure", sqlite.api("book/13")["id"] == 13)
        sqlite.stop()
        check("sqlite_backup_recovery_preserves_complete_catalog", sqlite_snapshot(sqlite) == golden_sqlite)
        check("sqlite_backup_recovery_preserves_all_owned_media", restore.media_unchanged(sqlite))
        check("healthy_application_stops_are_graceful", all(fixture.forced_stops == 0 for fixture in fixtures))
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
        for name, package in (("candidate", runtime), ("baseline", baseline_runtime)):
            try:
                checks[name + "_package_unchanged"] = snapshot_runtime(package) == before[name]
            except Exception:
                checks[name + "_package_unchanged"] = False
    return {"checks": checks, "passed": sum(checks.values()), "failed": sum(not value for value in checks.values()),
            "completed": completed, "failure": failure, "syntheticHomesOnly": True,
            "postgresImage": IMAGE, "postgresNetworkMode": "none", "publishedPorts": False,
            "genuineBinaryUpgrade": True, "failureInjectionIsNotALegacySchema": True,
            "baselineMigratorVersions": baseline_versions, "candidateMigratorVersions": candidate_versions}
