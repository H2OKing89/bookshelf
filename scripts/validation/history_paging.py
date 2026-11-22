"""Exercise the Activity History browser against an owned SQLite package."""

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sqlite3

from . import app, common, identity, restore, upgrade


BROWSER = Path(__file__).with_name("history-paging-browser.cjs")
SYNC_BROWSER = Path(__file__).with_name("history-sync-browser.cjs")
FIRST_GRAB = 125
SECOND_GRAB = 104
TOTAL = 25


def seed_history(fixture):
    identity.seed(fixture, identity.recipe())
    fixture.stop()
    date = datetime.now(timezone.utc) - timedelta(minutes=TOTAL + 1)
    quality = json.dumps({"quality": 12, "revision": {"version": 1, "real": 0, "isRepack": False}})
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.execute('DELETE FROM "History"')
        for sequence in range(1, TOTAL + 1):
            history_id = 100 + sequence
            restore.insert(database, "History", {
                "Id": history_id, "AuthorId": 11, "BookId": 104 if sequence > 5 else 105,
                "SourceTitle": "Owned history row " + str(history_id),
                "Date": (date + timedelta(minutes=sequence)).isoformat(),
                "Quality": quality, "Data": "{}", "DownloadId": None,
                "EventType": 1 if history_id in (FIRST_GRAB, SECOND_GRAB) else 3,
            })
    fixture.start()


def run(runtime, output, playwright_module, *, browser=BROWSER, expected_failures=0):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    playwright_module = Path(playwright_module).resolve(strict=True)
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        seed_history(fixture)
        fixture.check("fixture_uses_owned_disabled_downloader_and_indexer", not fixture.api("downloadclient")
                      and not fixture.api("indexer"))
        page = fixture.api("history?page=1&pageSize=20&sortKey=date&sortDirection=descending")
        fixture.check("seeded_history_has_two_ordered_pages", page.get("totalRecords") == TOTAL
                      and [record["id"] for record in page.get("records", [])] == list(range(125, 105, -1)))
        stage = "browser"
        screenshots = common.private_directory(output / "screenshots")
        process = common.private_directory(output / "browser-process")
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(screenshots), "playwrightModule": str(playwright_module),
                  "firstGrab": FIRST_GRAB, "secondGrab": SECOND_GRAB}
        execution = identity._browser_process(config, process, browser)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if (not isinstance(checks, dict) or not checks
                or not all(type(value) is bool for value in checks.values())):
            raise common.ValidationFailure("history_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("history_browser_completed", execution.returncode == 0
                      and report.get("completed") is True and all(checks.values()))
        failed_downloads = fixture.api("history?page=1&pageSize=20&eventType=4")
        fixture.check("browser_writes_only_expected_owned_history",
                      failed_downloads.get("totalRecords") == expected_failures)
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


def run_sync(runtime, output, playwright_module):
    return run(runtime, output, playwright_module, browser=SYNC_BROWSER, expected_failures=2)
