"""Upgrade and exercise local-recording edits and explicit work coverage.

The older binary creates the owned home. Seeded catalog rows and generated
audio are the fixture boundary; edits, scans, backup and restore use the
candidate's real HTTP API and browser.
"""

import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess

from . import app, common, identity, library_review, restore, upgrade


LOCAL_BOOK = 104
PACK = 1106
LOCAL_FILE = 9901
AUTHOR_NAME = "Owned Independent Author"
BROWSER_FILE = 9902
BROWSER_AUTHOR = "Owned Browser Author"
BROWSER = Path(__file__).with_name("recording-lifecycle-browser.cjs")


def seed_unmapped_audio(fixture):
    fixture.stop()
    folders = []
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        for file_id, author, title in ((LOCAL_FILE, AUTHOR_NAME, "Owned Independent Audiobook"),
                                      (BROWSER_FILE, BROWSER_AUTHOR, "Owned Browser Audiobook")):
            folder = fixture.home / "owned-media" / author
            folder.mkdir(mode=0o700)
            audio = folder / (title + ".m4b")
            subprocess.run([
                "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-metadata", "title=" + title, "-metadata", "artist=" + author,
                "-metadata", "album=" + title, "-c:a", "aac", str(audio)
            ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
            fixture.media_hashes[audio] = hashlib.sha256(audio.read_bytes()).hexdigest()
            library_review.add_unmapped(database, file_id, audio)
            folders.append(folder)
    fixture.start()
    return folders


def update_work(fixture, edition_id, members):
    route = f"recordingwork/{edition_id}"
    before = fixture.api(route)
    return fixture.api(route, "PUT", {"revision": before["revision"], "memberships": members})


def new_work(title, position, confirmed=True):
    return {"confirmed": confirmed, "newWork": {"title": title, "author": "Synthetic Author",
            "series": "Synthetic Identity Series", "position": position}}


def inspect_pack(fixture):
    route = f"recordingwork/{PACK}"
    before = fixture.api(route)
    fixture.check("range_is_not_inferred_as_work_membership", before["memberships"] == [] and before["coveredWorkCount"] == 0)
    fixture.check("work_coverage_requests_require_authentication", library_review.request(fixture, route, key=False)[0] == 401)
    before_projection = identity.snapshot(fixture)
    pack = fixture.api(route, "PUT", {"revision": before["revision"], "memberships": [
        new_work("Owned Volume One", "1"), new_work("Owned Volume Two and a Half", "2.5"),
        new_work("Owned Uncertain Side Story", "3.5?", False)]})
    by_position = {item["work"]["position"]: item["work"]["id"] for item in pack["memberships"]}
    fixture.check("pack_memberships_are_independent_explicit_works", len(by_position) == 3 and pack["coveredWorkCount"] == 2 and pack["relatedFileCount"] == 1)
    one = {"workId": by_position["1"], "confirmed": True}
    two = {"workId": by_position["2.5"], "confirmed": True}
    uncertain = {"workId": by_position["3.5?"], "confirmed": False}
    us = update_work(fixture, 1101, [one])
    uk = update_work(fixture, 1102, [one])
    standalone = update_work(fixture, 1105, [two])
    pack = fixture.api(route)
    expected_bytes = sum(item["recording"]["sizeOnDisk"] for item in (us, uk, standalone, pack))
    fixture.check("coverage_counts_pack_and_regional_files_once", pack["coveredWorkCount"] == 2 and
                  pack["relatedRecordingCount"] == 4 and pack["relatedFileCount"] == 5 and pack["relatedSizeOnDisk"] == expected_bytes)
    first = next(item for item in pack["memberships"] if item["work"]["id"] == one["workId"])
    fixture.check("regional_recordings_remain_distinct_work_members", {item["recording"]["foreignEditionId"] for item in first["recordings"]} ==
                  {"audible:us:B011111111", "audible:uk:B011111111", "audible:us:B044444444"})
    side = next(item for item in pack["memberships"] if item["work"]["id"] == uncertain["workId"])
    fixture.check("uncertain_membership_remains_distinct_from_coverage", side["confirmed"] is False and not side["hasConfirmedFiles"])
    search = fixture.api("recordingwork/works?term=Owned%20Volume")
    fixture.check("existing_work_search_keeps_distinct_ids", {item["id"] for item in search} == {one["workId"], two["workId"]})

    stale = library_review.request(fixture, route, "PUT", {"revision": before["revision"], "memberships": []})[0]
    fixture.check("stale_pack_membership_replacement_is_rejected", stale == 409 and fixture.api(route)["revision"] == pack["revision"])
    bad = library_review.request(fixture, route, "PUT", {"revision": pack["revision"], "memberships": [one, one]})[0]
    fixture.check("duplicate_work_membership_is_rejected_without_mutation", bad == 400 and fixture.api(route)["revision"] == pack["revision"])
    corrected = update_work(fixture, PACK, [one, uncertain])
    fixture.check("correction_changes_only_the_selected_recording_memberships", corrected["relatedFileCount"] == 4 and
                  corrected["coveredWorkCount"] == 1 and fixture.api("recordingwork/1105")["memberships"][0]["work"]["id"] == two["workId"])
    pack = update_work(fixture, PACK, [one, two, uncertain])
    fixture.check("coverage_never_reassigns_recordings_or_media", identity.snapshot(fixture) == before_projection and restore.media_unchanged(fixture))
    return pack


def editable(recording, **changes):
    fields = ("revision", "title", "subtitle", "narrators", "language", "publisher", "year", "chapterMetadata")
    return dict({name: recording.get(name) for name in fields}, **changes)


def inspect_local(fixture, folder):
    route = f"localrecording/{LOCAL_BOOK}"
    initial = fixture.api(route)
    fixture.check("local_edit_requires_authentication", library_review.request(fixture, route, key=False)[0] == 401)
    fixture.check("legacy_local_recording_retains_exact_identity", initial["recordingId"] == "local:recording:" + "1" * 32 and initial["fileIds"] == [1041])
    fixture.check("provider_recording_is_not_a_local_edit_target", library_review.request(fixture, "localrecording/101")[0] in (400, 404, 409))
    edit = editable(initial, title="Owned Corrected Recording", subtitle="Owned corrected subtitle", narrators=["Second Narrator", "First Narrator"],
                    publisher="Independent Owned Publisher", language="eng", year=None)
    fixture.api(route, "PUT", edit)
    saved = fixture.api(route)
    fixture.check("legacy_local_metadata_edit_preserves_ordered_chapters_and_identity", saved["recordingId"] == initial["recordingId"] and
                  saved["fileIds"] == initial["fileIds"] and saved["chapterMetadata"] == initial["chapterMetadata"] and
                  saved["narrators"] == edit["narrators"] and saved.get("year") is None and saved["title"] == edit["title"] and saved["revision"] != initial["revision"])
    fixture.check("manual_metadata_provenance_is_persisted", all(saved["provenance"][field] == "manual" for field in ("title", "subtitle", "narrators", "publisher")))
    fixture.check("stale_local_metadata_edit_is_rejected", library_review.request(fixture, route, "PUT", edit)[0] == 409 and fixture.api(route)["revision"] == saved["revision"])

    preview = fixture.api(f"localrecording?fileIds={LOCAL_FILE}")
    fixture.check("unmapped_audio_has_real_tag_title", preview["title"] == "Owned Independent Audiobook" and preview["fileIds"] == [LOCAL_FILE])
    create = dict(preview, authorId=0, newAuthorName=AUTHOR_NAME, newAuthorPath=str(folder), year=None)
    book = fixture.api("localrecording", "POST", create)
    recording = fixture.api(f"localrecording/{book['id']}")
    author = fixture.api(f"author/{recording['authorId']}")
    fixture.check("new_local_author_and_recording_have_separate_local_ids", author["foreignAuthorId"].startswith("local:author:") and
                  recording["recordingId"].startswith("local:recording:") and author["authorName"] == AUTHOR_NAME and
                  recording["fileIds"] == [LOCAL_FILE] and author["path"] == str(folder))
    fixture.check("new_recording_does_not_fabricate_an_asin", not book.get("asin") and not book.get("audibleRegion") and book["foreignBookId"] == recording["recordingId"])
    fixture.check("already_assigned_file_cannot_create_duplicate_author", library_review.request(fixture, "localrecording", "POST", create)[0] in (400, 409) and
                  sum(a["authorName"] == AUTHOR_NAME for a in fixture.api("author")) == 1)
    chapters = {"runtimeLengthMs": 2000, "isAccurate": True, "brandIntroDurationMs": 0, "brandOutroDurationMs": 0,
                "chapters": [{"title": "Owned First", "ordinal": 1, "startOffsetMs": 0, "lengthMs": 1000},
                             {"title": "Owned Second", "ordinal": 2, "startOffsetMs": 1000, "lengthMs": 1000}]}
    fixture.api(f"localrecording/{book['id']}", "PUT", editable(recording,
        title="Owned Local Author Recording", narrators=["Owned Narrator B", "Owned Narrator A"], chapterMetadata=chapters))
    recording = fixture.api(f"localrecording/{book['id']}")
    fixture.check("new_local_chapters_are_editable_and_ordered", recording["chapterMetadata"] == chapters and recording["provenance"]["chapters"] == "manual")
    return saved, recording, book, author


def verify_retained(fixture, saved, recording, pack, prefix):
    local = fixture.api(f"localrecording/{saved['bookId']}")
    new = fixture.api(f"localrecording/{recording['bookId']}")
    coverage = fixture.api(f"recordingwork/{PACK}")
    fields = ("recordingId", "editionId", "fileIds", "title", "subtitle", "narrators", "language", "publisher", "year", "chapterMetadata", "provenance", "revision")
    fixture.check(prefix + "local_metadata_identity_and_provenance_preserved", all(local.get(field) == saved.get(field) and new.get(field) == recording.get(field) for field in fields))
    fixture.check(prefix + "pack_membership_and_coverage_preserved", coverage == pack)
    fixture.check(prefix + "owned_media_bytes_unchanged", restore.media_unchanged(fixture))


def run(runtime, output, baseline_runtime, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise common.ValidationFailure("recording_lifecycle_requires_older_package")
    common.private_directory(output)
    playwright_module = Path(playwright_module).resolve(strict=True)
    fixture = app.Fixture(baseline_runtime, output / "application")
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        identity.seed(fixture, identity.recipe())
        folder, browser_folder = seed_unmapped_audio(fixture)
        baseline = identity.snapshot(fixture)
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        fixture.check("upgrade_preserves_every_existing_identity_and_setting", identity.snapshot(fixture, baseline) == baseline and restore.media_unchanged(fixture))
        fixture.check("owned_fixture_has_no_clients_or_indexers", not fixture.api("downloadclient") and not fixture.api("indexer"))
        stage = "pack_membership"
        pack = inspect_pack(fixture)
        stage = "local_recordings"
        saved, recording, book, author = inspect_local(fixture, folder)
        stage = "refresh_and_rescan"
        for command in ({"name": "RefreshBook", "bookId": recording["bookId"]},
                        {"name": "RefreshAuthor", "authorId": recording["authorId"]},
                        {"name": "RescanFolders", "folders": [str(folder)], "filter": "known", "addNewAuthors": False, "authorIds": [recording["authorId"]]}):
            restore.wait_command(fixture, fixture.api("command", "POST", command)["id"])
        verify_retained(fixture, saved, recording, pack, "rescan_")
        stage = "browser"
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")), "playwrightModule": str(playwright_module),
                  "local": {"id": recording["bookId"], "slug": book["titleSlug"], "title": recording["title"]},
                  "pack": {"editionId": PACK, "slug": "synthetic-identity-pack"},
                  "create": {"fileId": BROWSER_FILE, "author": BROWSER_AUTHOR, "path": str(browser_folder), "title": "Owned Browser Audiobook"}}
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("recording_lifecycle_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        browser_recording = fixture.api(f"localrecording/{recording['bookId']}")
        fixture.check("browser_edits_only_reviewed_metadata", browser_recording["subtitle"] == "Owned phone browser correction" and
                      browser_recording["recordingId"] == recording["recordingId"] and browser_recording["fileIds"] == recording["fileIds"] and
                      browser_recording["chapterMetadata"] == recording["chapterMetadata"] and browser_recording["narrators"] == recording["narrators"])
        recording = browser_recording
        fixture.check("browser_coverage_correction_preserves_exact_final_memberships", fixture.api(f"recordingwork/{PACK}") == pack)
        browser_books = [item for item in fixture.api("book") if item["title"] == config["create"]["title"]]
        fixture.check("browser_created_one_local_recording", len(browser_books) == 1)
        browser_created = fixture.api(f"localrecording/{browser_books[0]['id']}")
        fixture.check("browser_created_local_author_and_exact_file_association", browser_created["recordingId"].startswith("local:recording:") and
                      browser_created["fileIds"] == [BROWSER_FILE] and
                      fixture.api(f"author/{browser_created['authorId']}")["foreignAuthorId"].startswith("local:author:"))
        golden = identity.snapshot(fixture)
        stage = "restart"
        fixture.stop()
        fixture.start()
        verify_retained(fixture, saved, recording, pack, "restart_")
        fixture.check("restart_preserves_full_updated_library_projection", identity.snapshot(fixture, golden) == golden)
        stage = "backup_restore"
        _, backup = restore.create_original_backup(fixture)
        fixture.api(f"localrecording/{recording['bookId']}", "PUT", editable(recording, title="Owned Rollback Title"))
        update_work(fixture, PACK, [])
        fixture.check("backup_restore_mutations_are_observable", fixture.api(f"localrecording/{recording['bookId']}")["title"] == "Owned Rollback Title" and not fixture.api(f"recordingwork/{PACK}")["memberships"])
        status, _, body = restore.upload(fixture, "owned-recording-lifecycle.zip", backup.read_bytes())
        fixture.check("backup_restores_new_metadata_and_memberships", status == 200 and json.loads(body).get("restartRequired") is True)
        fixture.stop()
        fixture.start()
        verify_retained(fixture, saved, recording, pack, "restore_")
        fixture.check("restore_preserves_full_updated_library_projection", identity.snapshot(fixture, golden) == golden)
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
        fixture.checks[stage + "_completed"] = False
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    report = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", report)
    return report
