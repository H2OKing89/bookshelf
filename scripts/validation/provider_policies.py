"""Hosted Custom JSON and qBittorrent settings checks with owned loopback providers.

No caller-supplied homes, URLs, provider credentials, download jobs or external
metadata services are used. The baseline creates a disabled legacy client and
synthetic audiobook catalog; the candidate exercises the public provider API.
"""

from collections import Counter
from copy import deepcopy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import secrets
from threading import Thread
import time
from urllib.parse import parse_qs, urlsplit

from . import app, common, identity, library_review, restore, upgrade


CUSTOM_LIST_NAME = "Owned Custom JSON List"
QBIT_NAME = "Owned Legacy qBittorrent"
BROWSER = Path(__file__).with_name("provider-policies-browser.cjs")


class OwnedEndpoint:
    """Record methods/routes only; never persist headers, secrets or bodies."""

    def __init__(self):
        self.requests = Counter()
        self.unexpected = 0
        endpoint = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format, *args):
                pass

            def do_GET(self):
                self.serve()

            def do_POST(self):
                self.serve()

            def serve(self):
                route = urlsplit(self.path)
                endpoint.requests[(self.command, route.path)] += 1
                status, content_type, body, headers = endpoint.response(self.command, route, self.headers, self.rfile)
                body = json.dumps(body).encode() if content_type == "application/json" and not isinstance(body, bytes) else body
                if isinstance(body, str):
                    body = body.encode()
                self.send_response(status)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(body)))
                for key, value in headers.items():
                    self.send_header(key, value)
                self.end_headers()
                try:
                    self.wfile.write(body)
                except (BrokenPipeError, ConnectionResetError):
                    # Bounded responses and rejected requests close promptly.
                    pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        self.thread.start()

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server.server_port}"

    @staticmethod
    def json(status, body, headers=None):
        return status, "application/json", body, headers or {}

    def reject(self):
        self.unexpected += 1
        return self.json(404, {"message": "Unexpected owned route"})

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


class CustomEndpoint(OwnedEndpoint):
    def __init__(self, contract):
        self.contract = contract
        self.mode = "success"
        self.token = secrets.token_urlsafe(24)
        self.echo = "owned-custom-error-" + secrets.token_hex(12)
        self.pages = Counter()
        self.auth_failures = 0
        self.cookie_requests = 0
        super().__init__()

    def recording(self, key):
        item = next(item for item in self.contract["cases"] if item["key"] == key)
        return {"kind": "recording", "authorId": self.contract["author"]["id"], "author": self.contract["author"]["name"],
                "title": item["title"], "recordingId": item["recordingId"], "editionId": item["recordingId"]}

    def page(self, page):
        us, uk, local = (self.recording(key) for key in ("us", "uk", "local"))
        # A repeated, exactly identified recording is legal list input. A work
        # row explicitly enumerates that same recording, exercising expansion
        # and deduplication without adding 100 unrelated catalog records.
        work = {"kind": "work", "authorId": us["authorId"], "author": us["author"], "workId": "owned-work", "workTitle": us["title"],
                "recordings": [{key: us[key] for key in ("title", "recordingId", "editionId")}]}
        entries = [work] + [dict(us) for _ in range(99)] if page == 1 else [uk, local]
        payload = {"schemaVersion": 1, "snapshotId": "owned-revision-1", "page": page, "pageSize": 100,
                   "totalItems": 102, "complete": True, "items": entries}
        if self.mode == "empty":
            payload.update(snapshotId="owned-empty-revision", totalItems=0, items=[])
        elif self.mode == "version":
            payload["schemaVersion"] = 2
        elif page == 2:
            if self.mode == "partial":
                payload["complete"] = False
            elif self.mode == "changed":
                payload["snapshotId"] = "changed-during-fetch"
            elif self.mode == "conflicting":
                entries[0]["editionId"] = us["recordingId"]
        return payload

    def response(self, method, route, headers, stream):
        if method != "GET" or route.path != "/owned-custom-list" or route.fragment:
            return self.reject()
        query = parse_qs(route.query, keep_blank_values=True)
        if set(query) != {"page", "pageSize"} or query["pageSize"] != ["100"] or query["page"] not in (["1"], ["2"]):
            return self.reject()
        page = int(query["page"][0])
        self.pages[page] += 1
        if headers.get("Cookie"):
            self.cookie_requests += 1
        if headers.get("Authorization") != "Bearer " + self.token:
            self.auth_failures += 1
            return self.json(401, {"message": self.echo})
        if self.mode == "auth" and page == 2:
            return self.json(401, {"message": self.token + " " + self.echo})
        if self.mode == "failed" and page == 2:
            return self.json(503, {"message": self.token + " " + self.echo})
        if self.mode == "redirect":
            # Same owned origin and a forbidden route. Never a real external URL.
            return self.json(302, {"message": self.echo}, {"Location": self.url + "/forbidden-redirect/" + self.echo})
        if self.mode == "oversized":
            return self.json(200, b" " * (1024 * 1024 + 1))
        return self.json(200, self.page(page), {"Set-Cookie": "owned=unused; Path=/"})


