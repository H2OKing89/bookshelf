using System;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    [NonParallelizable]
    public class SQLiteMigrationRebuildFixture : MigrationCompatibilityTestBase
    {
        public SQLiteMigrationRebuildFixture()
            : base(DatabaseType.SQLite)
        {
        }

        [Test]
        public void rebuilding_should_preserve_constraints_partial_expression_indexes_triggers_and_autoincrement_history()
        {
            Migrate(MigrationType.Main, 1);
            using (var connection = Open())
            {
                connection.Execute(@"CREATE TABLE ""P12Parent"" (""Id"" INTEGER PRIMARY KEY);
                    CREATE TABLE ""P12Audit"" (""RecordId"" INTEGER NOT NULL, ""Title"" TEXT NOT NULL);
                    CREATE TABLE ""P12Recording"" (
                        ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                        ""ParentId"" INTEGER NOT NULL REFERENCES ""P12Parent"" (""Id""),
                        ""Title"" TEXT COLLATE NOCASE NOT NULL DEFAULT 'keep, (value)' CHECK (length(""Title"") > 0),
                        ""Attempts"" INTEGER NOT NULL DEFAULT 0 CHECK (""Attempts"" >= 0),
                        ""Scratch"" TEXT,
                        CONSTRAINT ""UQ_P12ParentTitle"" UNIQUE (""ParentId"", ""Title""),
                        CONSTRAINT ""CK_P12Attempts"" CHECK (""Attempts"" < 100));
                    CREATE INDEX ""IX_P12Recording_Title"" ON ""P12Recording"" (""Title"" DESC);
                    CREATE INDEX ""IX_P12Recording_PartialExpression"" ON ""P12Recording"" (lower(""Title"")) WHERE ""Attempts"" > 0;
                    CREATE VIEW ""P12RecordingView"" AS SELECT ""Id"", ""Title"" FROM ""P12Recording"";
                    CREATE TRIGGER ""TR_P12Recording_Audit"" AFTER INSERT ON ""P12Recording""
                        BEGIN INSERT INTO ""P12Audit"" (""RecordId"", ""Title"") VALUES (NEW.""Id"", NEW.""Title""); END;
                    INSERT INTO ""P12Parent"" VALUES (5);
                    INSERT INTO ""P12Recording"" (""Id"", ""ParentId"", ""Title"", ""Attempts"", ""Scratch"") VALUES (7, 5, 'Mixed Case', 1, 'unused');
                    UPDATE sqlite_sequence SET seq = 500 WHERE name = 'P12Recording';");
            }

            Migrate(MigrationType.Main, 2, migration => migration.Rename.Column("Title").OnTable("P12Recording").To("RecordingTitle"));
            Migrate(MigrationType.Main, 3, migration => migration.Delete.Column("Scratch").FromTable("P12Recording"));
            Migrate(MigrationType.Main, 4, migration => migration.Alter.Table("P12Recording").AlterColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(9));

            using (var connection = Open())
            {
                connection.Execute("PRAGMA foreign_keys = ON");
                connection.QuerySingle<string>("SELECT \"RecordingTitle\" FROM \"P12Recording\" WHERE \"Id\" = 7").Should().Be("Mixed Case");
                connection.QuerySingle<string>("SELECT \"RecordingTitle\" FROM \"P12RecordingView\" WHERE \"Id\" = 7").Should().Be("Mixed Case");
                connection.QuerySingle<int>("SELECT \"Attempts\" FROM \"P12Recording\" WHERE \"Id\" = 7").Should().Be(1);
                connection.QuerySingle<long>("SELECT seq FROM sqlite_sequence WHERE name = 'P12Recording'").Should().Be(500);
                connection.QuerySingle<string>("SELECT sql FROM sqlite_master WHERE name = 'IX_P12Recording_PartialExpression'")
                    .Should().Contain("lower(").And.Contain("WHERE").And.Contain("RecordingTitle");
                connection.QuerySingle<string>("SELECT sql FROM sqlite_master WHERE name = 'IX_P12Recording_Title'")
                    .Should().Contain("DESC").And.Contain("RecordingTitle");
                Columns(connection, "P12Recording").Should().NotContain("Title").And.NotContain("Scratch");

                connection.Execute("INSERT INTO \"P12Recording\" (\"ParentId\", \"RecordingTitle\") VALUES (5, 'New recording')");
                connection.QuerySingle<long>("SELECT \"Id\" FROM \"P12Recording\" WHERE \"RecordingTitle\" = 'New recording'").Should().Be(501);
                connection.QuerySingle<int>("SELECT \"Attempts\" FROM \"P12Recording\" WHERE \"Id\" = 501").Should().Be(9);
                connection.QuerySingle<string>("SELECT \"Title\" FROM \"P12Audit\" WHERE \"RecordId\" = 501").Should().Be("New recording");
                connection.QuerySingle<int>("SELECT COUNT(*) FROM \"P12Audit\"").Should().Be(2, "copying existing rows must not replay their insert trigger");

                Assert.Throws<SQLiteException>(() => connection.Execute("INSERT INTO \"P12Recording\" (\"ParentId\", \"RecordingTitle\") VALUES (5, 'mixed case')"));
                Assert.Throws<SQLiteException>(() => connection.Execute("INSERT INTO \"P12Recording\" (\"ParentId\", \"RecordingTitle\") VALUES (999, 'Orphan')"));
                Assert.Throws<SQLiteException>(() => connection.Execute("INSERT INTO \"P12Recording\" (\"ParentId\", \"RecordingTitle\", \"Attempts\") VALUES (5, 'Negative', -1)"));
                Assert.Throws<SQLiteException>(() => connection.Execute("INSERT INTO \"P12Recording\" (\"ParentId\", \"RecordingTitle\", \"Attempts\") VALUES (5, 'Too many', 100)"));
                Assert.Throws<SQLiteException>(() => connection.Execute("INSERT INTO \"P12Recording\" (\"ParentId\", \"RecordingTitle\") VALUES (5, '')"));
                AssertIntegrity(connection);
            }
        }

        [Test]
        public void rebuilding_an_empty_autoincrement_table_should_not_reuse_previously_issued_ids()
        {
            Migrate(MigrationType.Main, 1);
            using (var connection = Open())
            {
                connection.Execute(@"CREATE TABLE ""P12EmptySequence"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Attempts"" INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO ""P12EmptySequence"" (""Id"") VALUES (500);
                    DELETE FROM ""P12EmptySequence"";");
            }

            Migrate(MigrationType.Main, 2, migration => migration.Alter.Table("P12EmptySequence").AlterColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(9));

            using (var connection = Open())
            {
                connection.Execute("INSERT INTO \"P12EmptySequence\" DEFAULT VALUES");
                connection.QuerySingle<long>("SELECT \"Id\" FROM \"P12EmptySequence\"").Should().Be(501);
                connection.QuerySingle<int>("SELECT \"Attempts\" FROM \"P12EmptySequence\"").Should().Be(9);
                AssertIntegrity(connection);
            }
        }

        [Test]
        public void named_and_null_defaults_with_quoted_punctuation_should_survive_column_changes()
        {
            Migrate(MigrationType.Main, 1);
            using (var connection = Open())
            {
                connection.Execute(@"CREATE TABLE ""P12Defaults"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Note"" TEXT CONSTRAINT ""DF_P12Null"" DEFAULT NULL,
                    ""Label"" TEXT CONSTRAINT ""DF_P12Label"" DEFAULT 'literal; , ''quote'' (value)' COLLATE NOCASE,
                    ""Attempts"" INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO ""P12Defaults"" (""Note"") VALUES (NULL);");
            }

            Migrate(MigrationType.Main, 2, migration =>
            {
                migration.Alter.Table("P12Defaults").AlterColumn("Note").AsString(500).Nullable();
                migration.Alter.Table("P12Defaults").AlterColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(9);
            });

            using (var connection = Open())
            {
                connection.Execute("INSERT INTO \"P12Defaults\" DEFAULT VALUES");
                connection.Query<string>("SELECT \"Label\" FROM \"P12Defaults\" ORDER BY \"Id\"")
                    .Should().Equal("literal; , 'quote' (value)", "literal; , 'quote' (value)");
                connection.Query<int?>("SELECT \"Note\" FROM \"P12Defaults\"").Should().OnlyContain(value => value == null);
                connection.Query<int>("SELECT \"Attempts\" FROM \"P12Defaults\" ORDER BY \"Id\"").Should().Equal(0, 9);
                connection.QuerySingle<string>("SELECT sql FROM sqlite_master WHERE name = 'P12Defaults'").Should().Contain("DF_P12Label");
                AssertIntegrity(connection);
            }
        }

        [Test]
        public void stored_generated_columns_should_be_recomputed_without_being_inserted_during_rebuild()
        {
            Migrate(MigrationType.Main, 1);
            using (var connection = Open())
            {
                connection.Execute(@"CREATE TABLE ""P12Computed"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL,
                    ""Folded"" TEXT GENERATED ALWAYS AS (lower(""Name"")) STORED,
                    ""Attempts"" INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO ""P12Computed"" (""Name"") VALUES ('Mixed Case');");
            }

            Migrate(MigrationType.Main, 2, migration => migration.Alter.Table("P12Computed").AlterColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(9));

            using (var connection = Open())
            {
                connection.QuerySingle<string>("SELECT \"Folded\" FROM \"P12Computed\"").Should().Be("mixed case");
                connection.Execute("UPDATE \"P12Computed\" SET \"Name\" = 'Updated'");
                connection.QuerySingle<string>("SELECT \"Folded\" FROM \"P12Computed\"").Should().Be("updated");
                connection.Execute("INSERT INTO \"P12Computed\" (\"Name\") VALUES ('NEW')");
                connection.QuerySingle<string>("SELECT \"Folded\" FROM \"P12Computed\" WHERE \"Id\" = 2").Should().Be("new");
                connection.QuerySingle<int>("SELECT \"Attempts\" FROM \"P12Computed\" WHERE \"Id\" = 2").Should().Be(9);
                AssertIntegrity(connection);
            }
        }

        [Test]
        public void dropping_a_referenced_index_column_should_fail_without_silently_removing_the_index_or_data()
        {
            Migrate(MigrationType.Main, 1);
            string originalSchema;
            using (var connection = Open())
            {
                connection.Execute(@"CREATE TABLE ""P12Indexed"" (""Id"" INTEGER PRIMARY KEY, ""Keep"" TEXT, ""Indexed"" TEXT);
                    CREATE UNIQUE INDEX ""IX_P12Indexed"" ON ""P12Indexed"" (""Indexed"");
                    INSERT INTO ""P12Indexed"" VALUES (1, 'owned', 'retained');");
                originalSchema = connection.QuerySingle<string>("SELECT sql FROM sqlite_master WHERE name = 'P12Indexed'");
            }

            Assert.Catch(() => Migrate(MigrationType.Main, 2, migration => migration.Delete.Column("Indexed").FromTable("P12Indexed")));

            using (var connection = Open())
            {
                connection.QuerySingle<string>("SELECT sql FROM sqlite_master WHERE name = 'P12Indexed'").Should().Be(originalSchema);
                connection.QuerySingle<string>("SELECT \"Indexed\" FROM \"P12Indexed\"").Should().Be("retained");
                connection.QuerySingle<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_P12Indexed'").Should().Be(1);
                connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\"").Should().Equal(1);
                AssertIntegrity(connection);
            }

            ExceptionVerification.IgnoreErrors();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void rebuilding_an_incoming_foreign_key_target_with_enforcement_enabled_should_fail_before_changing_data(bool selfReferenced)
        {
            Migrate(MigrationType.Main, 1);
            using (var connection = Open())
            {
                if (selfReferenced)
                {
                    connection.Execute(@"CREATE TABLE ""P12Referenced"" (
                        ""Id"" INTEGER PRIMARY KEY, ""Name"" TEXT NOT NULL,
                        ""ParentId"" INTEGER REFERENCES ""P12Referenced"" (""Id"") ON DELETE CASCADE);
                        INSERT INTO ""P12Referenced"" VALUES (1, 'owned', NULL), (2, 'child', 1);");
                }
                else
                {
                    connection.Execute(@"CREATE TABLE ""P12Referenced"" (""Id"" INTEGER PRIMARY KEY, ""Name"" TEXT NOT NULL);
                        CREATE TABLE ""P12Child"" (""Id"" INTEGER PRIMARY KEY, ""ParentId"" INTEGER REFERENCES ""P12Referenced"" (""Id"") ON DELETE CASCADE);
                        INSERT INTO ""P12Referenced"" VALUES (1, 'owned');
                        INSERT INTO ""P12Child"" VALUES (2, 1);");
                }
            }

            var enforce = new SQLiteConnectionStringBuilder(ConnectionString) { ForeignKeys = true }.ConnectionString;
            var error = Assert.Catch(() => Subject.Migrate(
                enforce,
                new MigrationContext(MigrationType.Main, 2)
                {
                    BeforeMigration = migration =>
                    {
                        if (migration.Version == 2)
                        {
                            migration.Alter.Table("P12Referenced").AlterColumn("Name").AsString(200).NotNullable();
                        }
                    }
                },
                DatabaseType.SQLite));

            error.ToString().ToLowerInvariant().Should().Contain("foreign key");
            using (var connection = Open())
            {
                connection.QuerySingle<string>("SELECT \"Name\" FROM \"P12Referenced\" WHERE \"Id\" = 1").Should().Be("owned");
                connection.QuerySingle<int>(selfReferenced
                    ? "SELECT \"ParentId\" FROM \"P12Referenced\" WHERE \"Id\" = 2"
                    : "SELECT \"ParentId\" FROM \"P12Child\"").Should().Be(1);
                connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\"").Should().Equal(1);
                AssertIntegrity(connection);
            }

            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void parallel_migrations_should_keep_main_and_log_contexts_isolated()
        {
            var logConnectionString = SqliteConnectionString(GetTempFilePath());
            using var barrier = new Barrier(2);
            var controller = Subject;
            var main = new MigrationContext(MigrationType.Main, 1)
            {
                BeforeMigration = migration =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                    MigrationContext.Current.MigrationType.Should().Be(MigrationType.Main);
                }
            };
            var log = new MigrationContext(MigrationType.Log, 1)
            {
                BeforeMigration = migration =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                    MigrationContext.Current.MigrationType.Should().Be(MigrationType.Log);
                }
            };

            Task.WaitAll(Task.Run(() => controller.Migrate(ConnectionString, main, DatabaseType.SQLite)),
                Task.Run(() => controller.Migrate(logConnectionString, log, DatabaseType.SQLite)));

            using var mainConnection = Open();
            using var logConnection = new SQLiteConnection(logConnectionString);
            logConnection.Open();
            Tables(mainConnection).Should().Contain("Books").And.NotContain("Logs");
            Tables(logConnection).Should().Contain("Logs").And.NotContain("Books");
        }
    }
}
