"""Exercise collection diagnostics and last-successful list membership.

Only owned loopback Readarr responses and a disposable catalog are used. The
prior package creates the home; the candidate performs all API and UI writes.
"""

from collections import Counter
from datetime import datetime, timezone
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sqlite3
from threading import Thread
from urllib.parse import urlsplit

from . import app, common, identity, library_review, recording_lifecycle, restore, upgrade


BROWSER = Path(__file__).with_name("collection-diagnostics-browser.cjs")
FIRST_LIST = 301
SECOND_LIST = 302
FIRST_NAME = "Owned Regional List"
SECOND_NAME = "Owned Shared List"


class ListEndpoint:
    def __init__(self, contract, book_ids):
        self.requests = Counter()
        self.book_ids = list(book_ids)
        self.mode = "success"
        self.contract = contract
        endpoint = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format, *args):
                pass

            def do_GET(self):
                pathname = urlsplit(self.path).path
                endpoint.requests[pathname] += 1
                status, payload = endpoint.response(pathname)
                body = json.dumps(payload).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        self.thread.start()

    @property
    def url(self):
        return "http://127.0.0.1:" + str(self.server.server_port)

    def response(self, pathname):
        if pathname == "/api/v1/author":
            return 200, [{"id": 1, "authorName": self.contract["author"]["name"],
                          "foreignAuthorId": self.contract["author"]["id"], "monitored": True,
                          "qualityProfileId": 1, "rootFolderPath": "/owned/source", "tags": []}]
        if pathname == "/api/v1/book":
            if self.mode == "failed":
                return 503, {"message": "Owned provider temporarily unavailable"}
            books = [{"id": item["id"], "title": item["title"], "foreignBookId": item["recordingId"],
                      "foreignEditionId": item["recordingId"], "authorId": 1, "monitored": True}
                     for item in self.contract["cases"] if item["id"] in self.book_ids]
            if self.mode == "partial":
                books.append({"id": 999, "title": "Owned incomplete provider row", "foreignBookId": "audible:us:B000009999",
                              "foreignEditionId": "audible:us:B000009999", "authorId": 999, "monitored": True})
            return 200, books
        if pathname == "/api/v1/qualityprofile":
            return 200, [{"id": 1, "name": "Owned quality profile"}]
        if pathname == "/api/v1/tag":
            return 200, []
        if pathname == "/api/v1/rootfolder":
            return 200, [{"id": 1, "path": "/owned/source"}]
        return 404, {"message": "Unexpected owned fixture route"}

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


def seed_list(fixture, database, list_id, name, endpoint):
    root = database.execute('SELECT "Path", "DefaultQualityProfileId", "DefaultMetadataProfileId" FROM "RootFolders" WHERE "Id"=9').fetchone()
    restore.insert(database, "ImportLists", {"Id": list_id, "Name": name, "Implementation": "ReadarrImport", "ConfigContract": "ReadarrSettings",
                       "Settings": json.dumps({"baseUrl": endpoint.url, "apiKey": "owned-fixture-api-key", "profileIds": [], "tagIds": [], "rootFolderPaths": []}),
                       "EnableAutomaticAdd": 1, "RootFolderPath": root[0], "ProfileId": root[1], "MetadataProfileId": root[2],
                       "ShouldMonitor": 0, "ShouldMonitorExisting": 0, "ShouldSearch": 0, "MonitorNewItems": 0, "Tags": "[]"})