class QbitEndpoint(OwnedEndpoint):
    def __init__(self):
        self.version = "2.9.1"
        self.username = "owned-qbit-user"
        self.password = secrets.token_urlsafe(24)
        self.logins = 0
        super().__init__()

    @property
    def port(self):
        return self.server.server_port

    def response(self, method, route, headers, stream):
        path = route.path
        if method == "POST":
            if path != "/api/v2/auth/login":
                return self.reject()
            length = int(headers.get("Content-Length", "0"))
            if not 0 < length <= 8192:
                return self.reject()
            values = parse_qs(stream.read(length).decode())
            if values != {"username": [self.username], "password": [self.password]}:
                return self.json(403, {"message": "Owned login rejected"})
            self.logins += 1
            return 200, "text/plain", "Ok.", {"Set-Cookie": "SID=owned-qbit-cookie; Path=/"}
        if method != "GET":
            return self.reject()
        if path == "/api/v2/app/webapiVersion":
            return 200, "text/plain", self.version, {}
        if path == "/api/v2/app/version":
            return 200, "text/plain", "v4.5.5" if self.version == "2.9.1" else "v4.6.0", {}
        if path == "/api/v2/app/preferences":
            return self.json(200, {"save_path": "/owned/downloads", "max_ratio_enabled": False, "max_seeding_time_enabled": False,
                                   "max_inactive_seeding_time_enabled": False, "queueing_enabled": True, "dht": True})
        if path == "/api/v2/torrents/categories":
            return self.json(200, {"readarr": {"name": "readarr", "savePath": "/owned/downloads"}})
        if path == "/api/v2/torrents/info":
            return self.json(200, [])
        return self.reject()


def field(resource, name):
    return next(item for item in resource["fields"] if item["name"] == name)


def fields(resource):
    return {item["name"]: item.get("value") for item in resource["fields"]}


def set_fields(resource, **values):
    result = deepcopy(resource)
    for name, value in values.items():
        field(result, name)["value"] = value
    return result


def template(fixture, endpoint, implementation):
    return next(resource for resource in fixture.api(endpoint + "/schema") if resource["implementation"] == implementation)


def seed_legacy_qbit(fixture, qbit):
    resource = template(fixture, "downloadclient", "QBittorrent")
    fixture.check("baseline_qbit_does_not_have_seed_mode", "seedTimeType" not in fields(resource))
    resource = set_fields(resource, host="127.0.0.1", port=qbit.port, username=qbit.username, password=qbit.password, apiKey="")
    resource.update(name=QBIT_NAME, enable=False, removeCompletedDownloads=False, removeFailedDownloads=False)
    return fixture.api("downloadclient?forceSave=true", "POST", resource)


