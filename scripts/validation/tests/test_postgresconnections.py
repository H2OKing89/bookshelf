from copy import deepcopy
import importlib
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import xml.etree.ElementTree as ET

from validation import postgresconnections as pg
from validation.common import ValidationFailure
from validation.tests.test_common import package


class PostgresConnectionBoundaryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = package(self.root / "runtime")
        self.baseline = package(self.root / "baseline")
        (self.baseline / "Readarr.Core.dll").write_bytes(b"different synthetic baseline")

    def test_import_has_no_application_container_or_sdk_side_effect(self):
        with patch.object(subprocess, "Popen") as process, patch.object(pg.datastore.OwnedPostgres, "start") as postgres:
            importlib.reload(pg)
        process.assert_not_called()
        postgres.assert_not_called()

    def test_baseline_is_required_and_identical_package_is_rejected_before_execution(self):
        for baseline in (None, self.runtime):
            with self.subTest(baseline=baseline), patch.object(pg.datastore.OwnedPostgres, "start") as start:
                with self.assertRaises(ValidationFailure):
                    pg.run(self.runtime, self.root / "output", baseline_runtime=baseline)
                start.assert_not_called()
                self.assertFalse((self.root / "output").exists())

    def test_failure_is_secret_safe_and_owned_postgres_cleanup_still_runs(self):
        with (patch.object(pg, "ensure_host_compatible"),
              patch.object(pg.datastore.OwnedPostgres, "start", side_effect=RuntimeError("private-dsn-token")),
              patch.object(pg.datastore.OwnedPostgres, "close", return_value=True) as close):
            report = pg.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        self.assertFalse(report["completed"])
        self.assertEqual(report["failure"], {"stage": "owned_postgres_start", "exceptionType": "RuntimeError"})
        self.assertNotIn("private-dsn-token", json.dumps(report))
        self.assertTrue(report["checks"]["candidate_package_unchanged"])
        self.assertTrue(report["checks"]["baseline_package_unchanged"])
        self.assertTrue(report["checks"]["owned_postgres_container_and_socket_removed"])
        close.assert_called_once()
        self.assertEqual((self.root / "output").stat().st_mode & 0o777, 0o700)

    def test_interrupt_joins_fixture_cleanup_and_postgres_cleanup(self):
        fixture = SimpleNamespace(process=object(), forced_stops=0)
        fixture.cleanup = Mock(side_effect=lambda: setattr(fixture, "process", None))

        def interrupt(*args):
            args[3].append(fixture)
            raise KeyboardInterrupt()

        server = SimpleNamespace(st_mode=0o700)
        def start(postgres):
            (postgres.output / "data").mkdir(parents=True)
            (postgres.output / "data" / "pg_hba.conf").write_text("local all " + pg.ROLE + " scram-sha-256\n")
            postgres.socket = Mock()
            postgres.socket.stat.return_value = server
        with (patch.object(pg, "ensure_host_compatible"),
              patch.object(pg.datastore.OwnedPostgres, "start", start),
              patch.object(pg.datastore.OwnedPostgres, "sql", return_value="16"),
              patch.object(pg.datastore.OwnedPostgres, "close", return_value=True) as close,
              patch.object(pg, "scram_role"), patch.object(pg, "switch_existing", side_effect=interrupt),
              self.assertRaises(KeyboardInterrupt)):
            pg.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        fixture.cleanup.assert_called_once()
        self.assertIsNone(fixture.process)
        close.assert_called_once()

    def test_changed_candidate_never_passes_after_an_early_failure(self):
        def fail(postgres):
            (self.runtime / "Readarr.Core.dll").write_bytes(b"changed synthetic candidate")
            raise ValidationFailure("owned_injected_failure")
        with (patch.object(pg, "ensure_host_compatible"),
              patch.object(pg.datastore.OwnedPostgres, "start", fail),
              patch.object(pg.datastore.OwnedPostgres, "close", return_value=True)):
            report = pg.run(self.runtime, self.root / "output", baseline_runtime=self.baseline)
        self.assertFalse(report["checks"]["candidate_package_unchanged"])
        self.assertGreater(report["failed"], 0)

    def test_upgrade_snapshot_requires_exact_old_values_and_known_null_additions(self):
        before = {"BookFiles": [{"Id": 19, "Path": "/owned.m4b"}],
                  "Editions": [{"Id": 17, "Title": "Owned"}],
                  "Books": [{"Id": 13, "ReleaseDate": "2027-02-02"}],
                  "settings": [{"Key": "preferpathasin", "Value": "False"}],
                  "cache": [{"Id": 999, "Response": "owned"}],
                  "log": [{"Id": 999, "Message": "owned"}]}
        after = pg.datastore.expected_additive_postgres_snapshot(before)
        self.assertTrue(pg.dsn_upgrade_preserves_snapshot(before, after))
        for table, column, wrong in (("Books", "ReleaseDate", "wrong date"),
                                     ("BookFiles", "TaggedLanguage", "inferred"),
                                     ("BookFiles", "Unexpected", None),
                                     ("Editions", "LocalMetadataRevision", "unexpected revision"),
                                     ("settings", "Value", "True"),
                                     ("cache", "Response", "wrong cache"),
                                     ("log", "Message", "wrong log")):
            with self.subTest(table=table, column=column):
                changed = deepcopy(after)
                changed[table][0][column] = wrong
                self.assertFalse(pg.dsn_upgrade_preserves_snapshot(before, changed))
        self.assertFalse(pg.dsn_upgrade_preserves_snapshot(before, before))

    def test_old_schema_and_additive_defaults_are_strict(self):
        baseline = {"main": {"columns": [{"table_name": "Books", "column_name": "Id"}]}}
        self.assertTrue(pg.schema_has_no_later_additions(baseline))
        additions = [{"table_name": table, "column_name": column, "data_type": contract[0],
                      "is_nullable": contract[1], "column_default": contract[2]}
                     for (table, column), contract in pg.ADDED_COLUMN_CONTRACT.items()]
        migrated = {"main": {"columns": [*baseline["main"]["columns"], *additions]}}
        self.assertFalse(pg.schema_has_no_later_additions(migrated))
        self.assertTrue(pg.expected_migration_column_contract(migrated))
        for field, value in (("data_type", "bigint"), ("is_nullable", "YES"), ("column_default", "1")):
            with self.subTest(field=field):
                changed = deepcopy(migrated)
                changed["main"]["columns"][1][field] = value
                self.assertFalse(pg.expected_migration_column_contract(changed))
        self.assertFalse(pg.expected_migration_column_contract(baseline))

    def test_replacing_component_configuration_removes_presence_not_just_values(self):
        fixture = SimpleNamespace(home=self.root)
        (self.root / "config.xml").write_text("<Config><ApiKey>owned-key</ApiKey><PostgresHost></PostgresHost>"
                                             "<PostgresPort>0</PostgresPort><InstanceName>Owned</InstanceName></Config>")
        def set_xml(values):
            tree = ET.parse(self.root / "config.xml")
            for key, value in values.items():
                ET.SubElement(tree.getroot(), key).text = value
            tree.write(self.root / "config.xml")
        fixture.set_xml = set_xml
        pg.replace_postgres(fixture, {"PostgresMainDbConnectionString": 'Password="owned;value"'})
        tree = ET.parse(self.root / "config.xml")
        self.assertIsNone(tree.find("PostgresHost"))
        self.assertIsNone(tree.find("PostgresPort"))
        self.assertEqual(tree.findtext("ApiKey"), "owned-key")
        self.assertEqual(tree.findtext("PostgresMainDbConnectionString"), 'Password="owned;value"')
        self.assertEqual((self.root / "config.xml").stat().st_mode & 0o777, 0o600)


if __name__ == "__main__":
    unittest.main()