def seed_collection(fixture, bundle, first, second):
    fixture.stop()
    now = datetime.now(timezone.utc).isoformat()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.row_factory = sqlite3.Row
        book = dict(database.execute('SELECT * FROM "Books" WHERE "Id"=108').fetchone())
        book.update(Id=109, ForeignBookId="owned-ebook-book", TitleSlug="owned-statistics-ebook", Title="Owned Statistics Ebook", CleanTitle="ownedstatisticsebook")
        edition = dict(database.execute('SELECT * FROM "Editions" WHERE "Id"=1108').fetchone())
        edition.update(Id=1109, BookId=109, ForeignEditionId="owned-ebook-edition", Asin=None, AudibleRegion=None,
                       Title="Owned Statistics Ebook", TitleSlug="owned-statistics-ebook", Format="EPUB", IsEbook=1, PageCount=321,
                       RuntimeLengthMinutes=None, ChapterMetadata=None, Narrators="[]", Contributors="[]")
        restore.insert(database, "Books", book)
        restore.insert(database, "Editions", edition)
        for file_id, edition_id, extension in ((1091, 1109, "epub"), (9901, 0, "m4b")):
            path = fixture.home / "owned-media" / ("owned-statistics-" + str(file_id) + "." + extension)
            payload = ("Owned statistics fixture " + str(file_id)).encode()
            path.write_bytes(payload)
            fixture.media_hashes[path] = hashlib.sha256(payload).hexdigest()
            restore.insert(database, "BookFiles", {"Id": file_id, "EditionId": edition_id, "Quality": '{"quality":12,"revision":{"version":1}}',
                           "Part": 1, "CalibreId": 0, "Path": str(path), "DateAdded": now, "Modified": now,
                           "Size": len(payload), "IndexerFlags": 0})
        # The primary and parallel streams describe the same file duration.
        # A stale stamp is intentional and cannot contribute known duration.
        for file_id, codec, profile, commercial, bitrate, seconds, stale in (
                (1011, "AAC", "LC", None, 128, 60, False),
                (1012, "AAC", "LC", None, 128, 70, False),
                (1021, "E-AC-3", None, "Dolby Digital Plus with Dolby Atmos", 768, 90, False),
                (1041, "USAC", "xHE-AAC", None, 64, 120, False),
                (1031, "AAC", "LC", None, 128, 9999, True)):
            size, modified = database.execute('SELECT "Size", "Modified" FROM "BookFiles" WHERE "Id"=?', (file_id,)).fetchone()
            audio = {"audioFormat": codec, "audioProfile": profile, "audioCommercialName": commercial,
                     "audioBitrate": bitrate, "audioChannels": 6 if file_id == 1021 else 2,
                     "audioSampleRate": 48000, "durationSeconds": seconds, "language": "eng"}
            info = {**audio, "source": "MediaInfo", "sourceVersion": "26.10", "containerDurationSeconds": seconds,
                    "audioStreams": [dict(audio, index=0), dict(audio, index=1)], "analysis": {
                        "revision": 1, "bundleId": "old-bundle" if stale else bundle, "size": size,
                        "modifiedUtc": modified, "complete": True}}
            database.execute('UPDATE "BookFiles" SET "MediaInfo"=? WHERE "Id"=?', (json.dumps(info), file_id))
        seed_list(fixture, database, FIRST_LIST, FIRST_NAME, first)
        seed_list(fixture, database, SECOND_LIST, SECOND_NAME, second)
        bytes_on_disk = database.execute('SELECT SUM("Size") FROM "BookFiles"').fetchone()[0]
    fixture.start()
    return bytes_on_disk


