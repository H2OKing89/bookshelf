"""Application-generated backup/restore validation with new synthetic homes only."""

from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
from pathlib import Path
import secrets
import sqlite3
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

from .app import Fixture, result, safe_failure
from .common import ValidationFailure, private_directory, validate_output


TABLES = ("RootFolders", "AuthorMetadata", "Authors", "Books", "Editions", "BookFiles", "History", "Users")
SETTING_KEYS = ("backupfolder", "backupretention", "backupinterval", "usehardlinks", "preferpathasin")


def columns(database, table):
    return [row[1] for row in database.execute('PRAGMA table_info("' + table + '")')]


def insert(database, table, values):
    available = set(columns(database, table))
    selected = {name: value for name, value in values.items() if name in available}
    names = ", ".join('"' + name + '"' for name in selected)
    placeholders = ", ".join("?" for _ in selected)
    database.execute('INSERT INTO "' + table + '" (' + names + ') VALUES (' + placeholders + ')', tuple(selected.values()))


def seed_library(fixture, *, seed_user=True):
    fixture.stop()
    media = fixture.home / "owned-media" / "Synthetic Author"
    media.mkdir(parents=True, mode=0o700)
    audio = media / "Synthetic Recording.m4b"
    original = fixture.home / "owned-media" / "Synthetic Original.m4b"
    payload = b"Synthetic owned audiobook restore fixture\x00" + secrets.token_bytes(64)
    audio.write_bytes(payload)
    original.write_bytes(payload)
    fixture.media_hashes = {audio: hashlib.sha256(payload).hexdigest(), original: hashlib.sha256(payload).hexdigest()}
    now = datetime.now(timezone.utc).isoformat()
    recording = "local:recording:" + str(uuid.uuid4())
    quality = json.dumps({"quality": 12, "revision": {"version": 1, "real": 0, "isRepack": False}})
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        fixture.check("synthetic_library_starts_empty", database.execute('SELECT COUNT(*) FROM "Authors"').fetchone()[0] == 0 and
                      database.execute('SELECT COUNT(*) FROM "Users"').fetchone()[0] == (0 if seed_user else 1))
        quality_profile = database.execute('SELECT "Id" FROM "QualityProfiles" ORDER BY "Id" LIMIT 1').fetchone()
        metadata_profile = database.execute('SELECT "Id" FROM "MetadataProfiles" ORDER BY "Id" LIMIT 1').fetchone()
        fixture.check("candidate_created_default_profiles", quality_profile is not None and metadata_profile is not None)
        insert(database, "RootFolders", {"Id": 9, "Path": str(media.parent), "Name": "Synthetic media",
               "DefaultMetadataProfileId": metadata_profile[0], "DefaultQualityProfileId": quality_profile[0],
               "DefaultMonitorOption": 0, "DefaultTags": "[]", "IsCalibreLibrary": 0})
        insert(database, "AuthorMetadata", {"Id": 7, "ForeignAuthorId": "local:author:" + str(uuid.uuid4()),
               "TitleSlug": "synthetic-restore-author", "Name": "Synthetic Author", "Status": 0, "Images": "[]",
               "Genres": "[]", "Links": "[]", "Ratings": "{}", "Aliases": "[]", "SortName": "synthetic author",
               "NameLastFirst": "Author, Synthetic", "SortNameLastFirst": "author, synthetic", "SimilarAuthors": "[]"})
        insert(database, "Authors", {"Id": 11, "AuthorMetadataId": 7, "CleanName": "syntheticauthor", "Path": str(media),
               "Monitored": 0, "SortName": "synthetic author", "QualityProfileId": quality_profile[0],
               "MetadataProfileId": metadata_profile[0], "Tags": "[]", "Added": now, "LastInfoSync": now, "MonitorNewItems": 0})
        insert(database, "Books", {"Id": 13, "AuthorMetadataId": 7, "ForeignBookId": recording,
               "TitleSlug": "synthetic-restore-book", "Title": "Synthetic Recording", "CleanTitle": "syntheticrecording",
               "Monitored": 0, "AnyEditionOk": 0, "Genres": "[]", "Links": "[]", "Ratings": "{}", "RelatedBooks": "[]",
               "Classifications": "[]", "Added": now, "LastInfoSync": now, "AddOptions": "{}"})
        insert(database, "Editions", {"Id": 17, "BookId": 13, "ForeignEditionId": recording,
               "Title": "Synthetic Recording", "TitleSlug": "synthetic-restore-edition", "Language": "eng", "Format": "M4B",
               "IsEbook": 0, "Images": "[]", "Links": "[]", "Ratings": "{}", "Monitored": 1, "ManualAdd": 1,
               "Narrators": '["Fixture Narrator"]', "Contributors": "[]", "RuntimeLengthMinutes": 1,
               "ReleaseDateIsPlaceholder": 0})
        insert(database, "BookFiles", {"Id": 19, "EditionId": 17, "CalibreId": 0, "Quality": quality,
               "Size": len(payload), "DateAdded": now, "Modified": now, "Path": str(audio),
               "OriginalFilePath": str(original), "Part": 1, "IndexerFlags": 0})
        insert(database, "History", {"Id": 23, "AuthorId": 11, "BookId": 13, "SourceTitle": "Synthetic import provenance",
               "Date": now, "Quality": quality, "Data": json.dumps({"importedPath": str(audio), "droppedPath": str(original),
               "downloadClient": "synthetic", "bookFileId": "19"}), "EventType": 3, "DownloadId": "synthetic-restore-download"})
        if seed_user:
            insert(database, "Users", {"Id": 17, "Identifier": fixture.identifier, "Username": fixture.username,
                   "Password": hashlib.sha256(fixture.password.encode("utf-8")).hexdigest(), "Salt": None, "Iterations": None})
        for name, value in (("backupfolder", "Backups"), ("backupretention", "14"), ("backupinterval", "7"),
                            ("usehardlinks", "False"), ("preferpathasin", "False")):
            database.execute('INSERT OR REPLACE INTO "Config" ("Key", "Value") VALUES (?, ?)', (name, value))
        if "LastExecution" in columns(database, "ScheduledTasks"):
            database.execute('UPDATE "ScheduledTasks" SET "LastExecution" = ?', (now,))
    with sqlite3.connect(fixture.home / "cache.db") as database:
        insert(database, "HttpResponse", {"Id": 999, "Url": "https://synthetic.invalid/restore-cache-marker",
               "LastRefresh": now, "Expiry": (datetime.now(timezone.utc) + timedelta(days=1)).isoformat(),
               "Value": "synthetic restore cache marker", "StatusCode": 200})
    with sqlite3.connect(fixture.home / "logs.db") as database:
        insert(database, "Logs", {"Id": 999, "Message": "Synthetic restore log marker", "Time": now,
               "Logger": "RestoreSmoke", "Level": "Info"})
    fixture.start()
    fixture.api()


