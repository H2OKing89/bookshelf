"""Protocol and safety tests for the owned cancellation validation fixture."""

import http.client
import importlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import urlsplit

from scripts.validation import cancellation


class MetadataServerTests(unittest.TestCase):
    def setUp(self):
        self.provider = cancellation.MetadataServer()
        self.provider.start()
        self.addCleanup(self.provider.close)
        self.port = urlsplit(self.provider.url).port

    def connection(self):
        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=2)
        self.addCleanup(connection.close)
        return connection

    def test_partial_body_observes_disconnect_and_next_request_has_complete_metadata(self):
        connection = self.connection()
        route = "/authors/" + cancellation.AUTHOR_ASIN + "?region=us"
        connection.request("GET", route)
        response = connection.getresponse()
        self.assertEqual(response.status, 200)
        self.assertEqual(response.read(1), b"{")
        self.assertTrue(self.provider.entered.wait(timeout=1))
        self.assertGreater(int(response.getheader("Content-Length")), 1)
        self.assertFalse(self.provider.disconnected.is_set())
        response.close()
        connection.close()
        self.assertTrue(self.provider.disconnected.wait(timeout=1))
        retry = self.connection()
        retry.request("GET", route)
        full = retry.getresponse()
        self.assertEqual(json.loads(full.read()), cancellation.author_resource())
        self.assertEqual(self.provider.unexpected, 0)
        self.assertEqual(self.provider.faults, 0)

    def test_unexpected_paths_marketplaces_and_absolute_targets_are_rejected(self):
        for route in ("/unexpected?region=us", "/books/search?region=uk",
                      "http://unrelated.invalid/books/search?region=us"):
            with self.subTest(route=route):
                connection = self.connection()
                connection.request("GET", route, headers={"Host": "127.0.0.1:" + str(self.port)})
                response = connection.getresponse()
                self.assertEqual(response.status, 404)
                response.read()
                connection.close()
        self.assertEqual(self.provider.unexpected, 3)
        self.assertFalse(self.provider.entered.is_set())

    def test_cleanup_closes_only_the_owned_listener_and_held_response(self):
        connection = self.connection()
        connection.request("GET", "/authors/" + cancellation.AUTHOR_ASIN + "?region=us")
        response = connection.getresponse()
        self.assertEqual(response.read(1), b"{")
        self.assertTrue(self.provider.entered.wait(timeout=1))
        self.assertTrue(self.provider.close())
        self.assertFalse(self.provider.thread.is_alive())
        with self.assertRaises(http.client.IncompleteRead):
            response.read()


class CancellationRunTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.runtime = self.root / "runtime"
        (self.runtime / "UI").mkdir(parents=True)
        for name in ("Readarr", "Readarr.Core.dll", "Readarr.runtimeconfig.json", "UI/index.html"):
            (self.runtime / name).write_bytes(b"owned synthetic package")

    def test_import_has_no_process_or_listener_side_effects(self):
        with patch.object(subprocess, "Popen") as start, patch.object(cancellation, "ThreadingHTTPServer") as listener:
            importlib.reload(cancellation)
        start.assert_not_called()
        listener.assert_not_called()

    def test_failure_reports_stage_and_type_without_exception_payload(self):
        with patch.object(cancellation, "configure", side_effect=RuntimeError("private-request-token")):
            report = cancellation.run(self.runtime, self.root / "output")
        self.assertFalse(report["completed"])
        self.assertEqual(report["failure"], {"stage": "cancellation_setup", "exceptionType": "RuntimeError"})
        self.assertNotIn("private-request-token", json.dumps(report))
        for name in ("all_owned_application_processes_cleaned_up", "all_owned_metadata_listeners_closed", "input_package_unchanged"):
            self.assertTrue(report["checks"][name])
        self.assertEqual((self.root / "output").stat().st_mode & 0o777, 0o700)

    def test_interrupt_propagates_after_owned_server_cleanup(self):
        provider = cancellation.MetadataServer()
        with (patch.object(cancellation, "MetadataServer", return_value=provider),
              patch.object(cancellation, "configure", side_effect=KeyboardInterrupt)):
            with self.assertRaises(KeyboardInterrupt):
                cancellation.run(self.runtime, self.root / "output")
        self.assertFalse(provider.thread.is_alive())


if __name__ == "__main__":
    unittest.main()
