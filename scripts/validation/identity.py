"""Versioned synthetic audiobook preservation recipe and hosted upgrade proof.

No existing home is accepted. SQLite seeding is intentionally the fixture
boundary; runtime API, authentication, browser and backup operations are real.
"""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import subprocess

from .app import Fixture, result, safe_failure
from .common import ValidationFailure, private_directory, snapshot_runtime, validate_output, write_json
from . import media, restore, upgrade

RECIPE = Path(__file__).with_name("fixtures") / "audiobook-identities-v1.json"
BROWSER = Path(__file__).with_name("identity-browser.cjs")
TABLES = (*restore.TABLES, "Series", "SeriesBookLink", "AudiobookMetadataOverrides")


def recipe(path=RECIPE):
    value = json.loads(Path(path).read_text())
    cases = value.get("cases", [])
    if value.get("version") != 1 or not cases or len(cases) > 32:
        raise ValidationFailure("identity_recipe_version_or_cases_invalid")
    for key in ("id", "key", "recordingId"):
        if len({item.get(key) for item in cases}) != len(cases):
            raise ValidationFailure("identity_recipe_duplicate_identity")
    for item in cases:
        identifier = item.get("recordingId", "")
        if (type(item.get("id")) is not int or item["id"] <= 0 or
                not re.fullmatch(r"[a-z][a-z-]*", item.get("key", "")) or
                type(item.get("parts")) is not int or not 1 <= item["parts"] <= 16 or
                not isinstance(item.get("title"), str) or not item["title"] or
                not isinstance(item.get("narrator"), str) or not item["narrator"]):
            raise ValidationFailure("identity_recipe_case_invalid")
        if item.get("region") is None:
            if item.get("asin") is not None or not re.fullmatch(r"local:recording:[a-f0-9]{32}", identifier):
                raise ValidationFailure("identity_recipe_local_identity_invalid")
        elif (item["region"] not in ("us", "uk") or not re.fullmatch(r"B[A-Z0-9]{9}", item.get("asin", "")) or
              identifier != "audible:" + item["region"] + ":" + item["asin"]):
            raise ValidationFailure("identity_recipe_marketplace_identity_invalid")
    return value


def snapshot(fixture, projection=None):
    """Compare baseline columns exactly while allowing additive migrations."""
    base = restore.snapshot(fixture, projection)
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        for table in TABLES[len(restore.TABLES):]:
            names = projection[table]["columns"] if projection is not None else restore.columns(database, table)
            if not names:
                raise ValidationFailure("identity_baseline_schema_missing")
            selection = ", ".join('"' + name + '"' for name in names)
            base[table] = {"columns": names, "rows": database.execute(
                'SELECT ' + selection + ' FROM "' + table + '" ORDER BY "Id"').fetchall()}
    return base


