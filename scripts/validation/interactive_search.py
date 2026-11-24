"""Verify the interactive search UI with synthetic releases and real library routes."""

import json
from pathlib import Path

from . import app, common, identity, restore, upgrade


BROWSER = Path(__file__).with_name("interactive-search-browser.cjs")


def run(runtime, output, playwright_module):
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
        fixture.check("no_download_clients_or_indexers_configured", not fixture.api("downloadclient") and not fixture.api("indexer"))
        fixture.check("real_search_without_indexers_is_empty", fixture.api("release?bookId=104") == [])
        author = fixture.api("author/11")

        def recording(book_id):
            case = next(item for item in contract["cases"] if item["id"] == book_id)
            return {"id": book_id, "title": case["title"], "slug": "synthetic-identity-" + case["key"]}

        stage = "browser"
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")),
                  "playwrightModule": str(playwright_module), "first": recording(104), "second": recording(105),
                  "author": {"id": 11, "name": author["authorName"], "slug": author["titleSlug"]}}
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("search_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        fixture.check("owned_queue_has_no_downloads", fixture.api("queue/status")["totalCount"] == 0)
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