def inspect_qbit(fixture, qbit, legacy):
    schema = template(fixture, "downloadclient", "QBittorrent")
    mode = field(schema, "seedTimeType")
    fixture.check("qbit_schema_exposes_total_and_inactive_with_total_default", mode.get("value") == 0 and
                  {option["value"] for option in mode["selectOptions"]} == {0, 1})
    fresh = fixture.api(f"downloadclient/{legacy['id']}")
    fixture.check("legacy_qbit_defaults_to_total_preserving_saved_fields", fields(fresh) == {**fields(legacy), "seedTimeType": 0} and
                  all(fresh[key] == legacy[key] for key in ("enable", "id", "name", "removeCompletedDownloads", "removeFailedDownloads")))
    test = lambda resource: library_review.request(fixture, "downloadclient/test", "POST", resource)
    fixture.check("older_qbit_still_accepts_total_mode", test(fresh)[0] == 200)
    invalid = set_fields(fresh, seedTimeType=99)
    before = sum(qbit.requests.values())
    fixture.check("invalid_qbit_seed_mode_fails_before_contacting_client", test(invalid)[0] == 400 and sum(qbit.requests.values()) == before)
    inactive = set_fields(fresh, seedTimeType=1)
    status, body = test(inactive)
    fixture.check("old_qbit_rejects_inactive_mode_explicitly", status == 400 and "inactive seed time requires" in json.dumps(body).lower() and "2.9.2" in json.dumps(body))
    inactive["enable"] = True
    status, _ = library_review.request(fixture, f"downloadclient/{legacy['id']}", "PUT", inactive)
    fixture.check("unsupported_enabled_update_keeps_legacy_total_mode", status == 400 and fixture.api(f"downloadclient/{legacy['id']}") == fresh)
    qbit.version = "2.9.2"
    fixture.check("supported_qbit_version_accepts_inactive_mode", test(inactive)[0] == 200)
    saved = fixture.api(f"downloadclient/{legacy['id']}", "PUT", inactive)
    fixture.check("supported_qbit_saves_explicit_inactive_mode", field(saved, "seedTimeType")["value"] == 1 and saved["enable"] is True)
    # No client is active after this exercise; the API still stores the new mode.
    saved["enable"] = False
    saved = fixture.api(f"downloadclient/{legacy['id']}", "PUT", saved)
    fixture.check("disabling_qbit_preserves_its_explicit_inactive_mode", field(saved, "seedTimeType")["value"] == 1 and not saved["enable"] and
                  fields(saved) == {**fields(legacy), "seedTimeType": 1})
    fixture.check("qbit_connection_tests_use_owned_routes_only", qbit.unexpected == 0 and qbit.logins > 0 and
                  set(qbit.requests) <= {("GET", "/api/v2/" + route) for route in ("app/webapiVersion", "app/version", "app/preferences", "torrents/categories", "torrents/info")} | {("POST", "/api/v2/auth/login")})
    return saved


def wait_membership(fixture, list_id):
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        snapshot = fixture.api(f"importlistmembership/{list_id}")
        if snapshot["isCurrent"] and snapshot.get("lastSuccessfulSync"):
            return snapshot
        time.sleep(.1)
    raise common.ValidationFailure("owned_custom_list_sync_did_not_finish")


def sync(fixture, list_id):
    restore.wait_command(fixture, fixture.api("command", "POST", {"name": "ImportListSync", "definitionId": list_id})["id"])
    return fixture.api(f"importlistmembership/{list_id}")