def snapshot(fixture, projection=None):
    result = {}
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        for table in TABLES:
            names = projection[table]["columns"] if projection is not None else columns(database, table)
            selection = ", ".join('"' + name + '"' for name in names)
            result[table] = {"columns": names, "rows": database.execute(
                'SELECT ' + selection + ' FROM "' + table + '" ORDER BY "Id"').fetchall()}
        placeholders = ",".join("?" for _ in SETTING_KEYS)
        result["settings"] = database.execute('SELECT "Key", "Value" FROM "Config" WHERE LOWER("Key") IN (' +
                                              placeholders + ') ORDER BY "Key"', SETTING_KEYS).fetchall()
    return result


def media_unchanged(fixture):
    return all(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == expected
               for path, expected in fixture.media_hashes.items())


def wait_command(fixture, command_id):
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        command = fixture.api("command/" + str(command_id))
        status = str(command.get("status", "")).lower()
        if status == "completed":
            return
        if status in ("failed", "cancelled", "aborted"):
            break
        time.sleep(0.1)
    raise RuntimeError("Synthetic command did not complete")


def create_original_backup(fixture):
    existing = {item["id"] for item in fixture.api("system/backup")}
    command = fixture.api("command", "POST", {"name": "Backup"})
    wait_command(fixture, command["id"])
    created = [item for item in fixture.api("system/backup") if item["id"] not in existing and
               str(item.get("type", "")).lower() == "manual"]
    fixture.check("application_generated_one_new_manual_backup", len(created) == 1)
    record = created[0]
    path = fixture.home / "Backups" / "manual" / record["name"]
    fixture.check("original_backup_is_private_and_present", path.is_file() and path.is_relative_to(fixture.home))
    with zipfile.ZipFile(path) as archive:
        names = {name.lower() for name in archive.namelist()}
        fixture.check("original_zip_contains_main_config_info_without_media_logs_or_cache",
                      names == {"config.xml", "readarr.db", "info"} and archive.testzip() is None)
    return record, path