def inspect_statistics(fixture, bytes_on_disk):
    route = "librarystatistics"
    fixture.check("statistics_requires_authentication", library_review.request(fixture, route, key=False)[0] == 401)
    pack = recording_lifecycle.inspect_pack(fixture)
    stats = fixture.api(route)
    fixture.check("catalog_and_owned_recordings_are_distinct_from_works", stats["authorCount"] == 1 and stats["bookCount"] == 9 and
                  stats["audioRecordingCount"] == 8 and stats["ownedAudioRecordingCount"] == 8 and
                  stats["ebookEditionCount"] == 1 and stats["ownedEbookEditionCount"] == 1 and
                  stats["confirmedWorkCount"] == stats["ownedConfirmedWorkCount"] == 2)
    fixture.check("global_storage_counts_each_physical_file_once", stats["fileCount"] == 11 and stats["mappedFileCount"] == 10 and
                  stats["unresolvedFileCount"] == 1 and stats["sizeOnDisk"] == bytes_on_disk and
                  stats["mappedSizeOnDisk"] + stats["unresolvedSizeOnDisk"] == bytes_on_disk)
    fixture.check("known_duration_counts_parallel_streams_and_pack_just_once", stats["knownAudioDurationSeconds"] == 340 and
                  stats["audioFilesWithKnownDuration"] == 4 and stats["audioFileCount"] == 10 and stats["ebookFileCount"] == 1)
    codecs = {item["label"].lower(): item["count"] for item in stats["codecs"]}
    fixture.check("codec_breakdown_identifies_lc_atmos_and_usac", any("aac" in label and "lc" in label for label in codecs) and
                  any("atmos" in label for label in codecs) and any("usac" in label or "xhe" in label for label in codecs))
    fixture.check("bitrate_breakdown_uses_kbps_and_marks_unknown_evidence", {item["label"]: item["count"] for item in stats["bitrates"]} ==
                  {"128–191 kbps": 2, "64–95 kbps": 1, "320 kbps and above": 1, "Unknown": 6})
    local = fixture.api(route + "?source=local")
    fixture.check("local_filter_does_not_invent_unresolved_ownership", local["audioRecordingCount"] == 1 and local["fileCount"] == 1 and
                  local["unresolvedFileCount"] == 0 and local["knownAudioDurationSeconds"] == 120)
    ebooks = fixture.api(route + "?media=ebook")
    fixture.check("ebook_filter_keeps_retained_format_and_unknown_audio_duration", ebooks["ebookEditionCount"] == 1 and
                  ebooks["audioRecordingCount"] == 0 and ebooks["fileCount"] == 1 and ebooks.get("knownAudioDurationSeconds") is None)
    author = fixture.api(route + "?authorId=11")
    fixture.check("author_filter_matches_catalog_and_excludes_unresolved_files", author["bookCount"] == 9 and author["fileCount"] == 10 and author["unresolvedFileCount"] == 0)
    fixture.check("invalid_statistics_filter_is_rejected", library_review.request(fixture, route + "?media=tv")[0] == 400)
    return stats, pack


def inspect_group_evidence(fixture):
    # The acquisition/service tests exercise actual resolution. Seed its
    # persisted output here to verify the hosted file DTO and correction path.
    route = "bookfile/1011"
    evidence = {"downloadGroup": "OwnedPackUploader", "folderGroup": "OwnedPackUploader", "fileGroup": "OwnedRecordingGroup",
                "packUploaderGroup": "OwnedPackUploader", "recordingFolderGroup": "OwnedRecordingGroup",
                "recordingId": "audible:us:B011111111", "packRecordingIds": ["audible:us:B011111111", "audible:us:B033333333"],
                "resolvedGroup": "OwnedRecordingGroup", "source": "recordingFile", "reason": "exactPackRecording"}
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.execute('UPDATE "BookFiles" SET "ReleaseGroup"=?, "ReleaseGroupEvidence"=? WHERE "Id"=1011',
                         (evidence["resolvedGroup"], json.dumps(evidence)))
    original = fixture.api(route)
    fixture.check("stored_pack_evidence_is_exposed_with_original_source_labels", original["releaseGroup"] == "OwnedRecordingGroup" and original["releaseGroupEvidence"] == evidence)
    edit = dict(original, releaseGroup="OwnedManualGroup", releaseGroupEvidence=dict(evidence, packUploaderGroup="Forged edit evidence"))
    fixture.api(route, "PUT", edit)
    manual = fixture.api(route)
    raw = ("downloadGroup", "folderGroup", "fileGroup", "packUploaderGroup", "recordingFolderGroup", "recordingId", "packRecordingIds")
    fixture.check("manual_group_correction_preserves_read_only_acquisition_evidence", manual["releaseGroup"] == "OwnedManualGroup" and
                  manual["releaseGroupEvidence"]["source"] == "manual" and manual["releaseGroupEvidence"]["reason"] == "explicitChoice" and
                  all(manual["releaseGroupEvidence"][field] == evidence[field] for field in raw))
    fixture.api(route, "PUT", dict(manual, releaseGroup=""))
    cleared = fixture.api(route)
    fixture.check("explicit_empty_group_does_not_fall_back_to_pack_uploader", cleared.get("releaseGroup", "") == "" and
                  cleared["releaseGroupEvidence"]["source"] == "manual" and cleared["releaseGroupEvidence"]["packUploaderGroup"] == evidence["packUploaderGroup"])
    fixture.api(route, "PUT", dict(cleared, releaseGroup="OwnedManualGroup"))
    saved = fixture.api(route)
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        edition_id = database.execute('SELECT "EditionId" FROM "BookFiles" WHERE "Id"=1011').fetchone()[0]
    fixture.check("group_choice_never_changes_file_recording_or_quality", edition_id == 1101 and
                  all(saved.get(field) == original.get(field) for field in ("id", "bookId", "path", "size", "quality")) and restore.media_unchanged(fixture))
    return saved["releaseGroupEvidence"]