def inspect_custom(fixture, endpoint):
    schema = template(fixture, "importlist", "CustomJsonImport")
    fixture.check("custom_json_is_discovered_and_uses_a_password_field_for_the_token", schema["implementationName"] == "Custom JSON" and
                  field(schema, "apiKey").get("type") == "password" and schema.get("enableAutomaticAdd") is False)
    resource = set_fields(schema, baseUrl=endpoint.url + "/owned-custom-list", apiKey=endpoint.token)
    root = fixture.api("rootfolder")[0]
    resource.update(name=CUSTOM_LIST_NAME, enableAutomaticAdd=True, shouldMonitor="none", shouldMonitorExisting=False, shouldSearch=False,
                    rootFolderPath=root["path"], qualityProfileId=root["defaultQualityProfileId"], metadataProfileId=root["defaultMetadataProfileId"], tags=[])
    test = lambda defined: library_review.request(fixture, "importlist/test", "POST", defined)
    unsafe = set_fields(resource, baseUrl=endpoint.url + "/owned-custom-list?credential=owned")
    before = sum(endpoint.pages.values())
    fixture.check("custom_list_rejects_url_credentials_before_fetching", test(unsafe)[0] == 400 and sum(endpoint.pages.values()) == before)
    status, error = test(set_fields(resource, apiKey="owned-wrong-token"))
    fixture.check("custom_list_auth_error_is_sanitized", status == 400 and "HTTP 401" in json.dumps(error) and endpoint.echo not in json.dumps(error) and endpoint.auth_failures == 1)
    fixture.check("custom_list_test_reads_all_pages", test(resource)[0] == 200 and endpoint.pages[2] >= 1)
    projection = identity.snapshot(fixture)
    created = fixture.api("importlist", "POST", resource)
    list_id = created["id"]
    fixture.check("custom_list_saves_its_exact_endpoint_and_bearer_setting", fields(created) == fields(resource))
    route = f"importlistmembership/{list_id}"
    fixture.check("custom_membership_requires_authentication", library_review.request(fixture, route, key=False)[0] == 401)
    successful = wait_membership(fixture, list_id)
    expected = {endpoint.recording(key)["recordingId"] for key in ("us", "uk", "local")}
    items = successful["items"]
    fixture.check("paged_work_and_recording_entries_deduplicate_exactly_without_merging_regions", len(items) == 3 and {item["sourceBookId"] for item in items} == expected and
                  all(item["sourceEditionId"] == item["sourceBookId"] for item in items))
    fixture.check("local_membership_is_raw_and_regional_membership_is_exact", all(not item.get("bookId") if item["sourceBookId"].startswith("local:") else item["bookId"] == item["sourceBookId"] and item["editionId"] == item["sourceEditionId"] for item in items))
    for mode in ("partial", "failed", "changed", "conflicting", "auth", "version", "redirect", "oversized"):
        endpoint.mode = mode
        requests_before = sum(endpoint.pages.values())
        retained = sync(fixture, list_id)
        fixture.check(f"custom_{mode}_fetch_retains_last_complete_snapshot", retained == successful and sum(endpoint.pages.values()) > requests_before)
    endpoint.mode = "success"
    uk_id = endpoint.recording("uk")["recordingId"]
    fixture.api("importlistexclusion", "POST", {"foreignId": uk_id, "authorName": "Owned exact UK exclusion"})
    excluded = sync(fixture, list_id)
    fixture.check("custom_list_exclusion_is_region_exact_and_retains_membership", {item["sourceBookId"] for item in excluded["items"]} == expected and
                  {item["sourceBookId"] for item in excluded["items"] if item["excluded"]} == {uk_id})
    unchanged = fixture.api(f"importlist/{list_id}")
    disabled = {**unchanged, "enableAutomaticAdd": False}
    fixture.api(f"importlist/{list_id}?forceSave=true", "PUT", disabled)
    before = sum(endpoint.pages.values())
    historical = sync(fixture, list_id)
    fixture.check("disabled_custom_list_retains_stale_snapshot_without_fetch", not historical["isCurrent"] and historical["items"] == excluded["items"] and
                  historical["lastSuccessfulSync"] == excluded["lastSuccessfulSync"] and sum(endpoint.pages.values()) == before)
    fixture.api(f"importlist/{list_id}", "PUT", unchanged)
    # Updating the provider enqueues its own refresh before returning. A current
    # snapshot is not proof that the command has finished. Join that command (or
    # complete a new one) with the successful endpoint still in place before
    # switching its response, so the subsequent command cannot coalesce with it.
    restored = sync(fixture, list_id)
    fixture.check("re_enabled_custom_list_has_completed_current_membership", restored["isCurrent"] and
                  restored.get("lastSuccessfulSync") is not None and {item["sourceBookId"] for item in restored["items"]} == expected)
    endpoint.mode = "empty"
    before = sum(endpoint.pages.values())
    empty = sync(fixture, list_id)
    fixture.check("custom_empty_sync_fetches_a_new_response", sum(endpoint.pages.values()) > before)
    fixture.check("custom_confirmed_empty_list_replaces_its_membership", empty["isCurrent"] and not empty["items"] and empty.get("lastSuccessfulSync") is not None)
    endpoint.mode = "success"
    final = sync(fixture, list_id)
    fixture.check("custom_membership_can_recover_after_confirmed_empty", {item["sourceBookId"] for item in final["items"]} == expected and final["isCurrent"])
    fixture.check("custom_list_never_follows_redirects_or_reuses_cookies", endpoint.unexpected == 0 and endpoint.cookie_requests == 0 and
                  set(endpoint.requests) == {("GET", "/owned-custom-list")})
    fixture.check("custom_list_does_not_change_existing_catalog_or_media", identity.snapshot(fixture, projection) == projection and restore.media_unchanged(fixture))
    return list_id, final


