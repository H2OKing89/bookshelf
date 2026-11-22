"""Verify Wanted and Queue in a disposable library with no configured clients."""

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sqlite3

from . import app, common, identity, library_review, restore, upgrade


WANTED_BROWSER = Path(__file__).with_name("wanted-paging-browser.cjs")
QUEUE_BROWSER = Path(__file__).with_name("queue-paging-browser.cjs")
CUTOFF_BOOKS = (104, 105)
IMPORT_BOOK = 107
IMPORT_FILE = library_review.REVIEW_FILE
UNMONITOR_BOOK = 101
MISSED_MONITOR_BOOK = 108


def seed_wanted(fixture, contract):
    fixture.stop()
    date = datetime(2020, 1, 1, tzinfo=timezone.utc)
    quality = json.dumps({"quality": 10, "revision": {"version": 1, "real": 0, "isRepack": False}})
    file = library_review.generate_file(fixture, "Owned missing recording",
                                        "Synthetic Side Story [B077777777].m4b", "Synthetic Side Story")
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.execute('UPDATE "QualityProfiles" SET "Cutoff" = 12, "UpgradeAllowed" = 1 '
                         'WHERE "Id" = (SELECT "QualityProfileId" FROM "Authors" WHERE "Id" = 11)')
        database.execute('UPDATE "Authors" SET "Monitored" = 1 WHERE "Id" = 11')
        for case in contract["cases"]:
            book_id = case["id"]
            release_date = (date + timedelta(days=book_id)).strftime("%Y-%m-%d %H:%M:%SZ")
            database.execute('UPDATE "Books" SET "Monitored" = 1, "ReleaseDate" = ? WHERE "Id" = ?',
                             (release_date, book_id))
            database.execute('UPDATE "Editions" SET "ReleaseDate" = ?, "ReleaseDateIsPlaceholder" = 0 WHERE "BookId" = ?',
                             (release_date, book_id))
            if book_id in CUTOFF_BOOKS:
                database.execute('UPDATE "BookFiles" SET "Quality" = ? WHERE "EditionId" = ?', (quality, book_id + 1000))
            else:
                database.execute('DELETE FROM "BookFiles" WHERE "EditionId" = ?', (book_id + 1000,))
        library_review.add_unmapped(database, IMPORT_FILE, file)
    fixture.start()


def run(runtime, output, playwright_module, *, browser):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    playwright_module = Path(playwright_module).resolve(strict=True)
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        contract = identity.recipe()
        identity.seed(fixture, contract)
        fixture.check("fixture_has_no_configured_download_clients_or_indexers", not fixture.api("downloadclient")
                      and not fixture.api("indexer"))
        queue = fixture.api("queue?page=1&pageSize=20&sortKey=timeleft&sortDirection=ascending&includeUnknownAuthorItems=true")
        fixture.check("owned_backend_queue_starts_empty", queue.get("totalRecords") == 0 and queue.get("records") == [])
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")),
                  "playwrightModule": str(playwright_module),
                  "titles": {str(case["id"]): case["title"] for case in contract["cases"]}}
        if browser == WANTED_BROWSER:
            stage = "seed_wanted"
            seed_wanted(fixture, contract)
            missing = fixture.api("wanted/missing?page=1&pageSize=5&sortKey=releaseDate&sortDirection=descending&monitored=true")
            cutoff = fixture.api("wanted/cutoff?page=1&pageSize=20&sortKey=releaseDate&sortDirection=descending&monitored=true")
            fixture.check("owned_missing_starts_with_two_ordered_pages", missing.get("totalRecords") == 6 and
                          [record["id"] for record in missing.get("records", [])] == [108, 107, 106, 103, 102])
            fixture.check("owned_cutoff_matches_only_the_two_lower_quality_recordings", cutoff.get("totalRecords") == 2 and
                          {record["id"] for record in cutoff.get("records", [])} == set(CUTOFF_BOOKS))
            config.update(cutoffBooks=list(CUTOFF_BOOKS), importBook=IMPORT_BOOK, importFile=IMPORT_FILE,
                          unmonitorBook=UNMONITOR_BOOK, missedMonitorBook=MISSED_MONITOR_BOOK)
        stage = "browser"
        process = common.private_directory(output / "browser-process")
        execution = identity._browser_process(config, process, browser)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if (not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values())):
            raise common.ValidationFailure("operational_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("operational_browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        if browser == WANTED_BROWSER:
            files = fixture.api(f"bookfile?bookId={IMPORT_BOOK}")
            fixture.check("browser_associated_only_the_reviewed_owned_recording", [file["id"] for file in files] == [IMPORT_FILE])
            fixture.check("browser_monitoring_changes_use_only_the_owned_targets",
                          fixture.api(f"book/{UNMONITOR_BOOK}")["monitored"] is False and
                          fixture.api(f"book/{MISSED_MONITOR_BOOK}")["monitored"] is False)
        else:
            fixture.check("no_download_operation_reached_the_owned_backend", fixture.api("queue/status")["totalCount"] == 0)
        fixture.check("owned_media_unchanged", restore.media_unchanged(fixture))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    value = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", value)
    return value


def run_wanted(runtime, output, playwright_module):
    return run(runtime, output, playwright_module, browser=WANTED_BROWSER)


def run_queue(runtime, output, playwright_module):
    return run(runtime, output, playwright_module, browser=QUEUE_BROWSER)
