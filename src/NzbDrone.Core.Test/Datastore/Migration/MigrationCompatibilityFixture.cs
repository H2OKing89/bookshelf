using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture(DatabaseType.SQLite)]
    [TestFixture(DatabaseType.PostgreSQL)]
    [NonParallelizable]
    public class MigrationCompatibilityFixture : MigrationCompatibilityTestBase
    {
        private static readonly long[] HistoricalVersions = Enumerable.Range(1, 48)
            .Where(version => version != 34).Select(version => (long)version).Concat(new long[] { 158, 159, 160 }).ToArray();

        public MigrationCompatibilityFixture(DatabaseType databaseType)
            : base(databaseType)
        {
        }

        [TestCase(MigrationType.Main, "Books")]
        [TestCase(MigrationType.Log, "Logs")]
        [TestCase(MigrationType.Cache, "HttpResponse")]
        public void all_historical_versions_should_replay_in_each_database_without_importing_tv_schema(MigrationType type, string expectedTable)
        {
            Migrate(type);

            using var connection = Open();
            connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\"")
                .Should().Equal(HistoricalVersions, "migration 034 is historically numbered 158 and must retain its applied identity");
            Tables(connection).Should().Contain(expectedTable).And.NotContain("Episodes").And.NotContain("Seasons");
            if (type != MigrationType.Main)
            {
                Tables(connection).Should().NotContain("Books").And.NotContain("Authors");
            }

            AssertIntegrity(connection);
        }

        [TestCase(1)]
        [TestCase(21)]
        [TestCase(41)]
        [TestCase(44)]
        [TestCase(46)]
        [TestCase(47)]
        [TestCase(158)]
        public void older_schema_checkpoints_should_preserve_recording_identity_parts_history_settings_and_dates(int checkpoint)
        {
            Migrate(MigrationType.Main, checkpoint);
            Dictionary<string, string[]> before;
            using (var connection = Open())
            {
                SeedLibrary(connection);
                before = LibrarySnapshot(connection);
            }

            Migrate(MigrationType.Main);

            using (var connection = Open())
            {
                LibrarySnapshot(connection).Should().BeEquivalentTo(before);
                connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\"").Should().Equal(HistoricalVersions);
                connection.Query<long>("SELECT \"Id\" FROM \"BookFiles\" ORDER BY \"Id\"").Should().Equal(19, 20);
                connection.Query<int>("SELECT \"Part\" FROM \"BookFiles\" ORDER BY \"Id\"")
                    .Should().Equal(checkpoint < 10 ? new[] { 1, 1 } : new[] { 1, 2 });
                connection.Query<string>("SELECT \"Classifications\" FROM \"Books\"").Should().OnlyContain(value => value == "[]");
                connection.Query<string>("SELECT \"SimilarAuthors\" FROM \"AuthorMetadata\"").Should().OnlyContain(value => value == "[]");
                connection.Query<string>("SELECT \"Language\" FROM \"Editions\"").Should().OnlyContain(value => value == "eng");
                connection.Query<string>("SELECT \"Narrators\" FROM \"Editions\"").Should()
                    .OnlyContain(value => value == (checkpoint < 42 ? "[]" : "[\"Synthetic Narrator\"]"));
                connection.Query("SELECT \"Salt\", \"Iterations\" FROM \"Users\"").Should().ContainSingle();
                AssertIntegrity(connection);
            }
        }

        [Test]
        public void rename_drop_and_default_changes_should_preserve_values_unique_indexes_and_generated_ids()
        {
            Migrate(MigrationType.Main, 1);
            Migrate(MigrationType.Main, 2, migration =>
            {
                migration.Create.Table("P12Recording")
                    .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                    .WithColumn("OldTitle").AsString().NotNullable()
                    .WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
                    .WithColumn("Scratch").AsString().Nullable();
                migration.Create.Index("IX_P12Recording_Title").OnTable("P12Recording").OnColumn("OldTitle").Ascending().WithOptions().Unique();
                migration.Insert.IntoTable("P12Recording").Row(new { OldTitle = "Owned recording", Attempts = 2, Scratch = "discard" });
            });
            Migrate(MigrationType.Main, 3, migration => migration.Rename.Column("OldTitle").OnTable("P12Recording").To("Title"));
            Migrate(MigrationType.Main, 4, migration => migration.Delete.Column("Scratch").FromTable("P12Recording"));
            Migrate(MigrationType.Main, 5, migration => migration.Alter.Table("P12Recording").AlterColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(9));

            using var connection = Open();
            connection.QuerySingle<string>("SELECT \"Title\" FROM \"P12Recording\" WHERE \"Id\" = 1").Should().Be("Owned recording");
            connection.QuerySingle<int>("SELECT \"Attempts\" FROM \"P12Recording\" WHERE \"Id\" = 1").Should().Be(2);
            Columns(connection, "P12Recording").Should().NotContain("OldTitle").And.NotContain("Scratch");
            Assert.Catch(() => connection.Execute("INSERT INTO \"P12Recording\" (\"Title\") VALUES ('Owned recording')"));
            connection.Execute("INSERT INTO \"P12Recording\" (\"Title\") VALUES ('New recording')");
            connection.QuerySingle<int>("SELECT \"Attempts\" FROM \"P12Recording\" WHERE \"Title\" = 'New recording'").Should().Be(9);
            connection.QuerySingle<long>("SELECT \"Id\" FROM \"P12Recording\" WHERE \"Title\" = 'New recording'").Should().BeGreaterThan(1);
            AssertIntegrity(connection);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void datetimeoffset_and_guid_columns_should_keep_the_same_instant_and_identity(bool offsetSource)
        {
            Migrate(MigrationType.Main, 1);
            Migrate(MigrationType.Main, 2, migration => migration.Create.Table("P12TypedValues")
                .WithColumn("Id").AsInt32().PrimaryKey()
                .WithColumn("Identity").AsGuid().NotNullable()
                .WithColumn("When").AsDateTimeOffset().NotNullable()
                .WithColumn("Optional").AsDateTimeOffset().Nullable());
            var identity = Guid.Parse("3375ca62-96d5-4e58-a902-d286d890c425");
            var instant = new DateTime(2020, 2, 29, 23, 45, 12, DateTimeKind.Utc).AddTicks(3456780);
            using (var connection = Open())
            {
                var value = offsetSource
                    ? DatabaseType == DatabaseType.SQLite
                        ? "'2020-03-01 05:30:12.345678+05:45'"
                        : "TIMESTAMP WITH TIME ZONE '2020-03-01 05:30:12.345678+05:45'"
                    : "@instant";
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO \"P12TypedValues\" (\"Id\", \"Identity\", \"When\", \"Optional\") VALUES (1, @identity, " + value + ", NULL)";
                var identityParameter = command.CreateParameter();
                identityParameter.ParameterName = "@identity";
                identityParameter.DbType = DbType.Guid;
                identityParameter.Value = identity;
                command.Parameters.Add(identityParameter);
                if (!offsetSource)
                {
                    var instantParameter = command.CreateParameter();
                    instantParameter.ParameterName = "@instant";
                    instantParameter.DbType = DbType.DateTime;
                    instantParameter.Value = instant;
                    command.Parameters.Add(instantParameter);
                }

                command.ExecuteNonQuery();
            }

            Migrate(MigrationType.Main, 3);

            using (var connection = Open())
            {
                // Native GUID columns exercise the migration type map; application
                // identifiers keep their existing text converter and AsString schema.
                var storedIdentity = connection.ExecuteScalar<object>("SELECT \"Identity\" FROM \"P12TypedValues\"");
                (storedIdentity is Guid native ? native : Guid.Parse((string)storedIdentity)).Should().Be(identity);
                var value = connection.QuerySingle<DateTime>("SELECT \"When\" FROM \"P12TypedValues\"");
                DateTime.SpecifyKind(value, DateTimeKind.Utc).Should().Be(instant);
                connection.QuerySingle<DateTime?>("SELECT \"Optional\" FROM \"P12TypedValues\"").Should().BeNull();
                if (DatabaseType == DatabaseType.SQLite)
                {
                    connection.QuerySingle<string>("SELECT type FROM pragma_table_info('P12TypedValues') WHERE name = 'When'")
                        .Should().Be("DATETIME", "DateTimeOffset must retain the existing SQLite date representation");
                }
                else
                {
                    connection.QuerySingle<string>("SELECT data_type FROM information_schema.columns WHERE table_name = 'P12TypedValues' AND column_name = 'When'")
                        .Should().Be("timestamp with time zone");
                }
            }
        }

        [Test]
        public void a_failed_migration_should_rollback_its_ddl_and_rows_restore_context_and_allow_retry()
        {
            Migrate(MigrationType.Main, 1);
            var outer = new MigrationContext(MigrationType.Cache);
            MigrationContext.Current = outer;

            var error = Assert.Catch(() => Migrate(MigrationType.Main, 2, migration =>
            {
                migration.Create.Table("P12RolledBack").WithColumn("Id").AsInt32().PrimaryKey();
                migration.Insert.IntoTable("Config").Row(new { Key = "p12_rollback_marker", Value = "must disappear" });
                migration.Execute.Sql("SELECT * FROM \"P12InjectedFailure\"");
            }));

            error.ToString().Should().Contain("P12InjectedFailure");
            MigrationContext.Current.Should().BeSameAs(outer);
            using (var connection = Open())
            {
                Tables(connection).Should().NotContain("P12RolledBack");
                connection.Query<int>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" = 'p12_rollback_marker'").Single().Should().Be(0);
                connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\"").Should().Equal(1);
            }

            Migrate(MigrationType.Main, 2);
            MigrationContext.Current.Should().BeSameAs(outer);
            using (var connection = Open())
            {
                connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\"").Should().Equal(1, 2);
                AssertIntegrity(connection);
            }

            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void successful_migration_should_restore_an_existing_outer_context()
        {
            var outer = new MigrationContext(MigrationType.Cache);
            MigrationContext.Current = outer;

            Migrate(MigrationType.Main, 1);

            MigrationContext.Current.Should().BeSameAs(outer);
        }

        private static void SeedLibrary(IDbConnection connection)
        {
            var stamp = new DateTime(2020, 2, 29, 23, 45, 12, DateTimeKind.Utc);
            Insert(connection, "AuthorMetadata", new Dictionary<string, object>
            {
                ["Id"] = 7, ["ForeignAuthorId"] = "audible:us:B000000011", ["TitleSlug"] = "p12-author",
                ["Name"] = "Synthetic Author", ["SortName"] = "synthetic author", ["NameLastFirst"] = "Author, Synthetic",
                ["SortNameLastFirst"] = "author, synthetic", ["Status"] = 0, ["Images"] = "[]", ["Aliases"] = "[]",
                ["Born"] = stamp, ["SimilarAuthors"] = "[]"
            });
            Insert(connection, "Authors", new Dictionary<string, object>
            {
                ["Id"] = 11, ["AuthorMetadataId"] = 7, ["CleanName"] = "synthetic author", ["Path"] = "/synthetic/audiobooks/author",
                ["Monitored"] = false, ["MetadataProfileId"] = 1, ["QualityProfileId"] = 1, ["Tags"] = "[]", ["Added"] = stamp,
                ["LastInfoSync"] = stamp, ["SortName"] = "synthetic author"
            });
            Insert(connection, "RootFolders", new Dictionary<string, object>
            {
                ["Id"] = 9, ["Path"] = "/synthetic/audiobooks", ["Name"] = "Synthetic media", ["IsCalibreLibrary"] = false,
                ["DefaultMetadataProfileId"] = 1, ["DefaultQualityProfileId"] = 1, ["DefaultMonitorOption"] = 0, ["DefaultTags"] = "[]"
            });
            for (var index = 0; index < 2; index++)
            {
                var region = index == 0 ? "us" : "uk";
                var recording = "audible:" + region + ":B00000001" + (index + 2);
                Insert(connection, "Books", new Dictionary<string, object>
                {
                    ["Id"] = 13 + index, ["AuthorMetadataId"] = 7, ["ForeignBookId"] = recording,
                    ["TitleSlug"] = "p12-recording-" + region, ["Title"] = "Synthetic recording " + region,
                    ["CleanTitle"] = "synthetic recording " + region, ["Monitored"] = index == 0, ["AnyEditionOk"] = false,
                    ["ReleaseDate"] = stamp, ["LastInfoSync"] = stamp, ["Added"] = stamp, ["Classifications"] = "[]"
                });
                Insert(connection, "Editions", new Dictionary<string, object>
                {
                    ["Id"] = 17 + index, ["BookId"] = 13 + index, ["ForeignEditionId"] = recording, ["Asin"] = "B00000001" + (index + 2),
                    ["Title"] = "Synthetic recording " + region, ["TitleSlug"] = "p12-edition-" + region,
                    ["Language"] = "eng", ["ReleaseDate"] = stamp, ["Images"] = "[]", ["Monitored"] = true, ["ManualAdd"] = true,
                    ["AudibleRegion"] = region, ["Narrators"] = "[\"Synthetic Narrator\"]", ["Contributors"] = "[]",
                    ["RuntimeLengthMinutes"] = 61, ["ReleaseDateIsPlaceholder"] = false
                });
                Insert(connection, "BookFiles", new Dictionary<string, object>
                {
                    ["Id"] = 19 + index, ["EditionId"] = 17, ["CalibreId"] = 0, ["Quality"] = "{\"quality\":17}",
                    ["Size"] = 3221225472L + index, ["DateAdded"] = stamp, ["Modified"] = stamp, ["ReleaseGroup"] = "Synthetic",
                    ["Path"] = "/synthetic/audiobooks/author/part-0" + (index + 1) + ".m4b", ["Part"] = index + 1,
                    ["MediaInfo"] = "{\"audioFormat\":\"AAC LC\"}"
                });
            }

            Insert(connection, "History", new Dictionary<string, object>
            {
                ["Id"] = 23, ["AuthorId"] = 11, ["BookId"] = 13, ["SourceTitle"] = "Synthetic import",
                ["Date"] = stamp, ["Quality"] = "{\"quality\":17}", ["Data"] = "{\"bookFileId\":\"19\"}", ["EventType"] = 3
            });
            Insert(connection, "Users", new Dictionary<string, object>
            {
                ["Id"] = 17, ["Identifier"] = "3375ca62-96d5-4e58-a902-d286d890c425", ["Username"] = "p12-synthetic-user", ["Password"] = "synthetic-legacy-hash"
            });
            Insert(connection, "Config", new Dictionary<string, object> { ["Id"] = 90001, ["Key"] = "p12-explicit-false", ["Value"] = "False" });
            Insert(connection, "Config", new Dictionary<string, object> { ["Id"] = 90002, ["Key"] = "p12-explicit-zero", ["Value"] = "0" });
            Insert(connection, "Config", new Dictionary<string, object> { ["Id"] = 90003, ["Key"] = "p12-explicit-empty", ["Value"] = "" });
            if (Tables(connection).Contains("AudiobookMetadataOverrides"))
            {
                Insert(connection, "AudiobookMetadataOverrides", new Dictionary<string, object>
                {
                    ["Id"] = 90001, ["Kind"] = "title", ["Region"] = "us", ["Asin"] = "B000000099", ["Name"] = "Owned correction",
                    ["AlternateAsins"] = "[]", ["Aliases"] = "[\"Synthetic Alias\"]", ["ReplacementTitle"] = "Owned title",
                    ["SourceUrl"] = "https://synthetic.invalid/p12", ["Reason"] = "Synthetic preservation fixture", ["Enabled"] = false,
                    ["UpdatedAt"] = stamp
                });
            }
        }

        private static Dictionary<string, string[]> LibrarySnapshot(IDbConnection connection)
        {
            var queries = new Dictionary<string, string>
            {
                ["AuthorMetadata"] = "SELECT \"Id\", \"ForeignAuthorId\", \"TitleSlug\", \"Name\", \"Born\", \"Aliases\" FROM \"AuthorMetadata\" WHERE \"Id\" = 7",
                ["Authors"] = "SELECT \"Id\", \"AuthorMetadataId\", \"CleanName\", \"Path\", \"Monitored\", \"MetadataProfileId\", \"QualityProfileId\", \"Tags\", \"Added\", \"LastInfoSync\" FROM \"Authors\" WHERE \"Id\" = 11",
                ["RootFolders"] = "SELECT \"Id\", \"Path\", \"Name\", \"DefaultMetadataProfileId\", \"DefaultQualityProfileId\", \"DefaultMonitorOption\", \"DefaultTags\", \"IsCalibreLibrary\" FROM \"RootFolders\" WHERE \"Id\" = 9",
                ["Books"] = "SELECT \"Id\", \"AuthorMetadataId\", \"ForeignBookId\", \"TitleSlug\", \"Title\", \"Monitored\", \"ReleaseDate\", \"Added\" FROM \"Books\" ORDER BY \"Id\"",
                ["Editions"] = "SELECT \"Id\", \"BookId\", \"ForeignEditionId\", \"Asin\", \"Title\", \"TitleSlug\", \"Monitored\", \"ManualAdd\", \"ReleaseDate\" FROM \"Editions\" ORDER BY \"Id\"",
                ["BookFiles"] = "SELECT \"Id\", \"EditionId\", \"CalibreId\", \"Quality\", \"Size\", \"DateAdded\", \"Modified\", \"Path\", \"ReleaseGroup\", \"MediaInfo\" FROM \"BookFiles\" ORDER BY \"Id\"",
                ["History"] = "SELECT \"Id\", \"AuthorId\", \"BookId\", \"SourceTitle\", \"Date\", \"Quality\", \"Data\", \"EventType\" FROM \"History\" WHERE \"Id\" = 23",
                ["Users"] = "SELECT \"Id\", \"Identifier\", \"Username\", \"Password\" FROM \"Users\" WHERE \"Id\" = 17",
                ["Config"] = "SELECT \"Id\", \"Key\", \"Value\" FROM \"Config\" WHERE \"Id\" >= 90001 ORDER BY \"Id\""
            };
            if (Tables(connection).Contains("AudiobookMetadataOverrides") &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"AudiobookMetadataOverrides\" WHERE \"Id\" = 90001") != 0)
            {
                queries["AudiobookMetadataOverrides"] = "SELECT \"Id\", \"Kind\", \"Region\", \"Asin\", \"Name\", \"Aliases\", \"ReplacementTitle\", \"SourceUrl\", \"Reason\", \"Enabled\", \"UpdatedAt\" FROM \"AudiobookMetadataOverrides\" WHERE \"Id\" = 90001";
            }

            return queries.ToDictionary(pair => pair.Key,
                pair => connection.Query(pair.Value).Select(row => string.Join("|", ((IDictionary<string, object>)row).Values.Select(Normalize))).ToArray());
        }

        private static string Normalize(object value)
        {
            if (value is DateTime date)
            {
                return DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
            }

            if (value is DateTimeOffset offset)
            {
                return offset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            }

            return value == null || value == DBNull.Value ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static void Insert(IDbConnection connection, string table, Dictionary<string, object> values)
        {
            var columns = Columns(connection, table);
            var row = values.Where(pair => columns.Contains(pair.Key)).ToList();
            var parameters = new DynamicParameters();
            for (var index = 0; index < row.Count; index++)
            {
                parameters.Add("p" + index, row[index].Value);
            }

            connection.Execute("INSERT INTO \"" + table + "\" (" + string.Join(",", row.Select(pair => "\"" + pair.Key + "\"")) +
                ") VALUES (" + string.Join(",", row.Select((pair, index) => "@p" + index)) + ")", parameters);
        }
    }

    public abstract class MigrationCompatibilityTestBase : CoreTest<MigrationController>
    {
        private string _postgresSocket;
        private string _ownedDatabase;
        private bool _createdDatabase;
        private MigrationContext _previousContext;

        protected MigrationCompatibilityTestBase(DatabaseType databaseType)
        {
            DatabaseType = databaseType;
        }

        protected DatabaseType DatabaseType { get; }
        protected string ConnectionString { get; private set; }

        [SetUp]
        public void CompatibilitySetup()
        {
            Mocker.SetConstant<ILoggerProvider>(NullLoggerProvider.Instance);
            _previousContext = MigrationContext.Current;
            if (DatabaseType == DatabaseType.SQLite)
            {
                ConnectionString = SqliteConnectionString(GetTempFilePath());
                return;
            }

            _postgresSocket = Environment.GetEnvironmentVariable("BOOKSHELF_MIGRATION_PG_SOCKET");
            if (string.IsNullOrEmpty(_postgresSocket))
            {
                Assert.Ignore("PostgreSQL migration checks require the explicit owned validation socket.");
            }

            ValidateOwnedSocket(_postgresSocket);
            _ownedDatabase = "p12_test_" + Guid.NewGuid().ToString("N");
            using var admin = new NpgsqlConnection(PostgresConnectionString("postgres"));
            admin.Open();
            admin.Execute("CREATE DATABASE \"" + _ownedDatabase + "\"");
            _createdDatabase = true;
            ConnectionString = PostgresConnectionString(_ownedDatabase);
        }

        [TearDown]
        public void CompatibilityCleanup()
        {
            MigrationContext.Current = _previousContext;
            if (_createdDatabase)
            {
                using var admin = new NpgsqlConnection(PostgresConnectionString("postgres"));
                admin.Open();
                admin.Execute("DROP DATABASE \"" + _ownedDatabase + "\" WITH (FORCE)");
                _createdDatabase = false;
            }
        }

        protected void Migrate(MigrationType type, long? version = null, Action<NzbDroneMigrationBase> before = null)
        {
            Subject.Migrate(
                ConnectionString,
                new MigrationContext(type, version)
                {
                    BeforeMigration = migration =>
                    {
                        if (version == migration.Version)
                        {
                            before?.Invoke(migration);
                        }
                    }
                },
                DatabaseType);
        }

        protected IDbConnection Open()
        {
            IDbConnection connection = DatabaseType == DatabaseType.SQLite ? new SQLiteConnection(ConnectionString) : new NpgsqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected static string SqliteConnectionString(string path)
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = path, Version = 3, Pooling = false, ForeignKeys = false, BinaryGUID = false, DateTimeKind = DateTimeKind.Utc
            }.ConnectionString;
        }

        protected static string[] Tables(IDbConnection connection)
        {
            return connection is SQLiteConnection
                ? connection.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name").ToArray()
                : connection.Query<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name").ToArray();
        }

        protected static string[] Columns(IDbConnection connection, string table)
        {
            return connection is SQLiteConnection
                ? connection.Query<string>("SELECT name FROM pragma_table_info(@table)", new { table }).ToArray()
                : connection.Query<string>("SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table", new { table }).ToArray();
        }

        protected static void AssertIntegrity(IDbConnection connection)
        {
            if (connection is SQLiteConnection)
            {
                connection.QuerySingle<string>("PRAGMA integrity_check").Should().Be("ok");
                connection.Query("PRAGMA foreign_key_check").Should().BeEmpty();
            }
        }

        private string PostgresConnectionString(string database)
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = _postgresSocket, Port = 5432, Username = "postgres", Database = database, Pooling = false,
                Enlist = false, Timezone = "UTC", IncludeErrorDetail = false
            }.ConnectionString;
        }

        private static void ValidateOwnedSocket(string socket)
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The owned PostgreSQL fixture requires a Unix socket.");
            }

            var marker = Path.Combine(socket, ".bookshelf-migration-tests");
            if (!Path.IsPathFullyQualified(socket) || !Directory.Exists(socket) || new DirectoryInfo(socket).LinkTarget != null ||
                !File.Exists(marker) || new FileInfo(marker).LinkTarget != null ||
                File.ReadAllText(marker) != "Bookshelf synthetic migration validation\n" ||
                (File.GetUnixFileMode(socket) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            {
                throw new InvalidOperationException("PostgreSQL tests accept only an explicitly marked private validation socket directory.");
            }
        }
    }
}
