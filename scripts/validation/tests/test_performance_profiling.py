"""Boundary and measurement checks for owned performance job fixtures."""

import http.client
import unittest

from validation import performance_profiling as profiling
from validation.common import ValidationFailure


class PerformanceFixtureTests(unittest.TestCase):
    def test_worker_duration_uses_actual_timespan_including_fractional_seconds(self):
        self.assertAlmostEqual(profiling.command_duration_milliseconds("00:00:00.0045732"), 4.5732)
        self.assertEqual(profiling.command_duration_milliseconds("1.02:03:04.5"), 93784500)
        self.assertEqual(profiling.command_duration_milliseconds("00:00:00"), 0)
        with self.assertRaises(ValidationFailure):
            profiling.command_duration_milliseconds("-00:00:00.5")

    def test_owned_proxy_rejects_connect_and_http_without_forwarding(self):
        proxy = profiling.OwnedRejectingProxy()
        self.addCleanup(proxy.close)
        for method, target in (("CONNECT", "owned-nonforwarded.invalid:443"), ("GET", "http://owned-nonforwarded.invalid/private-marker")):
            with self.subTest(method=method):
                connection = http.client.HTTPConnection("127.0.0.1", proxy.port, timeout=2)
                try:
                    connection.request(method, target)
                    response = connection.getresponse()
                    self.assertEqual(response.status, 502)
                    self.assertEqual(response.read(), b"")
                finally:
                    connection.close()
        self.assertEqual(proxy.rejections, 2)


if __name__ == "__main__":
    unittest.main()