def sync_list(fixture, list_id):
    restore.wait_command(fixture, fixture.api("command", "POST", {"name": "ImportListSync", "definitionId": list_id})["id"])
    return fixture.api(f"importlistmembership/{list_id}")


def source_ids(snapshot):
    return {item["sourceBookId"] for item in snapshot["items"]}


def inspect_lists(fixture, first, second):
    route = f"importlistmembership/{FIRST_LIST}"
    fixture.check("list_snapshots_require_authentication", library_review.request(fixture, route, key=False)[0] == 401)
    initial = fixture.api(route)
    fixture.check("unfetched_list_is_distinct_from_confirmed_empty", initial.get("lastSuccessfulSync") is None and not initial["items"] and not initial["isCurrent"])
    projection = identity.snapshot(fixture)
    a = sync_list(fixture, FIRST_LIST)
    b = sync_list(fixture, SECOND_LIST)
    fixture.check("same_title_us_and_uk_recordings_remain_distinct_list_members", a["lastSuccessfulSync"] is not None and a["isCurrent"] and
                  source_ids(a) == {"audible:us:B011111111", "audible:uk:B011111111"} and
                  {item["bookId"] for item in a["items"]} == source_ids(a))
    fixture.check("overlapping_lists_have_independent_successful_snapshots", source_ids(b) == {"audible:us:B011111111", "audible:us:B033333333"})
    first.mode = "partial"
    partial = sync_list(fixture, FIRST_LIST)
    fixture.check("partial_provider_response_retains_previous_success", partial["items"] == a["items"] and partial["lastSuccessfulSync"] == a["lastSuccessfulSync"])
    first.mode = "failed"
    failed = sync_list(fixture, FIRST_LIST)
    fixture.check("failed_provider_response_retains_previous_success", failed["items"] == a["items"] and failed["lastSuccessfulSync"] == a["lastSuccessfulSync"])
    first.mode = "success"
    first.book_ids = []
    empty = sync_list(fixture, FIRST_LIST)
    fixture.check("confirmed_empty_clears_only_its_own_list", empty["isCurrent"] and empty["lastSuccessfulSync"] is not None and not empty["items"] and fixture.api(f"importlistmembership/{SECOND_LIST}") == b)
    first.book_ids = [101, 102]
    a = sync_list(fixture, FIRST_LIST)
    fixture.api("importlistexclusion", "POST", {"foreignId": "audible:uk:B011111111", "authorName": "Owned UK recording exclusion"})
    excluded = fixture.api(route)
    fixture.check("excluded_item_remains_visible_in_raw_snapshot", source_ids(excluded) == source_ids(a) and
                  {item["sourceBookId"] for item in excluded["items"] if item["excluded"]} == {"audible:uk:B011111111"})
    fixture.check("list_sync_never_changes_existing_recording_or_media_ownership", identity.snapshot(fixture) == projection and restore.media_unchanged(fixture))
    return excluded, b


