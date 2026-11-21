"""Verify provider diagnostics and accessible interactions in an owned home."""

from collections import Counter
from datetime import datetime, timezone
import hashlib
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
from pathlib import Path
import sqlite3
import subprocess
from threading import Thread
from urllib.parse import parse_qs, urlencode, urlsplit

from . import app, common, identity, restore, upgrade


BROWSER = Path(__file__).with_name("metadata-accessibility-browser.cjs")
CLIENT_NAME = "Synthetic Disabled Client"
EMPTY_SEARCH = "synthetic empty keyword"
LIMITED_SEARCH = "synthetic rate limited keyword"
MISSING_ASIN = "B000000909"


def metadata_server():
    requests = Counter()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, format, *args):
            pass

        def do_GET(self):
            request = urlsplit(self.path)
            query = parse_qs(request.query)
            status, payload, event = 404, "{}", "unexpected"
            if self.client_address[0] == "127.0.0.1" and query.get("region") == ["us"]:
                if request.path == "/books/search" and query.get("q") == [EMPTY_SEARCH]:
                    status, payload, event = 200, "[]", "empty"
                elif request.path == "/books/search" and query.get("q") == [LIMITED_SEARCH]:
                    status, event = 429, "limited"
                elif request.path == "/books/" + MISSING_ASIN:
                    event = "missing"
            requests[event] += 1
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            if status == 429:
                self.send_header("Retry-After", "60")
            self.end_headers()
            self.wfile.write(payload.encode("utf-8"))

    server = HTTPServer(("127.0.0.1", 0), Handler)
    thread = Thread(target=server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
    thread.start()
    return server, thread, requests


def seed_unmapped_file(fixture):
    folder = common.private_directory(fixture.home / "owned-media" / "Unmapped")
    file = folder / "synthetic-unmapped.m4b"
    # Generate a real audio file so the local-recording preview reads its tags.
    subprocess.run([
        "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
        "-metadata", "title=Synthetic Unmapped Recording", "-metadata", "artist=Synthetic Author",
        "-c:a", "aac", str(file),
    ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
    data = file.read_bytes()
    fixture.media_hashes[file] = hashlib.sha256(data).hexdigest()
    now = datetime.now(timezone.utc).isoformat()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        restore.insert(database, "BookFiles", {
            "Id": 9101, "EditionId": 0, "Quality": '{"quality":12,"revision":{"version":1}}',
            "Part": 1, "CalibreId": 0, "Path": str(file), "DateAdded": now,
            "Modified": now, "Size": len(data), "IndexerFlags": 0,
        })


def inspect_status(fixture, requests, provider_url):
    route = "metadataproviderstatus"
    fixture.check("status_requires_authentication", fixture.request(fixture.URL_BASE + "/api/v1/" + route)[0] == 401)
    fixture.check("before_any_request_status_is_empty", fixture.api(route) == [])

    def lookup(term):
        path = fixture.URL_BASE + "/api/v1/book/lookup?" + urlencode({"term": term})
        return fixture.request(path, key=True)[0]

    def state(operation):
        return next((row for row in fixture.api(route) if row["provider"] == "audnexus" and row["operation"] == operation), {})

    fixture.check("valid_empty_keyword_response_succeeds", lookup(EMPTY_SEARCH) == 200)
    initial = state("bookSearch")
    fixture.check("valid_empty_response_is_recorded_as_complete", initial.get("state") == "complete" and initial.get("lastCompleteAtUtc"))
    fixture.check("repeat_search_uses_cached_response", lookup(EMPTY_SEARCH) == 200 and requests["empty"] == 1)
    cached = state("bookSearch")
    fixture.check("cached_response_preserves_actual_success_time",
                  cached.get("state") == "cached" and cached.get("lastCompleteAtUtc") == initial.get("lastCompleteAtUtc"))
    lookup("audible:us:" + MISSING_ASIN)
    missing = state("recordingDetails")
    fixture.check("confirmed_missing_has_a_distinct_reason",
                  missing.get("state") == "missing" and missing.get("reason") == "confirmedMissing")
    lookup(LIMITED_SEARCH)
    limited = state("bookSearch")
    fixture.check("http_429_exposes_rate_limit_and_retry_time",
                  limited.get("state") == "rateLimited" and limited.get("reason") == "tooManyRequests"
                  and limited.get("retryAfterUtc") and limited.get("lastCompleteAtUtc") == initial.get("lastCompleteAtUtc"))
    report = fixture.api(route)
    safe_keys = {"provider", "operation", "state", "reason", "observedAtUtc", "lastCompleteAtUtc", "retryAfterUtc"}
    fixture.check("diagnostics_omit_query_identifiers_and_provider_url",
                  all(set(row) <= safe_keys for row in report)
                  and not any(value in json.dumps(report) for value in (EMPTY_SEARCH, LIMITED_SEARCH, MISSING_ASIN, provider_url)))
    fixture.check("diagnostics_read_does_not_fetch_metadata", requests == {"empty": 1, "missing": 1, "limited": 1})


def run(runtime, output, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    playwright_module = Path(playwright_module).resolve(strict=True)
    server, thread, requests = metadata_server()
    provider_url = "http://127.0.0.1:" + str(server.server_address[1])
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        settings = fixture.api("config/metadataprovider/1")
        settings.update(audnexusUrl=provider_url, audnexusRegion="us", audibleRsEnabled=False)
        fixture.api("config/metadataprovider/1", "PUT", settings)
        saved = fixture.api("config/metadataprovider/1")
        fixture.check("only_owned_metadata_endpoint_configured",
                      saved["audnexusUrl"] == provider_url and saved["audibleRsEnabled"] is False)
        upgrade.seed_current_account(fixture)
        identity.seed(fixture, identity.recipe())
        fixture.stop()
        seed_unmapped_file(fixture)
        fixture.start()
        stage = "provider_outcomes"
        inspect_status(fixture, requests, provider_url)
        stage = "disabled_download_client"
        schema = fixture.api("downloadclient/schema")
        client = next(item for item in schema if item["implementation"].lower() == "qbittorrent")
        client.update(name=CLIENT_NAME, enable=False)
        fixture.api("downloadclient?forceSave=true", "POST", client)
        stage = "browser"
        screenshots = common.private_directory(output / "screenshots")
        process = common.private_directory(output / "browser-process")
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(screenshots), "playwrightModule": str(playwright_module),
                  "downloadClientName": CLIENT_NAME}
        execution = identity._browser_process(config, process, BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("accessibility_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        fixture.check("browser_preserves_owned_files", restore.media_unchanged(fixture))
        fixture.check("browser_preserves_disabled_client", any(value["name"] == CLIENT_NAME and value["enable"] is False
                                                               for value in fixture.api("downloadclient")))
        fixture.check("browser_preserves_monitoring", fixture.api("book/101")["monitored"] is False)
        fixture.check("browser_does_not_add_provider_requests", requests == {"empty": 1, "missing": 1, "limited": 1})
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
