"""Exercise command cancellation and shutdown against owned loopback metadata.

The package is read-only input. Both application homes, media bytes and HTTP
responses are newly generated fixtures; no saved development home is accepted.
Importing this module starts no processes or listeners.
"""

from __future__ import annotations

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import select
import socket
import sqlite3
import threading
import time
from urllib.parse import parse_qs, urlsplit

from .app import Fixture, result, safe_failure
from .common import ValidationFailure, private_directory, snapshot_runtime, validate_output
from .restore import media_unchanged, seed_library, snapshot


AUTHOR_ASIN = "B000000001"
BOOK_ASIN = "B000000002"
AUTHOR_ID = "audible:us:" + AUTHOR_ASIN
BOOK_ID = "audible:us:" + BOOK_ASIN
CANCEL_TIMEOUT = 5


def author_resource():
    return {"asin": AUTHOR_ASIN, "region": "us", "name": "Synthetic Author",
            "description": "Owned cancellation fixture", "genres": [], "similar": []}


def book_resource():
    return {"asin": BOOK_ASIN, "region": "us", "title": "Synthetic Recording",
            "language": "english", "runtimeLengthMin": 1,
            "releaseDate": "2020-01-01T00:00:00Z", "description": "Owned recording metadata",
            "publisherName": "Synthetic Publisher", "contentType": "Audiobook",
            "authors": [{"asin": AUTHOR_ASIN, "name": "Synthetic Author"}],
            "narrators": [{"name": "Fixture Narrator"}], "genres": []}


class MetadataServer:
    """One held response, with an observable client disconnect rather than a delay."""

    def __init__(self):
        self.entered = threading.Event()
        self.disconnected = threading.Event()
        self.release = threading.Event()
        self.lock = threading.Lock()
        self.hold_next = True
        self.requests: list[str] = []
        self.unexpected = 0
        self.faults = 0
        self.sockets: set[socket.socket] = set()
        self.handlers: set[threading.Thread] = set()
        owner = self

        class Server(ThreadingHTTPServer):
            daemon_threads = True
            block_on_close = False

            def handle_error(self, request, client_address):
                # Request exceptions may contain headers. Never print them.
                with owner.lock:
                    owner.faults += 1

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, format, *args):
                pass

            def do_GET(self):
                owner.respond(self)

        self.server = Server(("127.0.0.1", 0), Handler)
        self.server.timeout = 0.2
        self.thread = threading.Thread(target=self.server.serve_forever,
                                       kwargs={"poll_interval": 0.05}, daemon=True)
        self.url = "http://127.0.0.1:" + str(self.server.server_address[1])

    def start(self):
        self.thread.start()

    def respond(self, handler):
        worker = threading.current_thread()
        with self.lock:
            self.handlers.add(worker)
        target = urlsplit(handler.path)
        query = parse_qs(target.query)
        valid = (handler.client_address[0] == "127.0.0.1" and not target.scheme and not target.netloc
                 and handler.headers.get("Host") == self.url.removeprefix("http://")
                 and query.get("region") == ["us"])
        routes = {
            "/authors/" + AUTHOR_ASIN: author_resource(),
            "/authors": [{"asin": AUTHOR_ASIN, "name": "Synthetic Author"}],
            "/books/search": [book_resource()],
            "/books/" + BOOK_ASIN: book_resource(),
            "/books/" + BOOK_ASIN + "/chapters": {
                "asin": BOOK_ASIN, "region": "us", "runtimeLengthMs": 60000,
                "isAccurate": True, "chapters": [
                    {"title": "Synthetic chapter", "ordinal": 1, "startOffsetMs": 0, "lengthMs": 60000}]},
        }
        with self.lock:
            self.requests.append(target.path)
            if not valid or target.path not in routes:
                self.unexpected += 1
            hold = valid and target.path == "/authors/" + AUTHOR_ASIN and self.hold_next
            if hold:
                self.hold_next = False
                self.sockets.add(handler.connection)
        payload = json.dumps(routes.get(target.path) if valid else None).encode("utf-8")
        handler.send_response(200 if valid and target.path in routes else 404)
        handler.send_header("Content-Type", "application/json")
        handler.send_header("Content-Length", str(len(payload)))
        handler.send_header("Connection", "close")
        handler.end_headers()
        handler.close_connection = True
        try:
            if not hold:
                handler.wfile.write(payload)
                return
            # Headers have arrived, but ReadAsByteArrayAsync is still pending.
            handler.wfile.write(payload[:1])
            handler.wfile.flush()
            self.entered.set()
            while not self.release.is_set():
                try:
                    readable, _, _ = select.select([handler.connection], [], [], 0.05)
                    if readable and not handler.connection.recv(1):
                        self.disconnected.set()
                        break
                except OSError:
                    self.disconnected.set()
                    break
        except (BrokenPipeError, ConnectionResetError):
            if hold:
                self.disconnected.set()
        finally:
            with self.lock:
                self.handlers.discard(worker)
                if hold:
                    self.sockets.discard(handler.connection)

    def close(self):
        self.release.set()
        with self.lock:
            sockets = tuple(self.sockets)
        for connection in sockets:
            try:
                connection.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            connection.close()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        with self.lock:
            handlers = tuple(self.handlers)
        for worker in handlers:
            worker.join(timeout=3)
        return not self.thread.is_alive() and not any(worker.is_alive() for worker in handlers)