def run(runtime, output, baseline_runtime, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise common.ValidationFailure("collection_diagnostics_requires_older_package")
    common.private_directory(output)
    playwright_module = Path(playwright_module).resolve(strict=True)
    contract = identity.recipe()
    first, second = ListEndpoint(contract, [101, 102]), ListEndpoint(contract, [101, 105])
    fixture = app.Fixture(baseline_runtime, output / "application")
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        identity.seed(fixture, contract)
        bundle = (runtime / "tools/mediainfo/build-id").read_text().strip()
        bytes_on_disk = seed_collection(fixture, bundle, first, second)
        baseline = identity.snapshot(fixture)
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        fixture.check("upgrade_preserves_catalog_and_media", identity.snapshot(fixture, baseline) == baseline and restore.media_unchanged(fixture))
        fixture.check("no_real_clients_or_indexers_configured", not fixture.api("downloadclient") and not fixture.api("indexer"))
        stage = "group_evidence"
        group = inspect_group_evidence(fixture)
        stage = "statistics"
        stats, pack = inspect_statistics(fixture, bytes_on_disk)
        stage = "membership"
        a, b = inspect_lists(fixture, first, second)
        stage = "browser"
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")), "playwrightModule": str(playwright_module),
                  "firstList": {"id": FIRST_LIST, "name": FIRST_NAME}, "secondList": {"id": SECOND_LIST, "name": SECOND_NAME}}
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("collection_diagnostics_browser_report_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        stage = "restart_and_backup"
        fixture.stop()
        fixture.start()
        fixture.check("restart_preserves_successful_memberships_and_statistics", fixture.api(f"importlistmembership/{FIRST_LIST}") == a and
                      fixture.api(f"importlistmembership/{SECOND_LIST}") == b and fixture.api("librarystatistics") == stats and fixture.api("bookfile/1011")["releaseGroupEvidence"] == group)
        _, backup = restore.create_original_backup(fixture)
        definition = fixture.api(f"importlist/{FIRST_LIST}")
        definition["enableAutomaticAdd"] = False
        fixture.api(f"importlist/{FIRST_LIST}?forceSave=true", "PUT", definition)
        inactive = sync_list(fixture, FIRST_LIST)
        fixture.check("disabled_list_retains_history_without_claiming_current_definition", inactive["items"] == a["items"] and
                      inactive["lastSuccessfulSync"] == a["lastSuccessfulSync"] and not inactive["isCurrent"])
        status, _ = library_review.request(fixture, f"importlist/{SECOND_LIST}", "DELETE")
        fixture.check("deleting_list_removes_only_its_membership", status in (200, 204) and library_review.request(fixture, f"importlistmembership/{SECOND_LIST}")[0] == 404 and fixture.api(f"importlistmembership/{FIRST_LIST}")["items"] == a["items"])
        status, _, body = restore.upload(fixture, "owned-collection-backup.zip", backup.read_bytes())
        fixture.check("application_accepts_owned_backup", status == 200 and json.loads(body).get("restartRequired") is True)
        fixture.stop()
        fixture.start()
        fixture.check("backup_restores_independent_memberships_and_catalog", fixture.api(f"importlistmembership/{FIRST_LIST}") == a and
                      fixture.api(f"importlistmembership/{SECOND_LIST}") == b and fixture.api("librarystatistics") == stats and
                      fixture.api("recordingwork/1106") == pack and fixture.api("bookfile/1011")["releaseGroupEvidence"] == group and restore.media_unchanged(fixture))
        fixture.check("provider_requests_use_only_expected_owned_routes", all(set(item.requests) <= {
            "/api/v1/author", "/api/v1/book", "/api/v1/qualityprofile", "/api/v1/tag", "/api/v1/rootfolder"} for item in (first, second)))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
        fixture.checks[stage + "_completed"] = False
    finally:
        fixture.cleanup()
        first.close()
        second.close()
        fixture.checks["owned_processes_stopped"] = fixture.process is None and fixture.forced_stops == 0 and not first.thread.is_alive() and not second.thread.is_alive()
    report = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", report)
    return report
