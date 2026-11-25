"""Validate both queue contracts with owned loopback clients and recorded coverage.

The fixture uses disposable app state, two fake qBittorrent endpoints, and a
synthetic catalog. It never starts torrents or accesses a real client/library.
"""
from datetime import datetime, timezone
import json
from pathlib import Path
import sqlite3
from urllib.parse import parse_qs

from . import app, common, identity, provider_policies, restore, upgrade

SHARED = "0123456789ABCDEF0123456789ABCDEF01234567"
UNRESOLVED = "FEDCBA9876543210FEDCBA9876543210FEDCBA98"
TITLE = "Owned Client Release"
GRAB_TITLE = "Synthetic Author - Synthetic Shared Work"
API_PREFIX = "/api/v2/"


class QueueEndpoint(provider_policies.QbitEndpoint):
    def __init__(self, owned_downloads):
        self.downloads = list(owned_downloads)
        self.removals = []
        super().__init__()

    def response(self, method, route, headers, stream):
        if method == "GET" and route.path == "/api/v2/torrents/info":
            return self.json(200, [
                {"hash": download.lower(), "name": TITLE if download == SHARED else "Unknown owned package",
                 "size": 1000, "progress": 0.25, "eta": 240, "category": "readarr", "state": "downloading",
                 "save_path": "/owned/downloads", "content_path": "/owned/downloads/owned-package"}
                for download in self.downloads
            ])
        if method == "POST" and route.path == "/api/v2/torrents/delete":
            length = int(headers.get("Content-Length", "0"))
            if not 0 < length <= 1024:
                return self.reject()
            values = parse_qs(stream.read(length).decode())
            expected = values.get("hashes", [])
            if len(expected) != 1 or expected[0].upper() not in self.downloads or values.get("deleteFiles") != ["true"]:
                return self.reject()
            download = expected[0].upper()
            self.removals.append(download)
            self.downloads.remove(download)
            return 200, "text/plain", "", {}
        return super().response(method, route, headers, stream)


def create_client(fixture, endpoint, name):
    schema = provider_policies.template(fixture, "downloadclient", "QBittorrent")
    resource = provider_policies.set_fields(schema, host="127.0.0.1", port=endpoint.port,
                                            username=endpoint.username, password=endpoint.password, apiKey="")
    resource.update(name=name, enable=True, removeCompletedDownloads=False, removeFailedDownloads=True)
    return fixture.api("downloadclient?forceSave=true", "POST", resource)["id"]


def seed_history(fixture, clients):
    fixture.stop()
    date = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ")
    quality = json.dumps({"quality": 12, "revision": {"version": 1, "real": 0}})
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        for client, book_ids in clients:
            data = {"BookIds": json.dumps(book_ids)}
            restore.insert(database, "DownloadHistory", {
                "EventType": 1, "AuthorId": 11, "DownloadId": SHARED, "SourceTitle": GRAB_TITLE,
                "Date": date, "Protocol": 2, "IndexerId": 0, "DownloadClientId": client, "Data": json.dumps(data),
            })
            history = {"downloadClientId": str(client), "downloadClient": "QBittorrent",
                       "publishedDate": date, "size": "1000", "protocol": "2", "indexer": "Owned fixture",
                       "ReleaseSource": "InteractiveSearch"}
            for book_id in book_ids:
                restore.insert(database, "History", {
                    "EventType": 1, "AuthorId": 11, "BookId": book_id, "DownloadId": SHARED,
                    "SourceTitle": GRAB_TITLE, "Date": date, "Quality": quality, "Data": json.dumps(history),
                })
    fixture.start()


def queue_refresh(fixture):
    command = fixture.api("command", "POST", {"name": "RefreshMonitoredDownloads"})
    restore.wait_command(fixture, command["id"])


def read_v2(fixture, path="queue", method="GET", data=None, expected=200):
    status, _, body = fixture.request(fixture.URL_BASE + API_PREFIX + path, method, data, key=True)
    fixture.check("v2_expected_http_status", status == expected)
    return json.loads(body) if body and expected == 200 else None


def targets(*rows):
    return [{"id": row["id"], "revision": row["revision"]} for row in rows]


