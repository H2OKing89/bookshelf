using System;
using FluentAssertions;
using Moq;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class PostgresFailurePrivacyFixture : CoreTest<DbFactory>
    {
        private const string Sentinel = "OwnedSensitiveProviderValue";

        [TestCase(MigrationType.Main)]
        [TestCase(MigrationType.Log)]
        [TestCase(MigrationType.Cache)]
        public void provider_migration_failures_should_not_retain_raw_exception_payloads(MigrationType type)
        {
            var context = Configure(type);
            Mocker.GetMock<IMigrationController>()
                .Setup(controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.PostgreSQL))
                .Throws(new NpgsqlException(Sentinel, new Exception(Sentinel)));

            var error = Assert.Throws<ReadarrStartupException>(() => Subject.Create(context));

            error.InnerException.Should().BeNull();
            error.ToString().Should().NotContain(Sentinel);
            error.Message.Should().Contain("PostgreSQL").And.Contain(type.ToString());
        }

        [TestCase(MigrationType.Main)]
        [TestCase(MigrationType.Log)]
        [TestCase(MigrationType.Cache)]
        public void other_postgres_startup_failures_should_not_retain_raw_exception_payloads(MigrationType type)
        {
            var context = Configure(type);
            Mocker.GetMock<IMigrationController>()
                .Setup(controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.PostgreSQL))
                .Throws(new InvalidOperationException(Sentinel));

            var error = Assert.Throws<ReadarrStartupException>(() => Subject.Create(context));

            error.InnerException.Should().BeNull();
            error.ToString().Should().NotContain(Sentinel);
        }

        [TestCase(MigrationType.Main)]
        [TestCase(MigrationType.Log)]
        [TestCase(MigrationType.Cache)]
        public void server_failure_should_keep_its_safe_sqlstate_without_server_values(MigrationType type)
        {
            var context = Configure(type);
            Mocker.GetMock<IMigrationController>()
                .Setup(controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.PostgreSQL))
                .Throws(new PostgresException(Sentinel, "FATAL", "FATAL", "28P01", detail: Sentinel));

            var error = Assert.Throws<ReadarrStartupException>(() => Subject.Create(context));

            error.InnerException.Should().BeNull();
            error.Message.Should().Contain("SQLSTATE 28P01").And.NotContain(Sentinel);
        }

        [Test]
        public void arbitrary_sqlstate_text_should_not_be_returned_as_a_diagnostic()
        {
            var context = Configure(MigrationType.Main);
            Mocker.GetMock<IMigrationController>()
                .Setup(controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.PostgreSQL))
                .Throws(new PostgresException(Sentinel, "FATAL", "FATAL", Sentinel));

            var error = Assert.Throws<ReadarrStartupException>(() => Subject.Create(context));

            error.ToString().Should().NotContain(Sentinel);
            error.Message.Should().NotContain("SQLSTATE");
        }

        [TestCase(MigrationType.Main)]
        [TestCase(MigrationType.Log)]
        [TestCase(MigrationType.Cache)]
        public void runtime_connection_failures_should_not_retain_connection_string_parser_errors(MigrationType type)
        {
            var context = Configure(type, Sentinel + "=value");
            var database = Subject.Create(context);

            var error = Assert.Throws<NpgsqlException>(() => database.OpenConnection());

            error.InnerException.Should().BeNull();
            error.ToString().Should().NotContain(Sentinel);
            error.Message.Should().Contain("PostgreSQL");
        }

        private MigrationContext Configure(MigrationType type, string connectionString = "Host=owned.invalid;Database=owned")
        {
            var info = new DatabaseConnectionInfo(DatabaseType.PostgreSQL, connectionString);
            var factory = Mocker.GetMock<IConnectionStringFactory>();
            factory.SetupGet(item => item.MainDbConnection).Returns(info);
            factory.SetupGet(item => item.LogDbConnection).Returns(info);
            factory.SetupGet(item => item.CacheDbConnection).Returns(info);
            return new MigrationContext(type);
        }
    }
}
