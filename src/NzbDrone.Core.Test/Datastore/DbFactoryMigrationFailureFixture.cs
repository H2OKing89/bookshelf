using System.Data.SQLite;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class DbFactoryMigrationFailureFixture : CoreTest<DbFactory>
    {
        [TestCase(MigrationType.Main, SQLiteErrorCode.Error)]
        [TestCase(MigrationType.Main, SQLiteErrorCode.Constraint)]
        [TestCase(MigrationType.Log, SQLiteErrorCode.Error)]
        [TestCase(MigrationType.Log, SQLiteErrorCode.Constraint)]
        [TestCase(MigrationType.Cache, SQLiteErrorCode.Error)]
        [TestCase(MigrationType.Cache, SQLiteErrorCode.Constraint)]
        public void ordinary_migration_failures_should_not_delete_or_recreate_a_database(MigrationType type, SQLiteErrorCode code)
        {
            var context = SetupConnection(type);
            Mocker.GetMock<IMigrationController>()
                .Setup(controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.SQLite))
                .Throws(new SQLiteException(code, "Owned migration failure"));

            Assert.Throws<ReadarrStartupException>(() => Subject.Create(context));

            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.DeleteFile(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMigrationController>().Verify(
                controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.SQLite), Times.Once());
        }

        [TestCase(MigrationType.Log, SQLiteErrorCode.Corrupt)]
        [TestCase(MigrationType.Log, SQLiteErrorCode.NotADb)]
        [TestCase(MigrationType.Cache, SQLiteErrorCode.Corrupt)]
        [TestCase(MigrationType.Cache, (SQLiteErrorCode)267)]
        public void corrupt_derived_databases_should_keep_the_existing_recreation_behavior(MigrationType type, SQLiteErrorCode code)
        {
            var context = SetupConnection(type);
            Mocker.GetMock<IMigrationController>()
                .SetupSequence(controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.SQLite))
                .Throws(new SQLiteException(code, "Owned corrupt database"))
                .Pass();

            Subject.Create(context);

            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.DeleteFile("owned-synthetic.db"), Times.Once());
            Mocker.GetMock<IMigrationController>().Verify(
                controller => controller.Migrate(It.IsAny<string>(), context, DatabaseType.SQLite), Times.Exactly(2));
            ExceptionVerification.ExpectedErrors(1);
        }

        private MigrationContext SetupConnection(MigrationType type)
        {
            var info = new DatabaseConnectionInfo(DatabaseType.SQLite, "Data Source=owned-synthetic.db");
            var factory = Mocker.GetMock<IConnectionStringFactory>();
            factory.SetupGet(item => item.MainDbConnection).Returns(info);
            factory.SetupGet(item => item.LogDbConnection).Returns(info);
            factory.SetupGet(item => item.CacheDbConnection).Returns(info);
            factory.Setup(item => item.GetDatabasePath(It.IsAny<string>())).Returns("owned-synthetic.db");
            return new MigrationContext(type);
        }
    }
}
