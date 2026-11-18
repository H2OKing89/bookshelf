using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BackupTests
{
    [TestFixture]
    [NonParallelizable]
    public class MakeDatabaseBackupFixture : CoreTest<MakeDatabaseBackup>
    {
        private string _sourcePath;
        private string _backupFolder;

        private string BackupPath => Path.Combine(_backupFolder, Path.GetFileName(_sourcePath));

        [SetUp]
        public void Setup()
        {
            AssemblyLoader.RegisterSQLiteResolver();
            var sourceFolder = Path.Combine(TempFolder, "owned live library");
            _backupFolder = Path.Combine(TempFolder, "owned backup; directory");
            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(_backupFolder);
            _sourcePath = Path.Combine(sourceFolder, "readarr.db");
            using var connection = Open(SourceConnectionString());
            connection.Execute("CREATE TABLE Parts (Id INTEGER PRIMARY KEY, Generation INTEGER NOT NULL, Payload TEXT NOT NULL)");
            connection.Execute("INSERT INTO Parts VALUES (1, 1, 'First part'), (2, 1, 'Second part')");
        }

        [TearDown]
        public void CleanupConnections()
        {
            // Tests may inspect pooled source connections. Release their idle
            // handles only after all assertions and owned connections finish.
            SQLiteConnection.ClearAllPools();
        }

        [Test]
        public void should_capture_committed_wal_rows_without_interrupting_an_active_write()
        {
            using var writer = Open(SourceConnectionString());
            using var transaction = writer.BeginTransaction();
            writer.Execute("UPDATE Parts SET Generation = 2 WHERE Id = 1", transaction: transaction);

            Subject.BackupDatabase(Database(), _backupFolder);

            using (var backup = Open(BackupConnectionString()))
            {
                backup.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(1, 1);
                backup.ExecuteScalar<string>("PRAGMA quick_check").Should().Be("ok");
            }

            writer.Execute("UPDATE Parts SET Generation = 2 WHERE Id = 2", transaction: transaction);
            transaction.Commit();
            writer.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(2, 2);
            writer.ExecuteScalar<string>("PRAGMA journal_mode").Should().Be("wal");
            File.ReadAllBytes(BackupPath).Skip(18).Take(2).Should().Equal(
                new byte[] { 1, 1 },
                "a restorable backup must not depend on WAL sidecar bytes");
            File.Exists(BackupPath + "-wal").Should().BeFalse();
            File.Exists(BackupPath + "-shm").Should().BeFalse();
            OwnedStagingFiles().Should().BeEmpty();
        }

        [Test]
        public void should_replace_a_completed_backup_without_leaving_destination_handles_or_staging_files()
        {
            Subject.BackupDatabase(Database(), _backupFolder);
            using (var source = Open(SourceConnectionString()))
            {
                source.Execute("UPDATE Parts SET Generation = 3");
            }

            Subject.BackupDatabase(Database(), _backupFolder);

            using (var backup = Open(BackupConnectionString()))
            {
                backup.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(3, 3);
            }

            var moved = BackupPath + ".owned-moved";
            File.Move(BackupPath, moved);
            File.Delete(moved);
            OwnedStagingFiles().Should().BeEmpty();
        }

        [Test]
        public void failed_publication_should_preserve_the_existing_destination_and_unrelated_part_file()
        {
            Directory.CreateDirectory(BackupPath);
            var sentinel = Path.Combine(BackupPath, "owned-sentinel");
            File.WriteAllText(sentinel, "Existing backup destination");
            var unrelated = Path.Combine(_backupFolder, "readarr.db.unrelated.part");
            File.WriteAllText(unrelated, "Unrelated staging bytes");

            var error = Assert.Catch(() => Subject.BackupDatabase(Database(), _backupFolder));

            (error is IOException || error is UnauthorizedAccessException).Should().BeTrue();

            File.ReadAllText(sentinel).Should().Be("Existing backup destination");
            File.ReadAllText(unrelated).Should().Be("Unrelated staging bytes");
            OwnedStagingFiles().Should().BeEmpty();
            using var source = Open(SourceConnectionString());
            source.ExecuteScalar<string>("PRAGMA quick_check").Should().Be("ok");
            source.ExecuteScalar<int>("SELECT COUNT(*) FROM Parts").Should().Be(2);
        }

        [Test]
        public void should_preserve_a_prior_backup_when_the_new_snapshot_fails_its_integrity_check()
        {
            Subject.BackupDatabase(Database(), _backupFolder);
            var before = File.ReadAllBytes(BackupPath);
            using (var source = Open(SourceConnectionString()))
            {
                source.Execute("CREATE TABLE CheckedValues (Value INTEGER CHECK (Value >= 0))");
                source.Execute("PRAGMA ignore_check_constraints = ON");
                source.Execute("INSERT INTO CheckedValues VALUES (-1)");
                source.Execute("PRAGMA ignore_check_constraints = OFF");
            }

            Assert.Throws<IOException>(() => Subject.BackupDatabase(Database(), _backupFolder));

            File.ReadAllBytes(BackupPath).Should().Equal(before);
            OwnedStagingFiles().Should().BeEmpty();
            using var backup = Open(BackupConnectionString());
            backup.ExecuteScalar<string>("PRAGMA quick_check").Should().Be("ok");
            backup.ExecuteScalar<int>("SELECT COUNT(*) FROM Parts").Should().Be(2);
        }

        [Test]
        public void should_reject_a_backup_over_the_source_without_changing_its_rows()
        {
            Assert.Throws<ArgumentException>(() => Subject.BackupDatabase(Database(), Path.GetDirectoryName(_sourcePath)));

            using var source = Open(SourceConnectionString());
            source.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(1, 1);
            Directory.GetFiles(Path.GetDirectoryName(_sourcePath), ".*.backup-*").Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_reject_a_backup_over_the_source_through_a_directory_symlink(bool ancestorLink)
        {
            PosixOnly();
            SQLiteConnection.ClearAllPools();
            var rollbackJournal = SourceConnectionString(SQLiteJournalModeEnum.Delete, pooling: false);
            using var source = Open(rollbackJournal);
            var database = new Mock<IDatabase>();
            database.Setup(item => item.OpenConnection()).Returns(() => Open(rollbackJournal));
            var sourceFolder = Path.GetDirectoryName(_sourcePath);
            var alias = Path.Combine(TempFolder, "owned source alias");
            Directory.CreateSymbolicLink(alias, ancestorLink ? TempFolder : sourceFolder);
            var targetFolder = ancestorLink ? Path.Combine(alias, Path.GetFileName(sourceFolder)) : alias;

            Assert.Throws<ArgumentException>(() => Subject.BackupDatabase(database.Object, targetFolder));

            source.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(1, 1);
            source.ExecuteScalar<string>("PRAGMA journal_mode").Should().Be("delete");
            source.Execute("UPDATE Parts SET Generation = 5");
            using var reopened = Open(rollbackJournal);
            reopened.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(5, 5);
            Directory.GetFiles(sourceFolder, ".*.backup-*").Should().BeEmpty();
        }

        [TestCase("-wal")]
        [TestCase("-shm")]
        [TestCase("-journal")]
        public void should_preserve_a_prior_backup_with_existing_journal_sidecars(string suffix)
        {
            Subject.BackupDatabase(Database(), _backupFolder);
            var before = File.ReadAllBytes(BackupPath);
            var sidecar = BackupPath + suffix;
            File.WriteAllText(sidecar, "Owned prior backup journal bytes");

            Assert.Throws<IOException>(() => Subject.BackupDatabase(Database(), _backupFolder));

            File.ReadAllBytes(BackupPath).Should().Equal(before);
            File.ReadAllText(sidecar).Should().Be("Owned prior backup journal bytes");
            OwnedStagingFiles().Should().BeEmpty();
        }

        [Test]
        public void a_locked_source_should_fail_within_its_timeout_preserve_a_prior_backup_and_allow_retry()
        {
            Subject.BackupDatabase(Database(), _backupFolder);
            var before = File.ReadAllBytes(BackupPath);
            SQLiteConnection.ClearAllPools();
            var rollbackJournal = SourceConnectionString(SQLiteJournalModeEnum.Delete, pooling: false);
            using var locker = Open(rollbackJournal);
            var source = Open(rollbackJournal);
            var database = new Mock<IDatabase>();
            database.Setup(item => item.OpenConnection()).Returns(source);
            locker.Execute("BEGIN EXCLUSIVE");
            locker.Execute("UPDATE Parts SET Generation = 4");
            var timer = Stopwatch.StartNew();
            try
            {
                Assert.Catch(() => Subject.BackupDatabase(database.Object, _backupFolder));
            }
            finally
            {
                locker.Execute("ROLLBACK");
            }

            timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6), "lock retries must remain bounded by the source command timeout");
            File.ReadAllBytes(BackupPath).Should().Equal(before);
            OwnedStagingFiles().Should().BeEmpty();
            Subject.BackupDatabase(Database(), _backupFolder);
            using var backup = Open(BackupConnectionString());
            backup.Query<int>("SELECT Generation FROM Parts ORDER BY Id").Should().Equal(1, 1);
        }

        private IDatabase Database()
        {
            var database = new Mock<IDatabase>();
            database.Setup(item => item.OpenConnection()).Returns(() => Open(SourceConnectionString()));
            return database.Object;
        }

        private string SourceConnectionString(SQLiteJournalModeEnum journalMode = SQLiteJournalModeEnum.Wal, bool pooling = true)
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = _sourcePath,
                Pooling = pooling,
                JournalMode = journalMode,
                DefaultTimeout = 1,
                BusyTimeout = 100
            }.ConnectionString;
        }

        private string BackupConnectionString()
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = BackupPath,
                Pooling = false,
                ReadOnly = true,
                FailIfMissing = true
            }.ConnectionString;
        }

        private string[] OwnedStagingFiles()
        {
            return Directory.GetFiles(_backupFolder, ".readarr.db.backup-*");
        }

        private static SQLiteConnection Open(string connectionString)
        {
            var connection = new SQLiteConnection(connectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }
}
