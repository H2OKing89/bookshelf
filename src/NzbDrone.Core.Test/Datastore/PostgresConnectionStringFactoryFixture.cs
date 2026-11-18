using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using FluentAssertions;
using Moq;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class PostgresConnectionStringFactoryFixture
    {
        private const string Sentinel = "Synthetic secret;with='quotes' and spaces";
        private Mock<IConfigFileProvider> _config;
        private IAppFolderInfo _folders;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigFileProvider>();
            _config.SetupGet(config => config.LogDbEnabled).Returns(true);
            _config.SetupGet(config => config.PostgresPort).Returns(5432);
            _config.SetupGet(config => config.PostgresMainDb).Returns("readarr-main");
            _config.SetupGet(config => config.PostgresLogDb).Returns("readarr-log");
            _config.SetupGet(config => config.PostgresCacheDb).Returns("readarr-cache");
            _folders = Mock.Of<IAppFolderInfo>(folders => folders.AppDataFolder == Path.Combine(Path.GetTempPath(), "bookshelf-configuration-only"));
        }

        public static IEnumerable<TestCaseData> Configurations()
        {
            foreach (var logEnabled in new[] { false, true })
            {
                for (var present = 0; present < 8; present++)
                {
                    yield return new TestCaseData(logEnabled, present)
                        .SetName($"active_connection_string_combinations_{logEnabled}_{present}");
                }
            }
        }

        [TestCaseSource(nameof(Configurations))]
        public void each_active_connection_string_combination_should_be_validated_before_selecting_a_database(bool logEnabled, int present)
        {
            _config.SetupGet(config => config.LogDbEnabled).Returns(logEnabled);
            _config.SetupGet(config => config.PostgresMainDbConnectionString).Returns((present & 1) != 0 ? Connection("main") : null);
            _config.SetupGet(config => config.PostgresCacheDbConnectionString).Returns((present & 2) != 0 ? Connection("cache") : null);
            _config.SetupGet(config => config.PostgresLogDbConnectionString).Returns((present & 4) != 0 ? Connection("log") : null);
            var active = present & (logEnabled ? 7 : 3);
            var valid = active == 0 || ((present & 3) == 3 && (!logEnabled || (present & 4) != 0));

            if (!valid)
            {
                Action create = () => Create();
                create.Should().Throw<ReadarrStartupException>().Which.InnerException.Should().BeNull();
                return;
            }

            var factory = Create();
            factory.MainDbConnection.DatabaseType.Should().Be(active == 0 ? DatabaseType.SQLite : DatabaseType.PostgreSQL);
            factory.CacheDbConnection.DatabaseType.Should().Be(factory.MainDbConnection.DatabaseType);
            if (active != 0)
            {
                new NpgsqlConnectionStringBuilder(factory.MainDbConnection.ConnectionString).Database.Should().Be("owned-main");
                new NpgsqlConnectionStringBuilder(factory.CacheDbConnection.ConnectionString).Database.Should().Be("owned-cache");
                if (logEnabled)
                {
                    new NpgsqlConnectionStringBuilder(factory.LogDbConnection.ConnectionString).Database.Should().Be("owned-log");
                }
                else
                {
                    factory.LogDbConnection.Should().BeNull();
                    _config.VerifyGet(config => config.PostgresLogDbConnectionString, Times.Never());
                }
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  \t  ")]
        public void blank_full_strings_should_keep_the_existing_sqlite_defaults(string empty)
        {
            _config.SetupGet(config => config.PostgresMainDbConnectionString).Returns(empty);
            _config.SetupGet(config => config.PostgresCacheDbConnectionString).Returns(empty);
            _config.SetupGet(config => config.PostgresLogDbConnectionString).Returns(empty);
            var factory = Create();
            var main = new SQLiteConnectionStringBuilder(factory.MainDbConnection.ConnectionString);
            main.DataSource.Should().EndWith("readarr.db");
            main.Pooling.Should().BeTrue();
            main.ForeignKeys.Should().BeFalse();
            main.DateTimeKind.Should().Be(DateTimeKind.Utc);
            main.CacheSize.Should().Be(-20000);
            main.BusyTimeout.Should().Be(1000);
            factory.LogDbConnection.DatabaseType.Should().Be(DatabaseType.SQLite);
            new SQLiteConnectionStringBuilder(factory.CacheDbConnection.ConnectionString).DataSource.Should().EndWith("cache.db");
            _config.Verify(config => config.HasPostgresComponentSettings(), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void component_mode_should_preserve_the_existing_three_database_names_and_credentials(bool logging)
        {
            _config.SetupGet(config => config.LogDbEnabled).Returns(logging);
            _config.SetupGet(config => config.PostgresHost).Returns("legacy-host.invalid");
            _config.SetupGet(config => config.PostgresUser).Returns("legacy-reader");
            _config.SetupGet(config => config.PostgresPassword).Returns(Sentinel);
            _config.SetupGet(config => config.PostgresPort).Returns(6543);
            _config.SetupGet(config => config.PostgresMainDb).Returns("legacy-main");
            _config.SetupGet(config => config.PostgresCacheDb).Returns("legacy-cache");
            _config.SetupGet(config => config.PostgresLogDb).Returns("legacy-log");
            _config.Setup(config => config.HasPostgresComponentSettings()).Returns(true);

            var factory = Create();
            var connections = new[] { factory.MainDbConnection, factory.CacheDbConnection, factory.LogDbConnection };
            var names = new[] { "legacy-main", "legacy-cache", "legacy-log" };
            for (var index = 0; index < connections.Length; index++)
            {
                var info = connections[index];
                info.DatabaseType.Should().Be(DatabaseType.PostgreSQL);
                var builder = new NpgsqlConnectionStringBuilder(info.ConnectionString);
                builder.Host.Should().Be("legacy-host.invalid");
                builder.Port.Should().Be(6543);
                builder.Username.Should().Be("legacy-reader");
                builder.Password.Should().Be(Sentinel);
                builder.Database.Should().Be(names[index]);
                builder.Enlist.Should().BeFalse();
            }

            _config.Verify(config => config.HasPostgresComponentSettings(), Times.Never());
        }

        [Test]
        public void inactive_component_settings_without_a_host_should_keep_legacy_sqlite_selection()
        {
            _config.SetupGet(config => config.PostgresUser).Returns("legacy-reader");
            _config.SetupGet(config => config.PostgresPassword).Returns(Sentinel);
            _config.SetupGet(config => config.PostgresPort).Returns(6543);
            _config.Setup(config => config.HasPostgresComponentSettings()).Returns(true);

            Create().MainDbConnection.DatabaseType.Should().Be(DatabaseType.SQLite);
        }

        [Test]
        public void active_component_presence_should_reject_full_strings_without_echoing_any_supplied_value()
        {
            FullStrings();
            _config.Setup(config => config.HasPostgresComponentSettings()).Returns(true);
            _config.SetupGet(config => config.PostgresPassword).Returns(Sentinel);
            var error = Assert.Throws<ReadarrStartupException>(() => Create());
            error.ToString().Should().NotContain(Sentinel).And.NotContain("pg-config.invalid");
            error.InnerException.Should().BeNull();
        }

        [TestCase("main")]
        [TestCase("cache")]
        [TestCase("log")]
        public void full_strings_should_preserve_tls_quoted_passwords_and_provider_options_for_each_database(string role)
        {
            FullStrings();
            var factory = Create();
            var info = role == "main" ? factory.MainDbConnection : role == "cache" ? factory.CacheDbConnection : factory.LogDbConnection;
            var value = new NpgsqlConnectionStringBuilder(info.ConnectionString);
            value.Database.Should().Be("owned-" + role);
            value.Password.Should().Be(Sentinel);
            value.Username.Should().Be("synthetic-reader");
            value.SslMode.Should().Be(SslMode.VerifyFull);
            value.RootCertificate.Should().Be("/synthetic/ca.pem");
            value.Timeout.Should().Be(17);
            value.CommandTimeout.Should().Be(23);
            value.Pooling.Should().BeFalse();
            value.Enlist.Should().BeFalse("the application never enlists datastore connections in ambient transactions");
        }

        [TestCase("Host=pg-config.invalid;Database=owned-main;Password=synthetic;Ssl Mode=broken-synthetic-secret")]
        [TestCase("Host=pg-config.invalid;Database=owned-main;Port=broken-synthetic-secret")]
        [TestCase("Host=pg-config.invalid;Database=owned-main;Timeout=99999999999999999999999")]
        [TestCase("Host=pg-config.invalid;Database=owned-main;Unrecognized=broken-synthetic-secret")]
        [TestCase("Host=pg-config.invalid;Database=owned-main;Password=\"unterminated-broken-synthetic-secret")]
        [TestCase("Database=owned-main;Password=broken-synthetic-secret")]
        [TestCase("Host=pg-config.invalid;Password=broken-synthetic-secret")]
        [TestCase("Host=;Database=owned-main;Password=broken-synthetic-secret")]
        [TestCase("Host=pg-config.invalid;Database=;Password=broken-synthetic-secret")]
        public void invalid_full_strings_should_return_only_a_generic_startup_error(string value)
        {
            FullStrings();
            _config.SetupGet(config => config.PostgresMainDbConnectionString).Returns(value);

            var error = Assert.Throws<ReadarrStartupException>(() => Create());

            error.ToString().Should().NotContain("broken-synthetic-secret").And.NotContain("pg-config.invalid").And.NotContain(value);
            error.InnerException.Should().BeNull();
        }

        [Test]
        public void disabled_log_string_should_not_be_parsed_or_select_full_string_mode()
        {
            _config.SetupGet(config => config.LogDbEnabled).Returns(false);
            _config.SetupGet(config => config.PostgresLogDbConnectionString).Throws(new InvalidOperationException("The disabled option must never be read."));

            Create().MainDbConnection.DatabaseType.Should().Be(DatabaseType.SQLite);

            _config.SetupGet(config => config.PostgresMainDbConnectionString).Returns(Connection("main"));
            _config.SetupGet(config => config.PostgresCacheDbConnectionString).Returns(Connection("cache"));
            Create().LogDbConnection.Should().BeNull();
        }

        [Test]
        public void integrated_authentication_may_omit_a_username_but_never_a_database_or_host()
        {
            FullStrings();
            _config.SetupGet(config => config.PostgresMainDbConnectionString).Returns("Host=pg-config.invalid;Database=owned-main");

            new NpgsqlConnectionStringBuilder(Create().MainDbConnection.ConnectionString).Username.Should().BeNullOrEmpty();
        }

        private ConnectionStringFactory Create() => new ConnectionStringFactory(_folders, _config.Object);

        private void FullStrings()
        {
            _config.SetupGet(config => config.PostgresMainDbConnectionString).Returns(Connection("main"));
            _config.SetupGet(config => config.PostgresCacheDbConnectionString).Returns(Connection("cache"));
            _config.SetupGet(config => config.PostgresLogDbConnectionString).Returns(Connection("log"));
        }

        private static string Connection(string role)
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = "pg-config.invalid", Database = "owned-" + role, Username = "synthetic-reader", Password = Sentinel,
                SslMode = SslMode.VerifyFull, RootCertificate = "/synthetic/ca.pem", Pooling = false,
                Timeout = 17, CommandTimeout = 23, Enlist = true
            }.ConnectionString;
        }
    }
}