def upload(fixture, filename, payload):
    boundary = "synthetic-restore-" + secrets.token_hex(16)
    body = ("--" + boundary + '\r\nContent-Disposition: form-data; name="file"; filename="' + filename +
            '"\r\nContent-Type: application/octet-stream\r\n\r\n').encode("ascii") + payload + (
                "\r\n--" + boundary + "--\r\n").encode("ascii")
    return fixture.request(fixture.URL_BASE + "/api/v1/system/backup/restore/upload", "POST", body, key=True,
                           headers={"Content-Type": "multipart/form-data; boundary=" + boundary})


def zipped(entries):
    destination = io.BytesIO()
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, payload in entries:
            archive.writestr(name, payload)
    return destination.getvalue()


def temporary_snapshot(fixture):
    result = {}
    for path in fixture.private_tmp.rglob("*"):
        relative = str(path.relative_to(fixture.private_tmp))
        if path.is_dir():
            result[relative] = None
        elif path.is_file():
            result[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    for workspace in fixture.home.glob(".readarr-restore-*"):
        result["workspace/" + workspace.name] = None
        for path in workspace.rglob("*"):
            relative = "workspace/" + str(path.relative_to(fixture.home))
            if path.is_dir():
                result[relative] = None
            elif path.is_file():
                result[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def pending_snapshot(fixture):
    path = fixture.home / "readarr.restore"
    return path.read_bytes() if path.is_file() else None


def invalid_restore_checks(fixture, original):
    import struct

    cases = fixture.home / "invalid-fixtures"
    cases.mkdir(mode=0o700)
    with zipfile.ZipFile(original) as archive:
        entries = {name: archive.read(name) for name in archive.namelist()}
    main_name = next(name for name in entries if name.lower() == "readarr.db")
    config_name = next(name for name in entries if name.lower() == "config.xml")
    arbitrary = cases / "arbitrary.db"
    with sqlite3.connect(arbitrary) as database:
        database.execute("CREATE TABLE Unrelated (Id INTEGER PRIMARY KEY)")
    wrong = cases / "wrong-schema.db"
    with sqlite3.connect(wrong) as database:
        database.executescript("CREATE TABLE VersionInfo (Version INTEGER); INSERT INTO VersionInfo VALUES (1); "
                               "CREATE TABLE Series (Id INTEGER PRIMARY KEY); CREATE TABLE Episodes (Id INTEGER PRIMARY KEY);")
    future = cases / "future.db"
    future.write_bytes(entries[main_name])
    with sqlite3.connect(future) as database:
        latest = database.execute('SELECT MAX("Version") FROM "VersionInfo"').fetchone()[0]
        database.execute('INSERT INTO "VersionInfo" ("Version") VALUES (?)', (int(latest) + 1000000,))
    future_entries = dict(entries)
    future_entries[main_name] = future.read_bytes()
    unknown_legacy = cases / "unknown-version.db"
    unknown_legacy.write_bytes(entries[main_name])
    with sqlite3.connect(unknown_legacy) as database:
        database.execute('INSERT INTO "VersionInfo" ("Version") VALUES (987654321)')
    unknown_legacy_entries = dict(entries)
    unknown_legacy_entries[main_name] = unknown_legacy.read_bytes()
    mixed_config = ET.fromstring(entries[config_name])
    mixed_config.find("InstanceName").text = "Readarr Rejected Mixed Restore"
    mixed = (("config.xml", ET.tostring(mixed_config, encoding="utf-8")), ("readarr.db", arbitrary.read_bytes()))
    escape = fixture.private_tmp / "restore-escape-probe"
    # Footer-only forged archives exercise entry-count checks before an archive
    # reader allocates its entry array. Run only against the fixed candidate.
    plain_count_footer = struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, 65, 65, 0, 0, 0)
    zip64_count = 1 << 32
    zip64_count_footer = (
        # ZIP64 EOCD: size44, versions45, disk0, counts, empty directory at0.
        struct.pack("<IQHHIIQQQQ", 0x06064B50, 44, 45, 45, 0, 0, zip64_count, zip64_count, 0, 0)
        # Locator: ZIP64 EOCD is at offset0 on disk0; one disk in total.
        + struct.pack("<IIQI", 0x07064B50, 0, 0, 1)
        # Classic EOCD carries sentinel entry counts and no archive comment.
        + struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, 0xFFFF, 0xFFFF, 0, 0, 0)
    )
    invalid = (
        ("malformed_xml", "invalid.xml", b"<Config><Port>not closed"),
        ("wrong_xml_root", "invalid-root.xml", b"<Settings><Port>8787</Port></Settings>"),
        ("arbitrary_sqlite", "arbitrary.db", arbitrary.read_bytes()),
        ("truncated_sqlite", "truncated.db", b"SQLite format 3\0" + bytes(84)),
        ("wrong_application_schema", "wrong-schema.db", wrong.read_bytes()),
        ("unknown_future_version", "future.zip", zipped(tuple(future_entries.items()))),
        ("unknown_legacy_version", "unknown-version.zip", zipped(tuple(unknown_legacy_entries.items()))),
        ("mixed_valid_config_invalid_db", "mixed.zip", zipped(mixed)),
        ("path_traversal", "traversal.zip", zipped(tuple(entries.items()) + (("../restore-escape-probe", b"owned probe"),))),
        ("forged_plain_eocd_entry_count", "forged-count.zip", plain_count_footer),
        ("forged_zip64_entry_count", "forged-zip64-count.zip", zip64_count_footer),
    )
    for name, filename, payload in invalid:
        before = snapshot(fixture)
        config = (fixture.home / "config.xml").read_bytes()
        pending = pending_snapshot(fixture)
        temporary = temporary_snapshot(fixture)
        response = upload(fixture, filename, payload)
        fixture.check(name + "_rejected_without_state_or_temp_change", response[0] in (400, 404) and
                      snapshot(fixture) == before and (fixture.home / "config.xml").read_bytes() == config and
                      pending_snapshot(fixture) == pending and temporary_snapshot(fixture) == temporary and not escape.exists())
        fixture.check(name + "_keeps_owned_media_and_server_healthy", media_unchanged(fixture) and
                      fixture.request(fixture.URL_BASE + "/api/v1/system/status", key=True)[0] == 200)


