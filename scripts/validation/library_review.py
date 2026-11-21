"""Exercise recording and author review using generated media and identities."""

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import time

from . import app, common, identity, metadata_accessibility, restore, upgrade


SOURCE_AUTHOR = 12
CANONICAL_AUTHOR = 11
EXPLICIT_AUTHOR = 13
MOVED_BOOK = 105
REVIEW_FILE = 9101
CONFLICT_FILE = 9102
REGIONAL_FILE = 9103
REVIEW_EDITION = 1107
QUALITY = '{"quality":12,"revision":{"version":1}}'
BROWSER = Path(__file__).with_name("library-review-browser.cjs")
AUTHOR_REVIEW = {
    "sourceAuthorId": SOURCE_AUTHOR,
    "targetAuthorId": CANONICAL_AUTHOR,
    "evidenceUrl": "https://synthetic.invalid/owned-author-evidence",
    "reason": "Owned fixture verifies this alias and its recording credits.",
}


def generate_file(fixture, folder_name, file_name, title):
    folder = fixture.home / "owned-media" / "Synthetic Author" / folder_name
    folder.mkdir(mode=0o700)
    file = folder / file_name
    subprocess.run([
        "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
        "-metadata", "title=" + title, "-metadata", "artist=Synthetic Author",
        "-c:a", "aac", str(file)
    ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
    fixture.media_hashes[file] = hashlib.sha256(file.read_bytes()).hexdigest()
    return file


def add_unmapped(database, file_id, path):
    stat = path.stat()
    modified = datetime.fromtimestamp(stat.st_mtime, timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f").rstrip("0").rstrip(".")
    restore.insert(database, "BookFiles", {
        "Id": file_id, "EditionId": 0, "Quality": QUALITY, "Part": 1, "CalibreId": 0,
        "Path": str(path), "DateAdded": modified, "Modified": modified, "Size": stat.st_size, "IndexerFlags": 0,
    })


def seed_review_library(fixture):
    identity.seed(fixture, identity.recipe())
    fixture.stop()
    exact = generate_file(fixture, "Owned exact recording", "Synthetic Side Story [B077777777].m4b", "Synthetic Side Story")
    conflict = generate_file(fixture, "Owned conflict [B033333333]", "Synthetic Conflict [B022222222].m4b", "Synthetic Conflict")
    regional = generate_file(fixture, "Owned regional ambiguity", "Synthetic Regional [B011111111].m4b", "Synthetic Shared Work")
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.row_factory = sqlite3.Row
        add_unmapped(database, REVIEW_FILE, exact)
        add_unmapped(database, CONFLICT_FILE, conflict)
        add_unmapped(database, REGIONAL_FILE, regional)
        # Keep the catalog edition and its history. The review target has no current file.
        database.execute('DELETE FROM "BookFiles" WHERE "EditionId" = ?', (REVIEW_EDITION,))

        database.execute('DELETE FROM "AudiobookMetadataOverrides" WHERE "Id" = 10001')
        database.execute('UPDATE "AuthorMetadata" SET "Aliases" = ? WHERE "Id" = 7', ('["Synthetic Name Variant"]',))
        prefix = dict(database.execute('SELECT * FROM "AuthorMetadata" WHERE "Id" = 7').fetchone())
        for author_id, metadata_id, identity_id, slug, name in (
                (SOURCE_AUTHOR, 18, "audible:us:name:Synthetic%20Name%20Variant",
                 "synthetic-name-variant", "Synthetic Name Variant"),
                (EXPLICIT_AUTHOR, 19, "audible:us:B000000919",
                 "synthetic-distinct-author", "Synthetic Distinct Author")):
            restore.insert(database, "AuthorMetadata", dict(prefix, Id=metadata_id,
                           ForeignAuthorId=identity_id, TitleSlug=slug, Name=name, Aliases="[]"))
            canonical = dict(database.execute('SELECT * FROM "Authors" WHERE "Id" = ?', (CANONICAL_AUTHOR,)).fetchone())
            restore.insert(database, "Authors", dict(canonical, Id=author_id, AuthorMetadataId=metadata_id,
                           Monitored=int(author_id == SOURCE_AUTHOR), SortName=name.lower(),
                           CleanName=name.replace(" ", "").lower()))
        database.execute('UPDATE "Books" SET "AuthorMetadataId" = 18 WHERE "Id" = ?', (MOVED_BOOK,))
        database.execute('UPDATE "History" SET "AuthorId" = ? WHERE "BookId" = ?', (SOURCE_AUTHOR, MOVED_BOOK))
    fixture.start()
    return exact


def request(fixture, path, method="GET", data=None, *, key=True):
    code, _, body = fixture.request(fixture.URL_BASE + "/api/v1/" + path, method, data, key=key)
    try:
        value = json.loads(body) if body else None
    except ValueError:
        value = None
    return code, value


def inspect_recording_review(fixture, file):
    route = f"bookfile/{REVIEW_FILE}/recordingreview"
    fixture.check("file_review_requires_authentication", request(fixture, route, key=False)[0] == 401)
    review = fixture.api(route)
    fixture.check("filename_and_exact_local_catalog_evidence",
                  review["fileId"] == REVIEW_FILE and review["filenameAsins"] == ["B077777777"]
                  and review["folderAsins"] == [] and review["effectiveAsin"] == "B077777777"
                  and review["reason"] == "exactCatalogMatch" and not review["preferPathAsin"]
                  and review.get("currentRecording") is None)
    targets = review["candidates"]
    fixture.check("preview_names_the_exact_work_edition_and_marketplace",
                  len(targets) == 1 and targets[0]["editionId"] == REVIEW_EDITION
                  and targets[0]["recordingId"] == "audible:us:B077777777" and targets[0]["region"] == "us")
    conflict = fixture.api(f"bookfile/{CONFLICT_FILE}/recordingreview")
    fixture.check("filename_and_folder_conflict_remains_visible",
                  conflict["filenameAsins"] == ["B022222222"] and conflict["folderAsins"] == ["B033333333"]
                  and conflict["reason"] == "identifierConflict" and not any(target.get("revision") for target in conflict["candidates"]))
    regions = fixture.api(f"bookfile/{REGIONAL_FILE}/recordingreview")
    fixture.check("regional_recordings_stay_distinct",
                  {target["recordingId"] for target in regions["candidates"]} ==
                  {"audible:us:B011111111", "audible:uk:B011111111"}
                  and not any(target.get("revision") for target in regions["candidates"]))
    token = targets[0]["revision"]
    fixture.check("association_specifies_one_reviewed_edition", isinstance(token, str) and len(token) > 0)
    stat = file.stat()
    try:
        os.utime(file, ns=(stat.st_atime_ns, stat.st_mtime_ns + 10_000_000_000))
        status, _ = request(fixture, route, "PUT", {"editionId": REVIEW_EDITION, "revision": token})
        fixture.check("changed_file_invalidates_association_preview", status == 409)
        fixture.check("conflict_leaves_file_unmapped", fixture.api(route).get("currentRecording") is None)
    finally:
        os.utime(file, ns=(stat.st_atime_ns, stat.st_mtime_ns))


def rejected_reconciliation(fixture, status, body):
    if status == 409:
        return True
    command_id = (body or {}).get("commandId")
    if status not in (200, 202) or not command_id:
        return False
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        command = fixture.api("command/" + str(command_id))
        state = str(command.get("status", "")).lower()
        if state in ("failed", "completed", "cancelled", "aborted"):
            return state == "failed"
        time.sleep(0.1)
    return False


def inspect_author_review(fixture):
    candidate_route = f"authorreconciliation/candidates?sourceAuthorId={SOURCE_AUTHOR}"
    fixture.check("author_review_requires_authentication", request(fixture, candidate_route, key=False)[0] == 401)
    candidates = fixture.api(candidate_route)
    fixture.check("name_only_alias_can_find_its_canonical_author",
                  [candidate["id"] for candidate in candidates] == [CANONICAL_AUTHOR])
    preview_route = "authorreconciliation/preview"
    preview = fixture.api(preview_route, "POST", AUTHOR_REVIEW)
    fixture.check("author_preview_is_bound_to_reviewed_recording_and_file",
                  preview["source"]["id"] == SOURCE_AUTHOR and preview["target"]["id"] == CANONICAL_AUTHOR
                  and preview["canApply"] and preview["settingsDifferences"] and preview["previewToken"]
                  and [(recording["bookId"], recording["foreignEditionId"]) for recording in preview["recordings"]]
                  == [(MOVED_BOOK, "audible:us:B033333333")]
                  and [file["id"] for file in preview["files"]] == [MOVED_BOOK * 10 + 1])
    code, other = request(fixture, preview_route, "POST", dict(AUTHOR_REVIEW, sourceAuthorId=EXPLICIT_AUTHOR))
    fixture.check("two_explicit_author_ids_cannot_be_merged",
                  code in (400, 409, 422) or code == 200 and other.get("canApply") is False)

    database_path = fixture.home / "readarr.db"
    with sqlite3.connect(database_path) as database:
        database.execute('UPDATE "Authors" SET "Monitored" = FALSE WHERE "Id" = ?', (SOURCE_AUTHOR,))
    try:
        code, result = request(fixture, "authorreconciliation", "POST", dict(
            AUTHOR_REVIEW, previewToken=preview["previewToken"], keepCanonicalSettings=True))
        fixture.check("changed_author_invalidates_review", rejected_reconciliation(fixture, code, result))
        with sqlite3.connect(database_path) as database:
            fixture.check("stale_apply_preserves_source_and_target",
                          database.execute('SELECT COUNT(*) FROM "Authors" WHERE "Id" IN (?, ?)',
                                           (SOURCE_AUTHOR, CANONICAL_AUTHOR)).fetchone()[0] == 2
                          and database.execute('SELECT "AuthorMetadataId" FROM "Books" WHERE "Id" = ?', (MOVED_BOOK,)).fetchone()[0] == 18)
    finally:
        with sqlite3.connect(database_path) as database:
            database.execute('UPDATE "Authors" SET "Monitored" = TRUE WHERE "Id" = ?', (SOURCE_AUTHOR,))


def verify_committed_state(fixture, prefix=""):
    review = fixture.api(f"bookfile/{REVIEW_FILE}/recordingreview")
    target = review.get("currentRecording") or {}
    fixture.check(prefix + "only_the_reviewed_file_is_associated",
                  target.get("editionId") == REVIEW_EDITION and target.get("recordingId") == "audible:us:B077777777"
                  and all(fixture.api(f"bookfile/{file_id}/recordingreview").get("currentRecording") is None
                          for file_id in (CONFLICT_FILE, REGIONAL_FILE)))
    authors = {author["id"]: author for author in fixture.api("author")}
    fixture.check(prefix + "canonical_settings_survive_and_distinct_author_is_preserved",
                  SOURCE_AUTHOR not in authors and authors[CANONICAL_AUTHOR]["monitored"] is False
                  and authors[CANONICAL_AUTHOR]["foreignAuthorId"] == "audible:us:B055555555"
                  and authors[EXPLICIT_AUTHOR]["foreignAuthorId"] == "audible:us:B000000919")
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        book = database.execute('SELECT "AuthorMetadataId", "ForeignBookId", "Monitored" FROM "Books" WHERE "Id" = ?',
                                (MOVED_BOOK,)).fetchone()
        edition = database.execute('SELECT "ForeignEditionId", "Asin", "AudibleRegion" FROM "Editions" WHERE "Id" = ?',
                                   (MOVED_BOOK + 1000,)).fetchone()
        history = database.execute('SELECT "AuthorId", "BookId", "DownloadId" FROM "History" WHERE "Id" = ?',
                                   (MOVED_BOOK * 10 + 1,)).fetchone()
        link = database.execute('SELECT "BookId", "SeriesId", "Position" FROM "SeriesBookLink" WHERE "Id" = ?',
                                (MOVED_BOOK,)).fetchone()
        file = database.execute('SELECT "EditionId", "Part" FROM "BookFiles" WHERE "Id" = ?',
                                (MOVED_BOOK * 10 + 1,)).fetchone()
    fixture.check(prefix + "author_reassignment_preserves_recording_file_and_series_identity",
                  book == (7, "audible:us:B033333333", 0) and edition == ("audible:us:B033333333", "B033333333", "us")
                  and file == (MOVED_BOOK + 1000, 1) and link == (MOVED_BOOK, 501, "2.5"))
    fixture.check(prefix + "history_retains_id_and_download_provenance",
                  history == (CANONICAL_AUTHOR, MOVED_BOOK, "owned-identity-coauthors"))
    overrides = fixture.api("metadataoverride")
    fixture.check(prefix + "reconciliation_saves_the_reviewed_override",
                  any(row["kind"] == "author" and row["asin"] == "B055555555" and row["enabled"]
                      and "Synthetic Name Variant" in row["aliases"] for row in overrides))
    fixture.check(prefix + "audio_and_sources_are_unchanged", restore.media_unchanged(fixture))


def run(runtime, output, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    playwright_module = Path(playwright_module).resolve(strict=True)
    server, thread, requests = metadata_accessibility.metadata_server()
    provider_url = "http://127.0.0.1:" + str(server.server_address[1])
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        settings = fixture.api("config/metadataprovider/1")
        settings.update(audnexusUrl=provider_url, audnexusRegion="us", audibleRsEnabled=False)
        fixture.api("config/metadataprovider/1", "PUT", settings)
        saved = fixture.api("config/metadataprovider/1")
        fixture.check("metadata_configuration_is_loopback_only",
                      saved["audnexusUrl"] == provider_url and saved["audibleRsEnabled"] is False)
        upgrade.seed_current_account(fixture)
        file = seed_review_library(fixture)
        stage = "file_review_api"
        inspect_recording_review(fixture, file)
        stage = "author_review_api"
        inspect_author_review(fixture)
        stage = "browser"
        config = {
            "url": "http://" + fixture.backend_host + fixture.URL_BASE,
            "username": fixture.current_username,
            "password": fixture.current_password,
            "apiKey": fixture.api_key,
            "output": str(common.private_directory(output / "screenshots")),
            "playwrightModule": str(playwright_module),
            "authorReview": AUTHOR_REVIEW,
        }
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("library_review_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("library_review_browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        stage = "committed_state"
        verify_committed_state(fixture)
        fixture.stop()
        fixture.start()
        verify_committed_state(fixture, "restart_")
        fixture.check("review_uses_only_local_evidence", len(requests) == 0)
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        try:
            fixture.cleanup()
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)
        fixture.checks["owned_process_and_server_stopped"] = fixture.process is None and fixture.forced_stops == 0 and not thread.is_alive()
    value = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", value)
    return value
