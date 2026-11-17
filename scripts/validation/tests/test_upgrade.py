"""Credential fixtures accept actual policy work factors, without exposing data."""

import base64
import hashlib
from pathlib import Path
import unittest

from scripts.validation import upgrade


class UpgradeTests(unittest.TestCase):
    def test_existing_password_work_factors_are_not_pinned_to_one_release(self):
        for iterations in (9000, 230000):
            salt = b"synthetic salt for testing"
            password = "synthetic local password"
            derived = hashlib.pbkdf2_hmac("sha512", password.encode(), salt, iterations, dklen=32)
            row = (17, "synthetic-identifier", "reader", base64.b64encode(derived).decode(),
                   base64.b64encode(salt).decode(), iterations)
            self.assertTrue(upgrade.password_matches(row, password))
            self.assertFalse(upgrade.password_matches(row, password + "wrong"))

    def test_corrupt_or_unbounded_credential_metadata_is_rejected(self):
        for row in [(17, "identifier", "reader", "not-base64", "invalid", 220000),
                    (17, "identifier", "reader", "", "", 0),
                    (17, "identifier", "reader", "", "", 10000001)]:
            self.assertFalse(upgrade.password_matches(row, "synthetic password"))

    def test_absent_baseline_is_an_explicit_skip_without_touching_output(self):
        result = upgrade.run(Path("unused-candidate"), Path("unused-output"))
        self.assertFalse(result["completed"])
        self.assertEqual(result["passed"], 0)
        self.assertEqual(result["failed"], 0)
        self.assertEqual(result["skipped"], ["upgrade_requires_explicit_baseline_runtime"])


if __name__ == "__main__":
    unittest.main()