def cache_marker_present(fixture):
    with sqlite3.connect(fixture.home / "cache.db") as database:
        return database.execute('SELECT COUNT(*) FROM "HttpResponse" WHERE "Url" = ?',
                                ("https://synthetic.invalid/restore-cache-marker",)).fetchone()[0] > 0


def log_marker_present(fixture):
    with sqlite3.connect(fixture.home / "logs.db") as database:
        return database.execute('SELECT COUNT(*) FROM "Logs" WHERE "Message" = ?',
                                ("Synthetic restore log marker",)).fetchone()[0] == 1


def current_roundtrip(fixture):
    golden = snapshot(fixture)
    original_key = fixture.api_key
    record, path = create_original_backup(fixture)
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.execute('UPDATE "Books" SET "Title" = ? WHERE "Id" = 13', ("Changed after backup",))
        database.execute('UPDATE "History" SET "SourceTitle" = ? WHERE "Id" = 23', ("Changed provenance after backup",))
    fixture.start()
    fixture.check("post_backup_mutations_are_present", snapshot(fixture) != golden)
    fixture.check("old_cache_marker_is_present_before_restore", cache_marker_present(fixture))
    invalid_restore_checks(fixture, path)
    response = fixture.api("system/backup/restore/" + str(record["id"]), "POST", b"")
    fixture.check("listed_original_backup_is_staged_with_restart_required", response.get("restartRequired") is True and
                  (fixture.home / "readarr.restore").is_file())
    fixture.stop()
    fixture.start()
    fixture.check("current_original_backup_restores_all_recording_user_and_history_rows", snapshot(fixture) == golden)
    fixture.check("current_restore_preserves_api_key_auth_and_owned_media", fixture.xml_value("ApiKey") == original_key and
                  fixture.xml_value("AuthenticationMethod").lower() == "none" and media_unchanged(fixture))
    fixture.check("successful_startup_consumes_pending_and_retains_recovery_database",
                  not (fixture.home / "readarr.restore").exists() and
                  any(fixture.home.glob("readarr.db.pre-restore-*")))
    # Current startup policy rotates the old metadata cache with the old main DB.
    fixture.check("startup_rebuilds_cache_and_retains_its_recovery_copy", not cache_marker_present(fixture) and
                  any(fixture.home.glob("cache.db.pre-restore-*")))
    fixture.check("database_restore_keeps_existing_log_database", log_marker_present(fixture))
    return golden


