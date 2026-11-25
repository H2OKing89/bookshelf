"""Hosted profile-rank preservation against a disposable audiobook upgrade."""
from datetime import datetime, timezone
import json
from pathlib import Path
import sqlite3

from . import app, common, identity, library_review, restore, upgrade

BROWSER = Path(__file__).with_name("quality-ranking-browser.cjs")
GROUP = 1000


def item(quality, allowed=False):
    return {"quality": quality, "allowed": allowed, "items": []}


def quality(quality_id):
    return json.dumps({"quality": quality_id, "revision": {"version": 1, "real": 0, "isRepack": False}})


def seed(fixture):
    identity.seed(fixture, identity.recipe())
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as db:
        profile_id = db.execute('SELECT "QualityProfileId" FROM "Authors" WHERE "Id"=11').fetchone()[0]
        items = [item(number) for number in (0, 1, 2, 3, 4)] + [item(13, True), item(11, True),
                 {"id": GROUP, "name": "Owned equal audio formats", "quality": None, "allowed": True, "items": [item(10, True), item(12, True)]}]
        db.execute('UPDATE "QualityProfiles" SET "Items"=?, "Cutoff"=? WHERE "Id"=?', (json.dumps(items), GROUP, profile_id))
        db.execute('UPDATE "BookFiles" SET "Quality"=? WHERE "Id"=1011', (quality(11),))
        db.execute('UPDATE "BookFiles" SET "Quality"=? WHERE "Id"=1012', (quality(10),))
        db.execute('DELETE FROM "History"')
        now = datetime.now(timezone.utc).isoformat()
        for row_id, book_id, quality_id in ((901, 101, 10), (902, 102, 11), (903, 103, 12), (904, 104, 13)):
            restore.insert(db, "History", {"Id": row_id, "AuthorId": 11, "BookId": book_id, "SourceTitle": "Owned rank " + str(row_id),
                           "Quality": quality(quality_id), "EventType": 3, "Date": now, "Data": "{}"})
            restore.insert(db, "Blocklist", {"Id": row_id, "AuthorId": 11, "BookIds": json.dumps([book_id]), "SourceTitle": "Owned rank " + str(row_id),
                           "Quality": quality(quality_id), "Date": now, "Message": "Owned fixture", "Protocol": 1, "Indexer": "Owned fixture"})
    fixture.start()
    return profile_id


def rank_ids(fixture, path, direction):
    result = []
    for page in (1, 2):
        resource = fixture.api(f"{path}?page={page}&pageSize=2&sortKey=quality&sortDirection={direction}")
        if resource["totalRecords"] != 4:
            raise common.ValidationFailure("quality_ranking_pagination_count")
        result += [row["id"] for row in resource["records"]]
    return result


def inspect_ranks(fixture, profile_id):
    fixture.check("history_quality_has_stable_equal_group_order_across_pages", rank_ids(fixture, "history", "descending") == [901, 903, 902, 904])
    fixture.check("blocklist_uses_saved_profile_order", rank_ids(fixture, "blocklist", "ascending") == [904, 902, 901, 903])
    files = fixture.api("bookfile?bookId=101")
    file_rank = {row["id"]: row["qualityRank"] for row in files}
    fixture.check("library_files_use_profile_rank_not_global_quality_weight", file_rank == {1011: 6 / 7, 1012: 1.0})
    first = fixture.api("book/101")["statistics"]
    fixture.check("book_rank_is_lowest_of_tracked_parts", first["fileQualityRank"] == 6 / 7 and first["bookFileCount"] == 2)
    fixture.check("other_recording_stays_separate", fixture.api("book/102")["statistics"]["fileQualityRank"] == 1.0)
    profile = fixture.api(f"qualityprofile/{profile_id}")
    profile["items"].reverse()
    fixture.api(f"qualityprofile/{profile_id}", "PUT", profile)
    fixture.check("profile_save_invalidate_cached_library_and_history_rank", fixture.api("book/101")["statistics"]["fileQualityRank"] == 0 and
                  rank_ids(fixture, "history", "descending") == [904, 902, 901, 903] and rank_ids(fixture, "blocklist", "descending") == [904, 902, 901, 903])
    fixture.check("per_file_rank_refreshes_after_profile_save", {row["id"]: row["qualityRank"] for row in fixture.api("bookfile?bookId=101")} == {1011: 1 / 7, 1012: 0})


def run(runtime, output, baseline_runtime, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(baseline_runtime, output / "application")
    stage, completed, failure = "baseline", False, None
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        profile_id = seed(fixture)
        golden = identity.snapshot(fixture)
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        fixture.check("upgrade_does_not_change_existing_profiles_catalog_or_bytes", identity.snapshot(fixture, golden) == golden and restore.media_unchanged(fixture))
        stage = "rank_checks"
        inspect_ranks(fixture, profile_id)
        fixture.check("rank_sort_requires_authentication", library_review.request(fixture, "history?page=1&sortKey=quality", key=False)[0] == 401)
        stage = "browser"
        configuration = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                         "username": fixture.current_username, "password": fixture.current_password,
                         "playwrightModule": str(Path(playwright_module).resolve(strict=True)), "output": str(common.private_directory(output / "screenshots"))}
        execution = identity._browser_process(configuration, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        fixture.checks.update(report["checks"])
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(report["checks"].values()))
        stage = "edit_and_restart"
        files = {row["id"]: row for row in fixture.api("bookfile?bookId=101")}
        fixture.api("bookfile/1012", "PUT", dict(files[1012], quality=files[1011]["quality"]))
        fixture.check("manual_quality_edit_refreshes_lowest_part_rank", fixture.api("book/101")["statistics"]["fileQualityRank"] == 1 / 7)
        fixture.stop()
        fixture.start()
        fixture.check("profile_order_and_manual_part_rank_survive_restart", fixture.api("book/101")["statistics"]["fileQualityRank"] == 1 / 7 and
                      rank_ids(fixture, "history", "descending") == [904, 902, 901, 903] and restore.media_unchanged(fixture))
        fixture.check("no_real_provider_or_client_configured", not fixture.api("indexer") and not fixture.api("downloadclient"))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
        fixture.checks[stage + "_completed"] = False
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    result = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", result)
    return result
