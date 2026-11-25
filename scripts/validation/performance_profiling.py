"""Exercise opt-in diagnostics in a disposable library and owned browser."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import http.server
import json
import math
import os
from pathlib import Path
import sqlite3
import statistics
import subprocess
import threading
import time

from . import app, common, identity, library_review, library_virtualization, restore, upgrade

BROWSER = Path(__file__).with_name("performance-profiling-browser.cjs")
ROUTE = "system/performance"
FIELDS = {"operation", "method", "requests", "failed", "cancelled", "meanMilliseconds", "maxMilliseconds",
          "sqliteStatements", "sqliteMilliseconds", "postgresOperations", "postgresMilliseconds"}
JOB_FIELDS = FIELDS - {"method", "requests"} | {"category", "trigger", "executions"}
SCHEDULED_JOBS = {"RefreshAuthor": "authorRefresh", "RescanFolders": "scan",
                  "RefreshStaleMediaInfo": "mediaInfo", "RefreshMonitoredDownloads": "downloadMonitor"}


class OwnedRejectingProxy:
    """Reject HTTP/S near the fixture without storing targets or forwarding.

    The app's startup health check also uses the default HTTP transport. This
    owned proxy prevents it from reaching the former upstream cloud service.
    It does not claim complete OS-level network isolation.
    """

    def __init__(self):
        self.rejections = 0
        self._gate = threading.Lock()
        owner = self

        class Reject(http.server.BaseHTTPRequestHandler):
            def reject(self):
                with owner._gate:
                    owner.rejections += 1
                self.send_response(502)
                self.send_header("Content-Length", "0")
                self.end_headers()

            do_CONNECT = reject
            do_GET = reject
            do_POST = reject
            do_HEAD = reject
            do_PUT = reject
            do_DELETE = reject

            def log_message(self, *args):
                pass

        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Reject)
        self.port = self.server.server_port
        self._thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self._thread.start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self._thread.join(timeout=5)
        return not self._thread.is_alive()


def generate_owned_audio(path: Path, title: str, artist: str) -> None:
    subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "aac",
                    "-metadata", "title=" + title, "-metadata", "artist=" + artist,
                    "-metadata", "album=" + title, "-metadata", "track=1", str(path)],
                   check=True, timeout=15, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def prepare_job_media(fixture):
    fixture.stop()
    download_folder = common.private_directory(fixture.home / "owned-downloads")
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        existing, original = (Path(value) for value in database.execute('SELECT "Path","OriginalFilePath" FROM "BookFiles" WHERE "Id"=19').fetchone())
        # Existing fixture source bytes are synthetic placeholders. Only this
        # owned fixture introduces a valid known recording for actual MediaInfo
        # analysis; the original untracked placeholder is moved outside roots
        # so the scheduled scan never submits unidentified media to a provider.
        generate_owned_audio(existing, "Synthetic Recording", "Synthetic Author")
        timestamp = int(time.time()) - 60
        os.utime(existing, (timestamp, timestamp))
        moved = download_folder / "original-placeholder.m4b"
        original.rename(moved)
        fixture.media_hashes.pop(original, None)
        fixture.media_hashes[moved] = hashlib.sha256(moved.read_bytes()).hexdigest()
        fixture.media_hashes[existing] = hashlib.sha256(existing.read_bytes()).hexdigest()
        # SQLite's provider persists DateTime as a space-separated UTC value
        # with a Z suffix; an arbitrary ISO text offset is not byte-equal to
        # its optimistic Modified predicate even when it denotes the same
        # instant. Seed the actual provider representation at a whole second.
        modified = datetime.fromtimestamp(timestamp, timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ")
        database.execute('UPDATE "BookFiles" SET "Size"=?,"Modified"=?,"MediaInfo"=NULL,"OriginalFilePath"=? WHERE "Id"=19',
                         (existing.stat().st_size, modified, str(moved)))
        database.execute('INSERT OR REPLACE INTO "Config" ("Key","Value") VALUES (?,?)', ("rescanafterrefresh", "Never"))
        # Prevent all other scheduled tasks in the disposable home; selected
        # jobs will be made due after enabling, through the genuine scheduler.
        database.execute('UPDATE "ScheduledTasks" SET "LastExecution"=?', ("2099-01-01T00:00:00Z",))
        author_id, author_path, author_name, book_id, title, edition = database.execute('SELECT a."Id",a."Path",m."Name",b."Id",b."Title",e."ForeignEditionId" FROM "Books" b JOIN "Editions" e ON e."BookId"=b."Id" JOIN "AuthorMetadata" m ON m."Id"=b."AuthorMetadataId" JOIN "Authors" a ON a."AuthorMetadataId"=m."Id" WHERE b."Id"=10002').fetchone()
    Path(author_path).mkdir(exist_ok=True)
    source = download_folder / "owned-private-import.m4b"
    generate_owned_audio(source, title, author_name)
    fixture.media_hashes[source] = hashlib.sha256(source.read_bytes()).hexdigest()
    empty_folder = common.private_directory(download_folder / "empty-import")
    fixture.start()
    return {"name": "ManualImport", "importMode": "copy", "replaceExistingFiles": False,
            "files": [{"path": str(source), "authorId": author_id, "bookId": book_id, "foreignEditionId": edition,
                       "quality": {"quality": {"id": 12, "name": "M4B"}, "revision": {"version": 1, "real": 0, "isRepack": False}},
                       "disableReleaseSwitching": True}]}, empty_folder, book_id


def make_jobs_due(fixture) -> None:
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        results = database.execute('SELECT "Id","TypeName" FROM "ScheduledTasks"').fetchall()
        selected = [(identifier, typename.rsplit(".", 1)[-1].removesuffix("Command")) for identifier, typename in results]
        fixture.check("scheduling_targets_real_owned_scheduler_tasks", set(SCHEDULED_JOBS) <= {operation for _, operation in selected})
        # An old LastExecution avoids the changed-author provider path, while
        # every owned author is local. No live metadata/provider is consulted.
        for identifier, operation in selected:
            if operation in SCHEDULED_JOBS:
                database.execute('UPDATE "ScheduledTasks" SET "LastExecution"=?,"LastStartTime"=? WHERE "Id"=?',
                                 ("2000-01-01T00:00:00Z", "2000-01-01T00:00:00Z", identifier))


def job_groups(fixture, expected, timeout=75, minimum_executions=None):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        sample = fixture.api(ROUTE)
        groups = {(job["operation"], job["trigger"]): job for job in sample["jobs"]}
        if all(key in groups for key in expected) and all(groups[key]["executions"] >= minimum for key, minimum in (minimum_executions or {}).items()):
            return groups
        time.sleep(0.5)
    raise common.ValidationFailure("owned_background_jobs_not_profiled")


def command_duration_milliseconds(value: str) -> float:
    if not isinstance(value, str) or value.startswith("-"):
        raise common.ValidationFailure("invalid_owned_command_duration")
    hours, minutes, seconds = value.split(":")
    days = 0
    if "." in hours:
        day_text, hours = hours.split(".", 1)
        days = int(day_text)
    result = ((days * 24 + int(hours)) * 3600 + int(minutes) * 60 + float(seconds)) * 1000
    if not math.isfinite(result) or result < 0:
        raise common.ValidationFailure("invalid_owned_command_duration")
    return result


def measure_local_refresh(fixture):
    # A warm-up, then seven identical read-only local-author commands. Measure
    # the actual worker's recorded execution duration, not API polling latency.
    # Report the range and median; do not impose a flaky speed threshold.
    values = []
    for index in range(8):
        identifier = fixture.api("command", "POST", {"name": "RefreshAuthor", "authorId": 11})["id"]
        restore.wait_command(fixture, identifier)
        command = fixture.api("command/" + str(identifier))
        if command["status"] != "completed" or command["result"] != "successful":
            raise common.ValidationFailure("owned_local_refresh_workload_changed_outcome")
        if index:
            values.append(command_duration_milliseconds(command["duration"]))
    return {"iterations": len(values), "minimumMilliseconds": min(values), "medianMilliseconds": statistics.median(values),
            "maximumMilliseconds": max(values)}


def run(runtime, output, playwright_module=None):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    proxy = OwnedRejectingProxy()
    try:
        fixture = app.Fixture(runtime, output / "application", owned_http_proxy_port=proxy.port)
    except BaseException:
        proxy.close()
        raise
    completed, stage, failure = False, "default", None
    measurements = {}
    try:
        fixture.configure_home()
        fixture.start()
        default = fixture.api(ROUTE)
        fixture.check("default_inactive_without_samples", default == {"enabled": False, "retentionMinutes": 10,
                      "capacity": 256, "samples": 0, "operations": [], "jobSamples": 0, "jobs": []})
        fixture.check("anonymous_diagnostics_denied_even_before_login_setup",
                      library_review.request(fixture, ROUTE, key=False)[0] == 401)
        upgrade.seed_current_account(fixture)
        expected_catalog = library_virtualization.seed_catalog(fixture)
        manual_import, empty_import, import_book_id = prepare_job_media(fixture)
        fixture.check("owned_scheduler_has_no_providers_clients_or_indexers", not fixture.api("downloadclient") and not fixture.api("indexer") and not fixture.api("importlist"))
        measurements["disabled"] = measure_local_refresh(fixture)
        fixture.check("disabled_worker_execution_does_not_collect_samples", fixture.api(ROUTE) == default)
        stage = "enable"
        settings = fixture.api()
        fixture.check("profiling_host_setting_defaults_false", settings["performanceProfilingEnabled"] is False)
        fixture.api("config/host/1", "PUT", dict(settings, performanceProfilingEnabled=True))
        fixture.check("setting_saved_but_requires_restart", fixture.api()["performanceProfilingEnabled"] is True and
                      fixture.api(ROUTE)["enabled"] is False)
        fixture.stop()
        fixture.start()
        stage = "measurement"
        measurements["enabled"] = measure_local_refresh(fixture)
        fixture.check("paired_local_refresh_workload_has_same_successful_outcomes", measurements["disabled"]["iterations"] == measurements["enabled"]["iterations"] == 7)
        measured = job_groups(fixture, {("RefreshAuthor", "manual")}, timeout=5,
                              minimum_executions={("RefreshAuthor", "manual"): 8})[("RefreshAuthor", "manual")]
        fixture.check("repeated_local_refresh_is_sampled_once_per_job", measured["executions"] == 8 and measured["failed"] == 0)
        # Separate the paired local-only timings from the actual due scheduler
        # and large-catalogue capture. Both measured states have these tasks
        # postponed; the next host's diagnostic buffer starts empty.
        fixture.stop()
        make_jobs_due(fixture)
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
        stage = "scheduled_jobs"
        restore.wait_command(fixture, fixture.api("command", "POST", {"name": "RefreshAuthor", "authorId": 11})["id"])
        expected_jobs = {(operation, "scheduled"): category for operation, category in SCHEDULED_JOBS.items()}
        expected_jobs.update({("RefreshAuthor", "manual"): "authorRefresh", ("ProcessMonitoredDownloads", "unspecified"): "import"})
        jobs = job_groups(fixture, expected_jobs)
        fixture.check("genuine_scheduled_refresh_and_scan_completed_without_failure", all(jobs[(operation, "scheduled")]["failed"] == 0 for operation in SCHEDULED_JOBS))
        analyzed = fixture.api("bookfile/19").get("mediaInfo") or {}
        fixture.check("scheduled_media_info_reads_real_packaged_analyzer", analyzed.get("source") == "MediaInfo" and analyzed.get("analysisStatus") == "current" and
                      (analyzed.get("analysisRevision") or 0) > 0 and analyzed.get("audioFormat") == "AAC")
        stage = "import_jobs"
        # Actual user-queued import and an unsurprising unsuccessful import.
        restore.wait_command(fixture, fixture.api("command", "POST", manual_import)["id"])
        new_files = fixture.api("bookfile?bookId=" + str(import_book_id))
        fixture.check("manual_copy_import_creates_real_owned_recording", len(new_files) == 1 and
                      Path(new_files[0]["path"]).is_relative_to(fixture.home) and Path(new_files[0]["path"]).is_file() and restore.media_unchanged(fixture))
        restore.wait_command(fixture, fixture.api("command", "POST", {"name": "DownloadedBooksScan", "path": str(empty_import), "importMode": "copy"})["id"])
        expected_jobs.update({("ManualImport", "manual"): "import", ("DownloadedBooksScan", "manual"): "import"})
        jobs = job_groups(fixture, expected_jobs)
        fixture.check("real_independent_scheduler_and_import_worker_categories", all(jobs[key]["category"] == category and jobs[key]["sqliteStatements"] > 0
                      and jobs[key]["meanMilliseconds"] >= 0 for key, category in expected_jobs.items()))
        fixture.check("unsuccessful_import_is_counted_without_false_cancellation", jobs[("DownloadedBooksScan", "manual")]["failed"] == 1 and jobs[("DownloadedBooksScan", "manual")]["cancelled"] == 0)
        stage = "aggregate"
        status, headers, body = fixture.request(fixture.URL_BASE + "/api/v1/" + ROUTE, key=True)
        sample = json.loads(body)
        fixture.check("authenticated_read_is_not_cacheable", status == 200 and
                      "no-store" in dict((key.lower(), value.lower()) for key, value in headers).get("cache-control", ""))
        groups = sample["operations"]
        fixture.check("real_request_and_sql_timings", sample["enabled"] and sample["samples"] > 0 and
                      any(group["operation"].startswith("Book.") and group["sqliteStatements"] > 0 for group in groups) and
                      all(group["meanMilliseconds"] >= 0 and group["maxMilliseconds"] >= group["meanMilliseconds"] for group in groups))
        fixture.check("snapshot_is_aggregate_only", set(sample) == set(default) and
                      all(set(group) == FIELDS for group in groups) and all(set(job) == JOB_FIELDS for job in sample["jobs"]) and
                      sum(group["requests"] for group in groups) == sample["samples"] and
                      sum(job["executions"] for job in sample["jobs"]) == sample["jobSamples"] and
                      sample["samples"] + sample["jobSamples"] <= sample["capacity"] and
                      all(marker not in body.decode() for marker in (private_marker, fixture.api_key, str(fixture.home), books[0]["title"], "owned-private-import.m4b")))
        # Independent scheduled samples may arrive while fetching diagnostics;
        # specifically verify the request collection remains unchanged.
        fixture.check("viewing_diagnostics_does_not_profile_itself", fixture.api(ROUTE)["operations"] == sample["operations"])
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
        fixture.check("startup_http_probe_rejected_by_owned_nonforwarding_proxy", proxy.rejections > 0)
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = app.safe_failure(stage, error)
        if isinstance(error, common.ValidationFailure):
            failure["code"] = error.code
    finally:
        try:
            fixture.cleanup()
            fixture.checks["owned_processes_stopped"] = fixture.process is None and fixture.forced_stops == 0
        finally:
            fixture.checks["owned_http_proxy_stopped"] = proxy.close()
    report = app.result(fixture, completed, failure=failure,
                        measurements={"localRefreshCommandDuration": measurements, "note": "Synthetic worker duration; no performance pass threshold"},
                        httpRouting="owned loopback rejection proxy; not OS-level isolation")
    common.write_json(output / "result.json", report)
    return report