def seed(fixture, contract):
    """Seed only a freshly created, stopped home using a public JSON recipe."""
    restore.seed_library(fixture, seed_user=False)
    fixture.stop()
    now = datetime.now(timezone.utc).isoformat()
    quality = json.dumps({"quality": 12, "revision": {"version": 1, "real": 0, "isRepack": False}})
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        required = {"Editions": {"AudibleRegion", "Narrators", "Contributors", "ChapterMetadata", "IsAdult", "ReleaseDateIsPlaceholder"},
                    "Books": {"ForeignBookId", "AnyEditionOk"}, "BookFiles": {"EditionId", "Part", "OriginalFilePath"}}
        if any(not names <= set(restore.columns(database, table)) for table, names in required.items()):
            raise ValidationFailure("identity_baseline_audiobook_schema_required")
        for table in ("History", "BookFiles", "Editions", "Books"):
            database.execute('DELETE FROM "' + table + '"')
        database.execute('UPDATE "AuthorMetadata" SET "ForeignAuthorId"=? WHERE "Id"=7', (contract["author"]["id"],))
        restore.insert(database, "Series", {"Id": 501, "ForeignSeriesId": "synthetic-identity-series", "Title": "Synthetic Identity Series",
                       "Numbered": 1, "WorkCount": 8, "PrimaryWorkCount": 8})
        restore.insert(database, "AudiobookMetadataOverrides", {"Id": 10001, "Kind": "author", "Region": "us",
                       "Asin": contract["author"]["canonicalAsin"], "Name": contract["author"]["name"],
                       "AlternateAsins": json.dumps([contract["author"]["alternateAsin"]]), "Aliases": '["Synthetic Name Variant"]',
                       "SourceUrl": "https://synthetic.invalid/identity-contract", "Reason": "Owned canonical author fixture",
                       "Enabled": 1, "UpdatedAt": now})
        for item in contract["cases"]:
            identifier, edition = item["id"], item["id"] + 1000
            credits = [{"name": contract["author"]["name"], "asin": contract["author"]["canonicalAsin"], "role": "Author"}]
            if item.get("coauthor"):
                credits.append({"name": item["coauthor"], "asin": "B099999999", "role": "Author"})
            credits.append({"name": item["narrator"], "role": "Narrator"})
            restore.insert(database, "Books", {"Id": identifier, "AuthorMetadataId": 7, "ForeignBookId": item["recordingId"],
                           "TitleSlug": "synthetic-identity-" + item["key"], "Title": item["title"], "CleanTitle": item["key"],
                           "Monitored": 0, "AnyEditionOk": 0, "Genres": "[]", "Links": "[]", "Ratings": "{}", "RelatedBooks": "[]",
                           "Classifications": "[]", "Added": now, "LastInfoSync": now, "AddOptions": "{}", "ReleaseDate": None})
            chapters = {"runtimeLengthMs": 60000, "isAccurate": False, "brandIntroDurationMs": 0,
                        "chapters": [{"title": "Owned Chapter 1", "ordinal": 0, "startOffsetMs": 0, "lengthMs": 30000},
                                     {"title": "Owned Chapter 2", "ordinal": 1, "startOffsetMs": 30000, "lengthMs": 30000}]}
            restore.insert(database, "Editions", {"Id": edition, "BookId": identifier, "ForeignEditionId": item["recordingId"],
                           "Asin": item.get("asin"), "AudibleRegion": item.get("region"), "Title": item["title"],
                           "TitleSlug": "synthetic-identity-" + item["key"], "Language": "eng", "Format": "M4B", "IsEbook": 0,
                           "Images": "[]", "Links": "[]", "Ratings": "{}", "Monitored": 1, "ManualAdd": 1,
                           "Narrators": json.dumps([item["narrator"]]), "Contributors": json.dumps(credits),
                           "RuntimeLengthMinutes": 0, "ChapterMetadata": json.dumps(chapters), "CopyrightYear": 0, "IsAdult": 0,
                           "ReleaseDate": None, "SourceReleaseDate": "2050-12-30T00:00:00Z" if item.get("placeholder") else None,
                           "ReleaseDateIsPlaceholder": int(item.get("placeholder", False))})
            restore.insert(database, "SeriesBookLink", {"Id": identifier, "BookId": identifier, "SeriesId": 501,
                           "Position": item["position"], "SeriesPosition": identifier, "IsPrimary": 1})
            folder = fixture.home / "owned-media" / "Synthetic Author" / item["key"]
            folder.mkdir(mode=0o700)
            for part in range(1, item["parts"] + 1):
                path = folder / ("part-" + str(part) + ".m4b")
                original = folder / ("source-part-" + str(part) + ".m4b")
                payload = ("Owned identity fixture " + item["key"] + " part " + str(part)).encode()
                for file in (path, original):
                    file.write_bytes(payload)
                    fixture.media_hashes[file] = hashlib.sha256(payload).hexdigest()
                file_id = identifier * 10 + part
                restore.insert(database, "BookFiles", {"Id": file_id, "EditionId": edition, "Quality": quality, "Part": part,
                               "CalibreId": 0, "Path": str(path), "OriginalFilePath": str(original), "DateAdded": now,
                               "Modified": now, "Size": len(payload), "IndexerFlags": 0})
                restore.insert(database, "History", {"Id": file_id, "AuthorId": 11, "BookId": identifier, "SourceTitle": item["title"],
                               "Date": now, "Quality": quality, "EventType": 3, "DownloadId": "owned-identity-" + item["key"],
                               "Data": json.dumps({"fileId": str(file_id), "droppedPath": str(original), "importedPath": str(path),
                                                   "editionId": str(edition), "part": str(part), "partCount": str(item["parts"])})})
    fixture.start()