def config_only_cache_check(fixture):
    fixture.stop()
    now = datetime.now(timezone.utc)
    with sqlite3.connect(fixture.home / "cache.db") as database:
        insert(database, "HttpResponse", {"Id": 999, "Url": "https://synthetic.invalid/restore-cache-marker",
               "LastRefresh": now.isoformat(), "Expiry": (now + timedelta(days=1)).isoformat(),
               "Value": "synthetic config-only restore marker", "StatusCode": 200})
    fixture.start()
    before = snapshot(fixture)
    response = upload(fixture, "config-only.xml", (fixture.home / "config.xml").read_bytes())
    fixture.check("config_only_restore_is_accepted_without_staging_a_database", response[0] == 200 and
                  json.loads(response[2]).get("restartRequired") is True and pending_snapshot(fixture) is None)
    fixture.stop()
    fixture.start()
    fixture.check("config_only_restore_keeps_cache_library_user_and_media", cache_marker_present(fixture) and
                  snapshot(fixture) == before and log_marker_present(fixture) and media_unchanged(fixture))


def genuine_legacy_roundtrip(candidate, legacy):
    legacy.configure_home()
    legacy.start()
    legacy.stop()
    with sqlite3.connect(legacy.home / "readarr.db") as database:
        legacy.check("legacy_runtime_is_genuinely_pre_password_migration",
                     "Salt" not in columns(database, "Users"))
    seed_library(legacy)
    golden = snapshot(legacy)
    record, path = create_original_backup(legacy)
    legacy.stop()
    response = upload(candidate, "original-legacy.zip", path.read_bytes())
    candidate.check("genuine_legacy_application_backup_is_accepted", response[0] == 200 and
                    json.loads(response[2]).get("restartRequired") is True)
    candidate.stop()
    candidate.api_key = legacy.api_key
    candidate.backend_port = legacy.backend_port
    candidate.backend_host = legacy.backend_host
    candidate.start()
    candidate.check("genuine_legacy_restore_upgrades_without_changing_existing_rows", snapshot(candidate, golden) == golden)
    with sqlite3.connect(candidate.home / "readarr.db") as database:
        upgraded = database.execute('SELECT "Salt", "Iterations" FROM "Users" WHERE "Id" = 17').fetchone()
        candidate.check("genuine_legacy_restore_applies_password_schema_without_reset", upgraded == (None, None))
    candidate.check("genuine_legacy_restore_preserves_original_key_and_owned_media", candidate.xml_value("ApiKey") == legacy.api_key and
                    media_unchanged(legacy) and media_unchanged(candidate))
    candidate.check("genuine_legacy_restore_rebuilds_cache_without_replacing_logs", not cache_marker_present(candidate) and
                    log_marker_present(candidate))



def run(runtime: Path, output: Path, legacy_runtime: Path | None = None) -> dict:
    runtime, output = validate_output(runtime, output)
    if legacy_runtime is not None:
        legacy_runtime, _ = validate_output(legacy_runtime, output)
        if legacy_runtime == runtime:
            raise ValidationFailure("legacy_runtime_must_differ_from_candidate")
    private_directory(output)
    candidate = Fixture(runtime, output / "candidate")
    legacy = None
    completed = False
    legacy_verified = False
    failure = None
    stage = "current_setup"
    try:
        candidate.configure_home()
        candidate.start()
        seed_library(candidate)
        stage = "current_restore"
        current_roundtrip(candidate)
        stage = "config_only_restore"
        config_only_cache_check(candidate)
        if legacy_runtime is not None:
            stage = "genuine_legacy_restore"
            legacy = Fixture(legacy_runtime, output / "legacy")
            genuine_legacy_roundtrip(candidate, legacy)
            candidate.checks.update({"legacy_" + name: value for name, value in legacy.checks.items()})
            legacy_verified = True
        completed = True
    except Exception as error:
        candidate.checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
    finally:
        clean = True
        for fixture in (legacy, candidate):
            if fixture is not None:
                try:
                    fixture.cleanup()
                    clean = clean and fixture.process is None and fixture.forced_stops == 0
                except Exception:
                    clean = False
        candidate.checks["all_owned_processes_cleaned_up"] = clean
    return result(candidate, completed, failure=failure,
                  genuineLegacyBackupVerified=legacy_verified,
                  legacyRestoreRequested=legacy_runtime is not None,
                  notRun=[] if legacy_runtime is not None else ["genuine_legacy_restore_requires_explicit_legacy_runtime"])