def browser(fixture, output, playwright_module, list_id, qbit_id):
    config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
              "username": fixture.current_username, "password": fixture.current_password,
              "output": str(common.private_directory(output / "screenshots")), "playwrightModule": str(Path(playwright_module).resolve(strict=True)),
              "listName": CUSTOM_LIST_NAME, "listId": list_id, "clientName": QBIT_NAME, "clientId": qbit_id}
    execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
    report = json.loads(execution.stdout)
    checks = report.get("checks")
    if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
        raise common.ValidationFailure("provider_policies_browser_report_invalid")
    fixture.checks.update(checks)
    common.write_json(output / "browser-result.json", report)
    fixture.check("provider_policy_browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))


def run(runtime, output, baseline_runtime, playwright_module=None):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise common.ValidationFailure("provider_policies_requires_older_package")
    common.private_directory(output)
    contract = identity.recipe()
    custom, qbit = CustomEndpoint(contract), QbitEndpoint()
    fixture = app.Fixture(baseline_runtime, output / "application")
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        identity.seed(fixture, contract)
        legacy = seed_legacy_qbit(fixture, qbit)
        projection = identity.snapshot(fixture)
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        # The fixture uses only synthetic credentials and private output. Trace
        # logging is necessary to prove private provider diagnostics are masked.
        fixture.set_xml({"LogLevel": "Trace", "ConsoleLogLevel": "Trace"})
        fixture.start()
        fixture.check("upgraded_provider_fixture_preserves_catalog_and_bytes", identity.snapshot(fixture, projection) == projection and restore.media_unchanged(fixture))
        fixture.check("fixture_has_no_real_indexers_or_enabled_download_clients", not fixture.api("indexer") and all(not item["enable"] for item in fixture.api("downloadclient")))
        stage = "qbit"
        saved = inspect_qbit(fixture, qbit, legacy)
        stage = "custom"
        list_id, members = inspect_custom(fixture, custom)
        stage = "browser"
        if playwright_module is not None:
            browser(fixture, output, playwright_module, list_id, saved["id"])
        stage = "restart"
        fixture.stop()
        trace_logs = [fixture.home / "server.log", *[path for path in (fixture.home / "logs").glob("*.txt") if path.is_file()]]
        payload = b"\n".join(path.read_bytes() for path in trace_logs)
        fixture.check("private_provider_http_trace_redacts_endpoint_token_and_echo", b"[private endpoint]" in payload and custom.token.encode() not in payload and custom.echo.encode() not in payload)
        fixture.start()
        fixture.check("inactive_mode_and_complete_custom_membership_survive_restart", fixture.api(f"downloadclient/{saved['id']}") == saved and
                      fixture.api(f"importlistmembership/{list_id}")["items"] == members["items"] and restore.media_unchanged(fixture))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
        fixture.checks[stage + "_completed"] = False
    finally:
        fixture.cleanup()
        custom.close()
        qbit.close()
        fixture.checks["owned_provider_processes_stopped"] = fixture.process is None and fixture.forced_stops == 0 and not custom.thread.is_alive() and not qbit.thread.is_alive()
    result = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", result)
    return result
