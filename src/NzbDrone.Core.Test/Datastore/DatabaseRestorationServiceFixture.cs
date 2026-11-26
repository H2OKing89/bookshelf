using System;
using System.IO;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Datastore;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class DatabaseRestorationServiceFixture : TestBase<DatabaseRestorationService>
    {
        private static readonly string[] Suffixes = { "", "-shm", "-wal", "-journal" };
        private string _databasePath;
        private string _restorePath;
        private string _cachePath;

        [SetUp]
        public void Setup()
        {
            WithTempAsAppPath();
            _databasePath = TestFolderInfo.GetDatabase();
            _restorePath = TestFolderInfo.GetDatabaseRestore();
            _cachePath = TestFolderInfo.GetCacheDatabase();
            Mocker.GetMock<IConnectionStringFactory>()
                .SetupGet(x => x.MainDbConnection)
                .Returns(new DatabaseConnectionInfo(DatabaseType.SQLite, "unused"));
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(It.IsAny<string>()))
                .Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                .Callback<string, string, bool>((source, destination, overwrite) => File.Move(source, destination));
        }

        [Test]
        public void should_do_nothing_without_a_pending_restore()
        {
            WriteCurrentAndPending();
            File.Delete(_restorePath);

            Subject.Restore();

            foreach (var suffix in Suffixes)
            {
                File.ReadAllText(_databasePath + suffix).Should().Be("original" + suffix);
                File.ReadAllText(_cachePath + suffix).Should().Be("cache" + suffix);
            }

            Mocker.GetMock<IBackupRestoreValidator>().Verify(x => x.ValidateDatabase(It.IsAny<string>()), Times.Never());
            VerifyNoMovesOrDeletes();
        }

        [Test]
        public void should_validate_pending_database_before_touching_the_current_database()
        {
            WriteCurrentAndPending();
            Mocker.GetMock<IBackupRestoreValidator>()
                .Setup(x => x.ValidateDatabase(_restorePath))
                .Callback(() => AssertCurrentAndPending())
                .Throws(new RestoreBackupFailedException(HttpStatusCode.BadRequest, "Invalid backup database"));

            Action restore = () => Subject.Restore();

            restore.Should().Throw<RestoreBackupFailedException>();
            AssertCurrentAndPending();
            VerifyNoMovesOrDeletes();
        }

        [Test]
        public void should_reject_a_pending_sqlite_restore_when_postgres_is_configured()
        {
            WriteCurrentAndPending();
            Mocker.GetMock<IConnectionStringFactory>()
                .SetupGet(x => x.MainDbConnection)
                .Returns(new DatabaseConnectionInfo(DatabaseType.PostgreSQL, "unused"));

            Action restore = () => Subject.Restore();

            restore.Should().Throw<RestoreBackupFailedException>();
            AssertCurrentAndPending();
            Mocker.GetMock<IBackupRestoreValidator>().Verify(x => x.ValidateDatabase(It.IsAny<string>()), Times.Never());
            VerifyNoMovesOrDeletes();
        }

        [Test]
        public void should_keep_the_previous_main_and_cache_files_in_one_recovery_group_and_preserve_logs()
        {
            WriteCurrentAndPending();
            foreach (var suffix in Suffixes)
            {
                File.WriteAllText(TestFolderInfo.GetLogDatabase() + suffix, "existing logs" + suffix);
            }

            Subject.Restore();

            File.ReadAllText(_databasePath).Should().Be("replacement database");
            File.Exists(_restorePath).Should().BeFalse();
            var originalDatabase = Directory.GetFiles(TestFolderInfo.AppDataFolder, "readarr.db.pre-restore-*")
                .Single(path => !Suffixes.Skip(1).Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal)));
            var originalCache = _cachePath + originalDatabase.Substring(_databasePath.Length);
            foreach (var suffix in Suffixes)
            {
                File.ReadAllText(originalDatabase + suffix).Should().Be("original" + suffix);
                File.ReadAllText(originalCache + suffix).Should().Be("cache" + suffix);
                File.Exists(_cachePath + suffix).Should().BeFalse();
                if (suffix.Length > 0)
                {
                    File.Exists(_databasePath + suffix).Should().BeFalse();
                }
            }

            foreach (var suffix in Suffixes)
            {
                File.ReadAllText(TestFolderInfo.GetLogDatabase() + suffix).Should().Be("existing logs" + suffix);
            }

            Mocker.GetMock<IBackupRestoreValidator>().Verify(x => x.ValidateDatabase(_restorePath), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_preserve_an_earlier_recovery_group_on_another_restore()
        {
            WriteCurrentAndPending();
            Subject.Restore();
            var firstRecoveryFiles = Directory.GetFiles(TestFolderInfo.AppDataFolder, "*.pre-restore-*")
                .ToDictionary(path => path, File.ReadAllText);
            File.WriteAllText(_restorePath, "next database");

            Subject.Restore();

            File.ReadAllText(_databasePath).Should().Be("next database");
            foreach (var file in firstRecoveryFiles)
            {
                File.ReadAllText(file.Key).Should().Be(file.Value);
            }

            Directory.GetFiles(TestFolderInfo.AppDataFolder, "readarr.db.pre-restore-*").Should().HaveCount(5);
        }

        [TestCase("", false)]
        [TestCase("", true)]
        [TestCase("-shm", false)]
        [TestCase("-shm", true)]
        [TestCase("-wal", false)]
        [TestCase("-wal", true)]
        [TestCase("-journal", false)]
        [TestCase("-journal", true)]
        [TestCase("cache", false)]
        [TestCase("cache", true)]
        [TestCase("cache-shm", false)]
        [TestCase("cache-shm", true)]
        [TestCase("cache-wal", false)]
        [TestCase("cache-wal", true)]
        [TestCase("cache-journal", false)]
        [TestCase("cache-journal", true)]
        [TestCase("pending", false)]
        [TestCase("pending", true)]
        public void should_roll_back_every_file_when_a_move_fails_before_or_after_renaming(string failedSuffix, bool failAfterMove)
        {
            WriteCurrentAndPending();
            var failedSource = failedSuffix == "pending" ? _restorePath : failedSuffix.StartsWith("cache", StringComparison.Ordinal) ? _cachePath + failedSuffix.Substring("cache".Length) : _databasePath + failedSuffix;
            var failed = false;
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                .Callback<string, string, bool>((source, destination, overwrite) =>
                {
                    if (!failed && source == failedSource)
                    {
                        failed = true;
                        if (failAfterMove)
                        {
                            File.Move(source, destination);
                        }

                        throw new IOException("Simulated move failure");
                    }

                    File.Move(source, destination);
                });

            Action restore = () => Subject.Restore();

            restore.Should().Throw<IOException>().WithMessage("Simulated move failure");
            AssertCurrentAndPending();
            Mocker.GetMock<IDiskProvider>().Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_original_and_candidate_when_a_failed_move_leaves_both_files(bool pendingMove)
        {
            WriteCurrentAndPending();
            var failedSource = pendingMove ? _restorePath : _databasePath;
            var failed = false;
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                .Callback<string, string, bool>((source, destination, overwrite) =>
                {
                    if (!failed && source == failedSource)
                    {
                        failed = true;
                        File.Copy(source, destination);
                        throw new IOException("Simulated failure after copying");
                    }

                    File.Move(source, destination);
                });

            Action restore = () => Subject.Restore();

            restore.Should().Throw<IOException>();
            AssertCurrentAndPending();
            Mocker.GetMock<IDiskProvider>().Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_continue_rolling_back_remaining_files_if_one_rollback_fails()
        {
            WriteCurrentAndPending();
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                .Callback<string, string, bool>((source, destination, overwrite) =>
                {
                    if (source == _databasePath + "-wal")
                    {
                        throw new IOException("Original rotation failure");
                    }

                    if (destination == _databasePath + "-shm")
                    {
                        throw new IOException("Rollback failure");
                    }

                    File.Move(source, destination);
                });

            Action restore = () => Subject.Restore();

            restore.Should().Throw<IOException>().WithMessage("Original rotation failure");
            File.ReadAllText(_databasePath).Should().Be("original");
            File.ReadAllText(_restorePath).Should().Be("replacement database");
            File.ReadAllText(Directory.GetFiles(TestFolderInfo.AppDataFolder, "readarr.db.pre-restore-*-shm").Single())
                .Should().Be("original-shm");
            ExceptionVerification.ExpectedErrors(2);
        }

        private void WriteCurrentAndPending()
        {
            foreach (var suffix in Suffixes)
            {
                File.WriteAllText(_databasePath + suffix, "original" + suffix);
                File.WriteAllText(_cachePath + suffix, "cache" + suffix);
            }

            File.WriteAllText(_restorePath, "replacement database");
        }

        private void AssertCurrentAndPending()
        {
            foreach (var suffix in Suffixes)
            {
                File.ReadAllText(_databasePath + suffix).Should().Be("original" + suffix);
                File.ReadAllText(_cachePath + suffix).Should().Be("cache" + suffix);
            }

            File.ReadAllText(_restorePath).Should().Be("replacement database");
        }

        private void VerifyNoMovesOrDeletes()
        {
            Mocker.GetMock<IDiskProvider>().Verify(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never());
        }
    }
}
