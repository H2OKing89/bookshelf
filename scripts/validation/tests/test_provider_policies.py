"""Validate the owned provider protocol and fixture isolation boundaries."""

import http.client
import json
from pathlib import Path
import unittest
from unittest.mock import patch
from urllib.parse import urlencode

from scripts.validation import identity, provider_policies as policies
from scripts.validation.common import ValidationFailure


class ProviderPolicyTests(unittest.TestCase):
    def setUp(self):
        self.custom = policies.CustomEndpoint(identity.recipe())
        self.qbit = policies.QbitEndpoint()
        self.addCleanup(self.qbit.close)
        self.addCleanup(self.custom.close)

    @staticmethod
    def request(endpoint, path, method="GET", body=None, headers=None):
        connection = http.client.HTTPConnection("127.0.0.1", endpoint.server.server_port, timeout=3)
        try:
            connection.request(method, path, body=body, headers=headers or {})
            response = connection.getresponse()
            return response.status, response.read(), dict(response.getheaders())
        finally:
            connection.close()

    def page(self, number, mode="success", token=None):
        self.custom.mode = mode
        return self.request(self.custom, f"/owned-custom-list?page={number}&pageSize=100",
                            headers={"Authorization": "Bearer " + (self.custom.token if token is None else token)})

    def test_custom_endpoint_has_owned_paging_exact_regional_and_local_rows(self):
        self.assertEqual(self.custom.server.server_address[0], "127.0.0.1")
        status, first, _ = self.page(1)
        self.assertEqual(status, 200)
        first = json.loads(first)
        status, second, _ = self.page(2)
        self.assertEqual(status, 200)
        second = json.loads(second)
        self.assertEqual(len(first["items"]), 100)
        self.assertEqual(first["items"][0]["kind"], "work")
        self.assertEqual(first["items"][0]["recordings"][0]["recordingId"], "audible:us:B011111111")
        self.assertEqual(second["items"][0]["recordingId"], "audible:uk:B011111111")
        self.assertTrue(second["items"][1]["recordingId"].startswith("local:recording:"))
        self.assertEqual((first["snapshotId"], first["totalItems"]), (second["snapshotId"], second["totalItems"]))
        self.assertEqual(first["totalItems"], 102)

    def test_failure_modes_are_distinct_and_empty_is_explicit(self):
        for mode, expected in (("failed", 503), ("auth", 401), ("partial", 200), ("changed", 200), ("conflicting", 200)):
            with self.subTest(mode=mode):
                status, first, _ = self.page(1, mode)
                self.assertEqual(status, 200)
                status, second, _ = self.page(2, mode)
                self.assertEqual(status, expected)
                if mode == "partial":
                    self.assertFalse(json.loads(second)["complete"])
                elif mode == "changed":
                    self.assertNotEqual(json.loads(first)["snapshotId"], json.loads(second)["snapshotId"])
                elif mode == "conflicting":
                    recording = json.loads(second)["items"][0]
                    self.assertNotEqual(recording["recordingId"], recording["editionId"])
        status, empty, _ = self.page(1, "empty")
        self.assertEqual(status, 200)
        empty = json.loads(empty)
        self.assertTrue(empty["complete"])
        self.assertEqual(empty["totalItems"], 0)
        self.assertEqual(empty["items"], [])

    def test_mock_rejects_wrong_auth_other_routes_and_extra_query_parameters(self):
        self.assertEqual(self.page(1, token="wrong")[0], 401)
        self.assertEqual(self.request(self.custom, "/forbidden-redirect")[0], 404)
        self.assertEqual(self.request(self.custom, "/owned-custom-list?page=1&pageSize=100&extra=1")[0], 404)
        self.assertEqual(self.custom.auth_failures, 1)
        self.assertEqual(self.custom.unexpected, 2)
        status, _, headers = self.page(1, "redirect")
        self.assertEqual(status, 302)
        self.assertTrue(headers["Location"].startswith(self.custom.url + "/forbidden-redirect/"))

    def test_qbit_mock_accepts_only_owned_login_and_read_only_api(self):
        self.assertEqual(self.qbit.server.server_address[0], "127.0.0.1")
        auth = urlencode({"username": self.qbit.username, "password": self.qbit.password})
        status, response, headers = self.request(self.qbit, "/api/v2/auth/login", "POST", auth)
        self.assertEqual((status, response), (200, b"Ok."))
        self.assertIn("SID=owned-qbit-cookie", headers["Set-Cookie"])
        self.assertEqual(self.request(self.qbit, "/api/v2/app/webapiVersion")[1], b"2.9.1")
        self.qbit.version = "2.9.2"
        self.assertEqual(self.request(self.qbit, "/api/v2/app/webapiVersion")[1], b"2.9.2")
        self.assertEqual(self.request(self.qbit, "/api/v2/torrents/info")[1], b"[]")
        self.assertEqual(self.request(self.qbit, "/api/v2/torrents/add", "POST", "")[0], 404)
        self.assertEqual(self.qbit.unexpected, 1)

    def test_same_runtime_rejected_before_starting_new_servers(self):
        runtime = Path("/owned/runtime")
        output = Path("/owned/output")
        with patch.object(policies.common, "validate_output", return_value=(runtime, output)), patch.object(policies, "CustomEndpoint") as endpoint:
            with self.assertRaisesRegex(ValidationFailure, "requires_older_package"):
                policies.run(runtime, output, runtime)
            endpoint.assert_not_called()

    def test_set_fields_uses_a_copy(self):
        resource = {"fields": [{"name": "seedTimeType", "value": 0}]}
        self.assertEqual(policies.fields(policies.set_fields(resource, seedTimeType=1)), {"seedTimeType": 1})
        self.assertEqual(policies.fields(resource), {"seedTimeType": 0})