def configure(fixture, provider):
    fixture.configure_home()
    fixture.start()
    settings = fixture.api("config/metadataprovider/1")
    settings.update({"audnexusUrl": provider.url, "audnexusRegion": "us", "audibleRsEnabled": False,
                     "writeAudioTags": "no", "scrubAudioTags": False,
                     "updateCovers": False, "embedMetadata": False})
    fixture.api("config/metadataprovider/1", "PUT", settings)
    saved = fixture.api("config/metadataprovider/1")
    fixture.check("only_loopback_metadata_provider_configured",
                  saved["audnexusUrl"] == provider.url and saved["audnexusRegion"] == "us"
                  and saved["audibleRsEnabled"] is False)
    seed_library(fixture)
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.execute('UPDATE "AuthorMetadata" SET "ForeignAuthorId" = ? WHERE "Id" = 7', (AUTHOR_ID,))
        database.execute('UPDATE "Books" SET "ForeignBookId" = ? WHERE "Id" = 13', (BOOK_ID,))
        database.execute('UPDATE "Editions" SET "ForeignEditionId" = ? WHERE "Id" = 17', (BOOK_ID,))
        database.execute('INSERT OR REPLACE INTO "Config" ("Key", "Value") VALUES (?, ?)',
                         ("rescanafterrefresh", "Never"))
    fixture.start()
    fixture.check("fresh_home_and_temporary_directory_are_private",
                  fixture.home.stat().st_mode & 0o777 == 0o700
                  and fixture.private_tmp.stat().st_mode & 0o777 == 0o700)
    return snapshot(fixture)