def check_api(fixture, contract, label):
    books = fixture.api("book")
    by_id = {item["id"]: item for item in books}
    fixture.check(label + "_distinct_recording_ids", len(by_id) == len(contract["cases"]))
    for item in contract["cases"]:
        book = by_id[item["id"]]
        prefix = label + "_" + item["key"].replace("-", "_")
        fixture.check(prefix + "_identity_and_false_values", book["foreignBookId"] == item["recordingId"] and
                      book["foreignEditionId"] == item["recordingId"] and book["monitored"] is False and book["anyEditionOk"] is False)
        fixture.check(prefix + "_narration_and_zero_values", book["narrators"] == [item["narrator"]] and
                      book["isAdult"] is False and book["copyrightYear"] == 0 and book["runtimeLengthMinutes"] == 0)
        fixture.check(prefix + "_date_and_position", book.get("releaseDate") is None and
                      book["releaseDateIsPlaceholder"] is item.get("placeholder", False) and item["position"] in book["seriesTitle"])
        files = fixture.api("bookfile?bookId=" + str(item["id"]))
        fixture.check(prefix + "_exact_file_ownership", {file["id"] for file in files} ==
                      {item["id"] * 10 + part for part in range(1, item["parts"] + 1)} and all(file["bookId"] == item["id"] for file in files))
        chapters = fixture.api("book/" + str(item["id"]) + "/chapters")
        fixture.check(prefix + "_chapters_and_explicit_false_zero", len(chapters["chapters"]) == 2 and chapters["isAccurate"] is False and chapters["brandIntroDurationMs"] == 0)
        if item.get("coauthor"):
            fixture.check(prefix + "_coauthor_credit", any(credit["name"] == item["coauthor"] and credit["role"] == "Author" for credit in book["contributors"]))
    author = fixture.api("author/11")
    fixture.check(label + "_canonical_author_profile_identity", author["id"] == 11 and author["authorMetadataId"] == 7 and
                  author["foreignAuthorId"] == contract["author"]["id"] and author["monitored"] is False)
    mapping = next(item for item in fixture.api("metadataoverride") if item["id"] == 10001)
    fixture.check(label + "_canonical_author_mapping", mapping["asin"] == contract["author"]["canonicalAsin"] and
                  mapping["alternateAsins"] == [contract["author"]["alternateAsin"]] and mapping["enabled"] is True)


def _browser_process(configuration, output):
    """Reserve the owned leader until its full process group is stopped."""
    process = None
    timed_out = False
    stdout = output / "stdout.json"
    stderr = output / "stderr.log"
    with stdout.open("xb") as log, stderr.open("xb") as errors:
        stdout.chmod(0o600)
        stderr.chmod(0o600)
        try:
            with media._defer_termination_signals():
                process = subprocess.Popen(["node", str(BROWSER)], stdin=subprocess.PIPE, stdout=log,
                                           stderr=errors, start_new_session=True, preexec_fn=media._disable_core_dumps)
            process.stdin.write(json.dumps(configuration).encode())
            process.stdin.close()
            timed_out = not media._wait_leader_without_reaping(process, 180)
        finally:
            if process is not None:
                media._stop(process, process.pid)
    if timed_out:
        raise ValidationFailure("identity_browser_timeout")
    if stdout.stat().st_size > 1024 * 1024:
        raise ValidationFailure("identity_browser_report_too_large")
    return subprocess.CompletedProcess(["node", str(BROWSER)], process.returncode, stdout.read_text(), "")


def browser(fixture, contract, output, playwright_module):
    """Credentials travel only through stdin into an owned browser process."""
    private_directory(output)
    configuration = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                     "username": fixture.current_username, "password": fixture.current_password, "apiKey": fixture.api_key,
                     "output": str(output), "cases": contract["cases"], "playwrightModule": str(playwright_module)}
    execution = _browser_process(configuration, output)
    try:
        value = json.loads(execution.stdout)
    except (ValueError, TypeError):
        raise ValidationFailure("identity_browser_report_invalid") from None
    checks = value.get("checks")
    if not isinstance(checks, dict) or not checks or not all(type(condition) is bool for condition in checks.values()):
        raise ValidationFailure("identity_browser_checks_invalid")
    for name, condition in checks.items():
        fixture.checks["browser_" + name] = condition
    write_json(output / "result.json", value)
    fixture.check("browser_completed", execution.returncode == 0 and value.get("completed") is True and all(checks.values()))


