"""Exercise opt-in diagnostics in a disposable library and owned browser."""
from __future__ import annotations

import json
from pathlib import Path

from . import app, common, identity, library_review, library_virtualization, upgrade

BROWSER = Path(__file__).with_name("performance-profiling-browser.cjs")
ROUTE = "system/performance"
FIELDS = {"operation", "method", "requests", "failed", "cancelled", "meanMilliseconds", "maxMilliseconds",
          "sqliteStatements", "sqliteMilliseconds", "postgresOperations", "postgresMilliseconds"}


def run(runtime, output, playwright_module=None):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    completed, stage, failure = False, "default", None
    try:
        fixture.configure_home()
        fixture.start()
        default = fixture.api(ROUTE)
        fixture.check("default_inactive_without_samples", default == {"enabled": False, "retentionMinutes": 10,
                      "capacity": 256, "samples": 0, "operations": []})
        fixture.check("anonymous_diagnostics_denied_even_before_login_setup",
                      library_review.request(fixture, ROUTE, key=False)[0] == 401)
        upgrade.seed_current_account(fixture)
        expected_catalog = library_virtualization.seed_catalog(fixture)
        stage = "enable"
        settings = fixture.api()
        fixture.check("profiling_host_setting_defaults_false", settings["performanceProfilingEnabled"] is False)
        fixture.api("config/host/1", "PUT", dict(settings, performanceProfilingEnabled=True))
        fixture.check("setting_saved_but_requires_restart", fixture.api()["performanceProfilingEnabled"] is True and
                      fixture.api(ROUTE)["enabled"] is False)
        fixture.stop()
        fixture.start()
        stage = "capture"
        fixture.check("unauthenticated_and_wrong_key_requests_denied", library_review.request(fixture, ROUTE, key=False)[0] == 401 and
                      fixture.request(fixture.URL_BASE + "/api/v1/" + ROUTE, headers={"X-Api-Key": "owned-wrong"})[0] == 401)
        private_marker = "owned-private-query-marker"
        books = fixture.api("book?validationMarker=" + private_marker)
        book_ids = {book["id"] for book in books}
        fixture.check("large_seeded_library_is_available_while_profiling", len(book_ids) == expected_catalog["books"] and len(book_ids) >= 4160)
        fixture.api("bookfile?bookId=" + str(books[0]["id"]))
        fixture.api("librarystatistics")
        status, headers, body = fixture.request(fixture.URL_BASE + "/api/v1/" + ROUTE, key=True)
        sample = json.loads(body)
        fixture.check("authenticated_read_is_not_cacheable", status == 200 and
                      "no-store" in dict((key.lower(), value.lower()) for key, value in headers).get("cache-control", ""))
        groups = sample["operations"]
        fixture.check("real_request_and_sql_timings", sample["enabled"] and sample["samples"] > 0 and
                      any(group["operation"].startswith("Book.") and group["sqliteStatements"] > 0 for group in groups) and
                      all(group["meanMilliseconds"] >= 0 and group["maxMilliseconds"] >= group["meanMilliseconds"] for group in groups))
        fixture.check("snapshot_is_aggregate_only", set(sample) == set(default) and
                      all(set(group) == FIELDS for group in groups) and
                      sum(group["requests"] for group in groups) == sample["samples"] and
                      all(marker not in body.decode() for marker in (private_marker, fixture.api_key, str(fixture.home), books[0]["title"])))
        fixture.check("viewing_diagnostics_does_not_profile_itself", fixture.api(ROUTE) == sample)
        if playwright_module:
            stage = "browser"
            config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                      "username": fixture.current_username, "password": fixture.current_password,
                      "output": str(common.private_directory(output / "screenshots")),
                      "playwrightModule": str(Path(playwright_module).resolve(strict=True))}
            execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
            report = json.loads(execution.stdout)
            checks = report.get("checks")
            if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
                raise common.ValidationFailure("profiling_browser_report_invalid")
            fixture.checks.update(checks)
            fixture.check("profiling_browser_completed", execution.returncode == 0 and report.get("completed") is True)
        stage = "disable"
        fixture.api("config/host/1", "PUT", dict(fixture.api(), performanceProfilingEnabled=False))
        fixture.stop()
        fixture.start()
        fixture.check("disabling_clears_all_prior_samples", fixture.api(ROUTE) == default)
        fixture.check("synthetic_library_catalog_unchanged", {book["id"] for book in fixture.api("book")} == book_ids)
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_processes_stopped"] = fixture.process is None and fixture.forced_stops == 0
    report = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", report)
    return report