def run(runtime, output):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    endpoints = []
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        identity.seed(fixture, identity.recipe())
        left = QueueEndpoint([SHARED, UNRESOLVED])
        right = QueueEndpoint([SHARED])
        endpoints = [left, right]
        left_id, right_id = create_client(fixture, left, "Owned A"), create_client(fixture, right, "Owned B")
        fixture.check("no_configured_indexer", not fixture.api("indexer"))
        stage = "seed_history"
        seed_history(fixture, [(left_id, [101, 102]), (right_id, [103])])
        stage = "refresh"
        queue_refresh(fixture)
        query = "queue?page=1&pageSize=20&includeUnknownAuthorItems=true"
        legacy = fixture.api(query)
        page = read_v2(fixture, query)
        rows = page["records"]
        common.write_json(output / "owned-queue-projection.json",
                          {"legacy": [{"id": row["id"], "bookId": row.get("bookId"), "downloadClientId": row.get("downloadClientId")} for row in legacy["records"]],
                           "downloads": [{"id": row["id"], "kind": row["kind"], "coverageSource": row["coverageSource"],
                                          "recordings": row["recordings"], "size": row["size"], "sizeleft": row["sizeleft"]} for row in rows]})
        fixture.check("v1_keeps_book_rows_while_v2_counts_downloads",
                      legacy["totalRecords"] == 4 and page["totalRecords"] == 3 and all(type(row["id"]) is int for row in legacy["records"]))
        jobs = {row["id"]: row for row in rows}
        first = jobs[f"download:{left_id}:{SHARED}"]
        second = jobs[f"download:{right_id}:{SHARED}"]
        unknown = jobs[f"download:{left_id}:{UNRESOLVED}"]
        fixture.check("same_hash_different_client_coverage_is_not_merged",
                      first["coverageSource"] == second["coverageSource"] == "recordedGrab" and
                      [record["recordingId"] for record in first["recordings"]] ==
                      ["audible:us:B011111111", "audible:uk:B011111111"] and
                      [record["recordingId"] for record in second["recordings"]] == ["audible:us:B022222222"])
        fixture.check("unknown_download_visible_without_claimed_coverage",
                      unknown["coverageSource"] == "unknown" and unknown["recordings"] == [] and unknown["canRemoveFromClient"])
        fixture.check("progress_and_size_not_multiplied_by_recording_count",
                      first["size"] == 1000 and first["sizeleft"] == 750 and second["size"] == 1000)
        counts = read_v2(fixture, "queue/status")
        fixture.check("sidebar_counts_downloads_not_book_rows", counts["totalCount"] == 3 and counts["count"] == 2 and counts["unknownCount"] == 1)
        filtered = read_v2(fixture, "queue?page=999&pageSize=1&status=downloading&protocol=torrent&includeUnknownAuthorItems=false")
        fixture.check("filters_and_clamping_operate_on_download_count", filtered["totalRecords"] == 2 and filtered["page"] == 2 and len(filtered["records"]) == 1)
        stage = "stale_actions"
        read_v2(fixture, "queue/remove", "POST", {"targets": targets(first, first), "removeFromClient": True}, expected=400)
        stale = targets(first, second)
        stale[-1]["revision"] = "stale"
        read_v2(fixture, "queue/remove", "POST", {"targets": stale, "removeFromClient": True}, expected=409)
        fixture.check("invalid_bulk_preflight_has_no_client_side_effects", left.removals == right.removals == [])
        stage = "blocklist_and_remove"
        fixture.check("recorded_grab_can_be_blocklisted", first["canBlocklist"])
        read_v2(fixture, "queue/remove", "POST", {"targets": targets(first), "removeFromClient": True,
                                                  "blocklist": True, "skipRedownload": True}, expected=204)
        fixture.check("remove_and_blocklist_act_once_on_selected_client", left.removals == [SHARED] and right.removals == [])
        blocklist = fixture.api("blocklist?page=1&pageSize=10")
        fixture.check("one_blocklist_record_retains_exact_bundle_coverage",
                      blocklist["totalRecords"] == 1 and set(blocklist["records"][0]["bookIds"]) == {101, 102})
        queue_refresh(fixture)
        remaining = read_v2(fixture, query)["records"]
        fixture.check("other_client_and_unresolved_job_remain", {row["id"] for row in remaining} == {second["id"], unknown["id"]})
        fixture.check("fixture_only_used_owned_client_routes", all(not endpoint.unexpected for endpoint in endpoints))
        fixture.check("owned_media_unchanged", restore.media_unchanged(fixture))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        for endpoint in endpoints:
            endpoint.close()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    value = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", value)
    return value