def run(runtime: Path, output: Path, baseline_runtime=None, playwright_module=None):
    if baseline_runtime is None:
        return {"checks": {}, "passed": 0, "failed": 0, "completed": False,
                "skipped": ["identity_requires_explicit_baseline_runtime"]}
    if playwright_module is None:
        raise ValidationFailure("identity_requires_explicit_playwright_module")
    playwright_module = Path(playwright_module).resolve(strict=True)
    runtime, output = validate_output(runtime, output)
    baseline_runtime, _ = validate_output(baseline_runtime, output)
    before, previous = snapshot_runtime(runtime), snapshot_runtime(baseline_runtime)
    if runtime == baseline_runtime or before == previous:
        raise ValidationFailure("identity_requires_distinct_baseline_payload")
    contract = recipe()
    private_directory(output)
    fixture = Fixture(baseline_runtime, output / "application")
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        seed(fixture, contract)
        check_api(fixture, contract, "baseline")
        _, backup = restore.create_original_backup(fixture)
        fixture.stop()
        golden = snapshot(fixture)
        credentials = upgrade.credential_rows(fixture)
        config = (fixture.home / "config.xml").read_bytes()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        fixture.check("upgrade_exact_database_identity_projection", snapshot(fixture, golden) == golden)
        fixture.check("upgrade_preserves_account_credentials", upgrade.credential_rows(fixture) == credentials)
        fixture.check("upgrade_preserves_config_and_media_bytes", (fixture.home / "config.xml").read_bytes() == config and restore.media_unchanged(fixture))
        check_api(fixture, contract, "candidate")
        stage = "browser"
        browser(fixture, contract, output / "browser", playwright_module)
        fixture.check("browser_keeps_account_credentials", upgrade.credential_rows(fixture) == credentials)
        fixture.check("browser_keeps_library_and_media", snapshot(fixture, golden) == golden and restore.media_unchanged(fixture))
        stage = "restart"
        fixture.stop()
        fixture.start()
        fixture.check("restart_preserves_full_projection", snapshot(fixture, golden) == golden and restore.media_unchanged(fixture))
        check_api(fixture, contract, "restart")
        stage = "restore_baseline_data"
        book = fixture.api("book/101")
        book["monitored"] = True
        fixture.api("book/101", "PUT", book)
        fixture.check("owned_monitoring_mutation_is_observable", fixture.api("book/101")["monitored"] is True)
        response = restore.upload(fixture, "owned-baseline.zip", backup.read_bytes())
        fixture.check("application_accepts_its_baseline_data_backup", response[0] == 200 and json.loads(response[2]).get("restartRequired") is True)
        fixture.stop()
        fixture.start()
        fixture.check("restored_baseline_data_preserves_full_projection", snapshot(fixture, golden) == golden)
        fixture.check("restored_baseline_data_preserves_credentials_and_media", upgrade.credential_rows(fixture) == credentials and restore.media_unchanged(fixture))
        check_api(fixture, contract, "restored")
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
    finally:
        try:
            fixture.cleanup()
            fixture.checks["owned_identity_processes_cleaned_up"] = fixture.process is None and fixture.forced_stops == 0
        except Exception:
            fixture.checks["owned_identity_processes_cleaned_up"] = False
        fixture.checks["candidate_package_unchanged"] = snapshot_runtime(runtime) == before
        fixture.checks["baseline_package_unchanged"] = snapshot_runtime(baseline_runtime) == previous
    report = result(fixture, completed, failure=failure, recipeVersion=contract["version"],
                    snapshotBoundary="owned SQLite fixture recipe; real hosted API/browser/backup",
                    rollbackBoundary="baseline data restored by candidate; no binary schema downgrade",
                    metadataRefreshBoundary="source regression fixtures; no external providers contacted")
    write_json(output / "result.json", report)
    return report
