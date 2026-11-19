using System;
using System.Data.SQLite;
using System.IO;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Datastore.Migration;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class PasswordRecoveryDatabaseFixture : MigrationCompatibilityTestBase
    {
        public PasswordRecoveryDatabaseFixture()
            : base(DatabaseType.SQLite)
        {
        }

        [Test]
        public void missing_database_should_never_be_created_by_recovery()
        {
            var path = new SQLiteConnectionStringBuilder(ConnectionString).DataSource;

            Assert.Throws<InvalidOperationException>(() => PasswordRecoveryDatabase.Create(Info()));

            File.Exists(path).Should().BeFalse();
        }

        [Test]
        public void incomplete_main_database_should_not_be_migrated_or_changed()
        {
            Migrate(MigrationType.Main, 1);
            var path = new SQLiteConnectionStringBuilder(ConnectionString).DataSource;
            var before = File.ReadAllBytes(path);

            Assert.Throws<InvalidOperationException>(() => PasswordRecoveryDatabase.Create(Info()));

            File.ReadAllBytes(path).Should().Equal(before);
            using var connection = Open();
            connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\"").Should().Equal(1);
        }

        [Test]
        public void compatible_main_database_should_keep_rows_and_history_when_opened_for_recovery()
        {
            Migrate(MigrationType.Main);
            using (var connection = Open())
            {
                connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('owned-recovery-marker', 'preserved')");
            }

            var database = PasswordRecoveryDatabase.Create(Info());
            var reopened = PasswordRecoveryDatabase.Create(Info());

            using var recovered = database.OpenConnection();
            recovered.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'owned-recovery-marker'").Should().Be("preserved");
            recovered.ExecuteScalar<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"").Should().Be(158);
            using var reopenedConnection = reopened.OpenConnection();
            reopenedConnection.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'owned-recovery-marker'").Should().Be("preserved");
        }

        [Test]
        public void future_database_should_not_be_opened_for_credential_writes()
        {
            Migrate(MigrationType.Main);
            using (var connection = Open())
            {
                connection.Execute("UPDATE \"VersionInfo\" SET \"Version\" = 999 WHERE \"Version\" = 158");
            }

            Assert.Throws<InvalidOperationException>(() => PasswordRecoveryDatabase.Create(Info()));

            using var after = Open();
            after.ExecuteScalar<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"").Should().Be(999);
        }

        private DatabaseConnectionInfo Info()
        {
            return new DatabaseConnectionInfo(DatabaseType.SQLite, ConnectionString);
        }
    }
}
