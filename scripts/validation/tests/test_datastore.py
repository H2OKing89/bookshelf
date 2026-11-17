from contextlib import closing
import csv
import io
import json
from pathlib import Path
import sqlite3
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from validation import datastore
from validation.common import ValidationFailure
from validation.tests.test_common import package


class DatastoreFixtureValueTests(unittest.TestCase):
    def test_typed_fixture_values_keep_quotes_json_nulls_and_false(self):
        self.assertEqual(datastore.literal("O'Neil"), "'O''Neil'")
        self.assertEqual(datastore.literal('["a","b"]'), "'[\"a\",\"b\"]'")
        self.assertEqual(datastore.literal(None), "NULL")
        self.assertEqual(datastore.literal(0, "boolean"), "FALSE")
        self.assertEqual(datastore.literal(1, "boolean"), "TRUE")
        self.assertEqual(datastore.literal(17), "17")
        for invalid in ("False", "0", 2):
            with self.subTest(invalid=invalid), self.assertRaises(ValidationFailure):
                datastore.literal(invalid, "boolean")

    def test_fixture_names_and_null_bytes_cannot_become_sql(self):
        self.assertEqual(datastore.identifier("AudiobookMetadataOverrides"), '"AudiobookMetadataOverrides"')
        for invalid in ('Users";DROP TABLE "Books', "public.Users", "", None):
            with self.subTest(invalid=invalid), self.assertRaises(ValidationFailure):
                datastore.identifier(invalid)
        with self.assertRaises(ValidationFailure):
            datastore.literal("owned\x00fixture")

    def test_mount_paths_are_csv_arguments_without_shell_interpolation(self):
        value = datastore.mount(Path('/tmp/owned, "quoted"; fixture'), "/validation/data", readonly=True)
        self.assertEqual(next(csv.reader(io.StringIO(value))),
                         ["type=bind", 'src=/tmp/owned, "quoted"; fixture', "dst=/validation/data", "readonly"])
        for invalid in ("/tmp/owned\nfixture", "/tmp/owned\rfixture"):
            with self.subTest(invalid=invalid), self.assertRaises(ValidationFailure):
                datastore.mount(Path(invalid), "/validation/data")

    def test_historical_migration_158_is_preserved_without_inventing_34(self):
        self.assertEqual(len(datastore.HISTORY), 48)
        self.assertIn(158, datastore.HISTORY)
        self.assertNotIn(34, datastore.HISTORY)
        self.assertIn(48, datastore.HISTORY)

    def test_dump_restore_attnum_compaction_preserves_visible_schema_order(self):
        before = [
            {"table_name": "Books", "column_name": "Id", "ordinal_position": 1, "data_type": "integer", "column_default": "owned_sequence"},
            {"table_name": "Books", "column_name": "Title", "ordinal_position": 4, "data_type": "text", "column_default": None},
            {"table_name": "Editions", "column_name": "Id", "ordinal_position": 1, "data_type": "integer", "column_default": None},
        ]
        restored = [dict(before[0]), {**before[1], "ordinal_position": 2}, dict(before[2])]
        self.assertEqual(datastore.logical_columns(before), datastore.logical_columns(restored))
        self.assertEqual(before[1]["ordinal_position"], 4, "normalization must not mutate saved evidence")

    def test_visible_schema_reordering_type_default_or_nullability_changes_still_fail(self):
        columns = [
            {"table_name": "Books", "column_name": "Id", "ordinal_position": 1, "data_type": "integer", "is_nullable": "NO", "column_default": "owned_sequence"},
            {"table_name": "Books", "column_name": "Title", "ordinal_position": 4, "data_type": "text", "is_nullable": "YES", "column_default": None},
        ]
        expected = datastore.logical_columns(columns)
        self.assertNotEqual(expected, datastore.logical_columns(list(reversed(columns))))
        for field, value in (("data_type", "bigint"), ("column_default", "unexpected_sequence"), ("is_nullable", "YES")):
            with self.subTest(field=field):
                changed = [{**columns[0], field: value}, dict(columns[1])]
                self.assertNotEqual(expected, datastore.logical_columns(changed))


class DatastoreOwnedPostgresTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.postgres = datastore.OwnedPostgres(self.root / "postgres")
        self.postgres.output.mkdir()
        self.postgres.socket = self.root / "socket"
        self.postgres.socket.mkdir(mode=0o700)
        self.postgres.container = self.postgres.name
        self.postgres.environment = {}

    def test_unowned_or_unverifiable_containers_and_sockets_are_never_removed(self):
        with (patch.object(datastore.docker, "_state", return_value={"Running": True}),
              patch.object(datastore.docker, "_owned", return_value=False),
              patch.object(self.postgres, "command") as command,
              self.assertRaises(ValidationFailure) as error):
            self.postgres.close()
        self.assertEqual(error.exception.code, "datastore_refuses_unowned_container_cleanup")
        command.assert_not_called()
        self.assertTrue(self.postgres.socket.is_dir())
        with (patch.object(datastore.docker, "_state", side_effect=ValidationFailure("synthetic_state_unknown")),
              patch.object(self.postgres, "command") as command,
              self.assertRaises(ValidationFailure)):
            self.postgres.close()
        command.assert_not_called()
        self.assertTrue(self.postgres.socket.is_dir())

    def test_interrupted_create_can_clean_only_its_recorded_name_and_owner(self):
        calls = []
        def command(arguments, **kwargs):
            calls.append(arguments)
            return subprocess.CompletedProcess(arguments, 0, "synthetic owned logs", "")
        with (patch.object(datastore.docker, "_state", side_effect=[{"Running": True}, None]),
              patch.object(datastore.docker, "_owned", return_value=True) as owned,
              patch.object(self.postgres, "command", side_effect=command)):
            self.assertTrue(self.postgres.close())
        owned.assert_called_once_with(self.postgres.name, self.postgres.owner, {})
        self.assertEqual(calls[-1], ["docker", "rm", "--force", "--volumes", self.postgres.name])
        self.assertIsNone(self.postgres.socket)
        self.assertIsNone(self.postgres.container)

    def test_absent_recorded_container_does_not_trigger_discovery_or_removal(self):
        with patch.object(datastore.docker, "_state", return_value=None), patch.object(self.postgres, "command") as command:
            self.assertTrue(self.postgres.close())
        command.assert_not_called()
        self.assertIsNone(self.postgres.socket)

    def test_failed_log_collection_still_removes_the_owned_container_and_socket(self):
        calls = []
        def command(arguments, **kwargs):
            calls.append(arguments)
            if arguments[1] == "logs":
                raise subprocess.TimeoutExpired(arguments, 10)
            return subprocess.CompletedProcess(arguments, 0, "", "")
        with (patch.object(datastore.docker, "_state", side_effect=[{"Running": True}, None]),
              patch.object(datastore.docker, "_owned", return_value=True),
              patch.object(self.postgres, "command", side_effect=command)):
            self.assertTrue(self.postgres.close())
        self.assertEqual(calls[-1], ["docker", "rm", "--force", "--volumes", self.postgres.name])
        self.assertIsNone(self.postgres.socket)
        self.assertIsNone(self.postgres.container)

    def test_failed_log_write_still_removes_the_owned_container_and_socket(self):
        with (patch.object(datastore.docker, "_state", side_effect=[{"Running": True}, None]),
              patch.object(datastore.docker, "_owned", return_value=True),
              patch.object(self.postgres, "command", return_value=subprocess.CompletedProcess([], 0, "owned logs", "")) as command,
              patch.object(Path, "write_text", side_effect=OSError("synthetic diagnostic failure"))):
            self.assertTrue(self.postgres.close())
        self.assertEqual(command.call_args.args[0], ["docker", "rm", "--force", "--volumes", self.postgres.name])
        self.assertIsNone(self.postgres.socket)
        self.assertIsNone(self.postgres.container)

    def test_failed_removal_retains_ownership_and_socket_for_a_successful_retry(self):
        removals = 0
        def command(arguments, **kwargs):
            nonlocal removals
            if arguments[1] == "rm":
                removals += 1
                return subprocess.CompletedProcess(arguments, 1 if removals == 1 else 0, "", "")
            return subprocess.CompletedProcess(arguments, 0, "owned logs", "")
        with (patch.object(datastore.docker, "_state", side_effect=[{"Running": True}, {"Running": True}, None]),
              patch.object(datastore.docker, "_owned", return_value=True),
              patch.object(self.postgres, "command", side_effect=command)):
            self.assertFalse(self.postgres.close())
            self.assertEqual(self.postgres.container, self.postgres.name)
            self.assertTrue(self.postgres.socket.is_dir())
            self.assertTrue(self.postgres.close())
        self.assertEqual(removals, 2)
        self.assertIsNone(self.postgres.socket)
        self.assertIsNone(self.postgres.container)


class DatastoreBaselineTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def runtime(self, name, version):
        runtime = package(self.root / name)
        (runtime / "Readarr.deps.json").write_text(json.dumps({"libraries": {"FluentMigrator.Runner.Core/" + version: {}}}))
        return runtime

    def test_baseline_is_explicit_before_any_docker_or_application_work(self):
        with patch.object(datastore.OwnedPostgres, "start") as start, self.assertRaises(ValidationFailure) as error:
            datastore.run(self.root / "candidate", self.root / "evidence")
        self.assertEqual(error.exception.code, "datastore_requires_explicit_pre_engine_baseline")
        start.assert_not_called()
        self.assertFalse((self.root / "evidence").exists())

    def test_seed_transfer_copies_only_owned_log_and_cache_markers(self):
        for filename, table, field in (("logs.db", "Logs", "Message"), ("cache.db", "HttpResponse", "Url")):
            with closing(sqlite3.connect(self.root / filename)) as database, database:
                database.execute('CREATE TABLE "' + table + '" ("Id" INTEGER PRIMARY KEY,"' + field + '" TEXT)')
                database.execute('INSERT INTO "' + table + '" VALUES (1, ?)', ("startup-must-not-transfer",))
                database.execute('INSERT INTO "' + table + '" VALUES (999, ?)', ("owned-marker",))
        with closing(sqlite3.connect(self.root / "readarr.db")) as database, database:
            database.execute('CREATE TABLE "Config" ("Key" TEXT,"Value" TEXT)')
        statements = []
        postgres = SimpleNamespace(
            sql=lambda name, sql: statements.append(sql),
            json=lambda name, query: {"Id": "integer", "Message": "text", "Url": "text"},
        )
        with patch.object(datastore, "TABLES", ()):
            datastore.transfer_seed(postgres, {kind: kind for kind in datastore.DATABASE_KINDS}, SimpleNamespace(home=self.root))
        sql = "\n".join(statements)
        self.assertNotIn("startup-must-not-transfer", sql)
        self.assertEqual(sql.count("owned-marker"), 2)
        self.assertEqual(sql.count("VALUES (999,"), 2)

    def test_two_new_engine_binaries_do_not_count_as_a_genuine_engine_upgrade(self):
        candidate = self.runtime("candidate", "8.0.1")
        baseline = self.runtime("baseline", "8.0.0")
        with (patch.object(datastore, "ensure_host_compatible"), patch.object(datastore.OwnedPostgres, "start") as start,
              self.assertRaises(ValidationFailure) as error):
            datastore.run(candidate, self.root / "evidence", baseline)
        self.assertEqual(error.exception.code, "datastore_baseline_requires_pre_fluentmigrator_8_binary")
        start.assert_not_called()
        self.assertFalse((self.root / "evidence").exists())

    def test_candidate_must_contain_the_new_engine_before_starting_postgres(self):
        candidate = self.runtime("candidate", "3.3.2.9")
        baseline = self.runtime("baseline", "3.3.2.9")
        with (patch.object(datastore, "ensure_host_compatible"), patch.object(datastore.OwnedPostgres, "start") as start,
              self.assertRaises(ValidationFailure) as error):
            datastore.run(candidate, self.root / "evidence", baseline)
        self.assertEqual(error.exception.code, "datastore_candidate_requires_fluentmigrator_8")
        start.assert_not_called()


if __name__ == "__main__":
    unittest.main()