def wait_command(fixture, command_id, expected, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        command = fixture.api("command/" + str(command_id))
        status = str(command.get("status", "")).lower()
        if status == expected:
            return command
        if status in ("completed", "failed", "cancelled", "aborted", "orphaned"):
            raise ValidationFailure("command_reached_unexpected_terminal_state")
        time.sleep(0.05)
    raise ValidationFailure("command_state_wait_timed_out")


def start_blocked_refresh(fixture, provider):
    command = fixture.api("command", "POST", {"name": "RefreshAuthor", "authorId": 11})
    fixture.check("refresh_reached_partial_metadata_response", provider.entered.wait(timeout=10))
    started = wait_command(fixture, command["id"], "started", 5)
    fixture.check("refresh_command_was_started_before_cancellation", started.get("started") is not None)
    return command["id"]


def profile_uncached(fixture, provider):
    with sqlite3.connect(fixture.home / "cache.db") as database:
        return database.execute('SELECT COUNT(*) FROM "HttpResponse" WHERE "Url" LIKE ?',
                                (provider.url + "/authors/" + AUTHOR_ASIN + "%",)).fetchone()[0] == 0


def cancel_and_retry(fixture, provider, before):
    command_id = start_blocked_refresh(fixture, provider)
    start = time.monotonic()
    status, _, _ = fixture.request(fixture.URL_BASE + "/api/v1/command/" + str(command_id),
                                   "DELETE", key=True, timeout=CANCEL_TIMEOUT)
    fixture.check("running_refresh_cancellation_is_accepted", status in (200, 202, 204))
    cancelled = wait_command(fixture, command_id, "cancelled", CANCEL_TIMEOUT)
    fixture.check("running_refresh_is_cancelled_promptly", time.monotonic() - start < CANCEL_TIMEOUT)
    fixture.check("cancelled_refresh_has_no_failure_exception", not cancelled.get("exception"))
    fixture.check("cancellation_closed_the_held_http_response", provider.disconnected.wait(timeout=1))
    fixture.check("cancelled_metadata_response_was_not_cached", profile_uncached(fixture, provider))
    fixture.check("cancellation_preserved_the_complete_saved_catalog", snapshot(fixture) == before)
    fixture.check("cancellation_preserved_all_owned_media", media_unchanged(fixture))
    retry = fixture.api("command", "POST", {"name": "RefreshAuthor", "authorId": 11})
    completed = wait_command(fixture, retry["id"], "completed", 45)
    fixture.check("subsequent_refresh_completed_without_cooldown_poison", not completed.get("exception"))
    after = snapshot(fixture)
    fixture.check("successful_retry_preserved_files_history_and_users",
                  all(after[name] == before[name] for name in ("RootFolders", "BookFiles", "History", "Users")))
    books = fixture.api("book", data=None)
    fixture.check("successful_retry_retained_the_owned_recording",
                  len(books) == 1 and books[0]["id"] == 13 and books[0]["foreignBookId"] == BOOK_ID)
    fixture.check("successful_retry_preserved_all_owned_media", media_unchanged(fixture))
    fixture.check("retry_fetched_a_new_complete_author_response",
                  provider.requests.count("/authors/" + AUTHOR_ASIN) >= 2
                  and "/books/search" in provider.requests and "/books/" + BOOK_ASIN in provider.requests)
    fixture.check("metadata_requests_used_only_the_expected_loopback_routes", provider.unexpected == 0 and provider.faults == 0)
    fixture.stop()
    fixture.check("cancellation_fixture_stopped_gracefully", fixture.exit_codes[-1] == 0 and fixture.forced_stops == 0)


def shutdown_blocked(fixture, provider, before):
    start_blocked_refresh(fixture, provider)
    start = time.monotonic()
    fixture.stop()
    fixture.check("shutdown_during_metadata_read_is_bounded", time.monotonic() - start < 10)
    fixture.check("shutdown_during_metadata_read_is_graceful", fixture.exit_codes[-1] == 0 and fixture.forced_stops == 0)
    fixture.check("shutdown_closed_the_held_http_response", provider.disconnected.wait(timeout=1))
    fixture.check("shutdown_response_was_not_cached", profile_uncached(fixture, provider))
    fixture.check("shutdown_preserved_the_complete_saved_catalog", snapshot(fixture) == before)
    fixture.check("shutdown_preserved_all_owned_media", media_unchanged(fixture))
    fixture.check("metadata_requests_used_only_the_expected_loopback_routes", provider.unexpected == 0 and provider.faults == 0)


def run(runtime: Path, output: Path) -> dict:
    runtime, output = validate_output(runtime, output)
    before_runtime = snapshot_runtime(runtime)
    private_directory(output)
    candidate = Fixture(runtime, output / "cancel-and-retry")
    shutdown = None
    providers = []
    completed = False
    failure = None
    stage = "cancellation_setup"
    try:
        provider = MetadataServer()
        providers.append(provider)
        provider.start()
        before = configure(candidate, provider)
        stage = "active_command_cancellation"
        cancel_and_retry(candidate, provider, before)
        stage = "shutdown_setup"
        shutdown = Fixture(runtime, output / "shutdown")
        shutdown_provider = MetadataServer()
        providers.append(shutdown_provider)
        shutdown_provider.start()
        before_shutdown = configure(shutdown, shutdown_provider)
        stage = "active_request_shutdown"
        shutdown_blocked(shutdown, shutdown_provider, before_shutdown)
        completed = True
    except Exception as error:
        candidate.checks[stage + "_completed"] = False
        failure = safe_failure(stage, error)
        if isinstance(error, ValidationFailure):
            failure["code"] = error.code
    finally:
        clean = True
        for fixture in (shutdown, candidate):
            if fixture is not None:
                try:
                    fixture.cleanup()
                    clean = clean and fixture.process is None and fixture.forced_stops == 0
                except Exception:
                    clean = False
        candidate.checks["all_owned_application_processes_cleaned_up"] = clean
        servers_clean = True
        for provider in providers:
            try:
                servers_clean = provider.close() and servers_clean
            except Exception:
                servers_clean = False
        candidate.checks["all_owned_metadata_listeners_closed"] = servers_clean
        if shutdown is not None:
            candidate.checks.update({"shutdown_" + name: value for name, value in shutdown.checks.items()})
        try:
            candidate.checks["input_package_unchanged"] = snapshot_runtime(runtime) == before_runtime
        except Exception:
            candidate.checks["input_package_unchanged"] = False
    return result(candidate, completed, failure=failure, metadataProvider="owned_loopback_audnexus",
                  partialResponseBody=True, commandCancellationTimeoutSeconds=CANCEL_TIMEOUT)
