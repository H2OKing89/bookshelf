using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BackupTests
{
    [TestFixture]
    [NonParallelizable]
    public class BackupServiceRestoreFixture : CoreTest<BackupService>
    {
        private const string OriginalConfig = "<Config><Port>8787</Port><ApiKey>0123456789abcdef0123456789abcdef</ApiKey></Config>";
        private const string RestoredConfig = "<Config><Port>20555</Port><ApiKey>abcdef0123456789abcdef0123456789</ApiKey></Config>";
        private byte[] _databaseSeed;
        private int _latestMigration;
        private string _home;
        private string _uploads;
        private string _work;
        private string _configPath;
        private string _restorePath;
        private string _liveDatabasePath;
        private byte[] _originalRestore;
        private byte[] _originalLiveDatabase;
        private List<string> _createdRestoreDirectories;
        private Action<string, string> _beforeWrite;

        [OneTimeSetUp]
        public void CreateMigratedSqliteSeed()
        {
            NzbDroneLogger.GetLogger(typeof(BackupServiceRestoreFixture));
            var directory = Path.Combine(Path.GetTempPath(), "bookshelf-restore-seed-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "readarr.db");
            var previousMigrationContext = MigrationContext.Current;
            Directory.CreateDirectory(directory);
            try
            {
                var connectionString = ConnectionString(path);
                new MigrationController(NzbDroneLogger.GetLogger(typeof(MigrationController)), NullLoggerProvider.Instance)
                    .Migrate(connectionString, new MigrationContext(MigrationType.Main), DatabaseType.SQLite);
                using (var connection = OpenDatabase(path))
                {
                    _latestMigration = connection.ExecuteScalar<int>("SELECT MAX(\"Version\") FROM \"VersionInfo\"");
                }

                _databaseSeed = File.ReadAllBytes(path);
            }
            finally
            {
                MigrationContext.Current = previousMigrationContext;
                Directory.Delete(directory, true);
            }
        }

        [SetUp]
        public void Setup()
        {
            _home = Path.Combine(TempFolder, "home");
            _uploads = Path.Combine(TempFolder, "uploads");
            _work = Path.Combine(TempFolder, "work");
            Directory.CreateDirectory(_home);
            Directory.CreateDirectory(_uploads);
            Directory.CreateDirectory(_work);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(info => info.AppDataFolder).Returns(_home);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(info => info.TempFolder).Returns(_work);
            _configPath = Mocker.GetMock<IAppFolderInfo>().Object.GetConfigPath();
            _restorePath = Mocker.GetMock<IAppFolderInfo>().Object.GetDatabaseRestore();
            _liveDatabasePath = Mocker.GetMock<IAppFolderInfo>().Object.GetDatabase();
            File.WriteAllText(_configPath, OriginalConfig);
            File.WriteAllBytes(_restorePath, _databaseSeed);
            File.WriteAllBytes(_liveDatabasePath, _databaseSeed);
            _originalRestore = File.ReadAllBytes(_restorePath);
            _originalLiveDatabase = File.ReadAllBytes(_liveDatabasePath);
            _createdRestoreDirectories = new List<string>();
            _beforeWrite = null;
            ConfigureRealFileOperations();
            Mocker.SetConstant<IBackupRestoreValidator>(new BackupRestoreValidator(Mocker.GetMock<IDiskProvider>().Object));
            Mocker.GetMock<IMainDatabase>().SetupGet(database => database.DatabaseType).Returns(DatabaseType.SQLite);
            Mocker.GetMock<IMainDatabase>().SetupGet(database => database.Migration).Returns(_latestMigration);
        }

        [TestCase("backup.xml")]
        [TestCase("backup.XML")]
        public void should_restore_direct_config_without_replacing_database(string filename)
        {
            var upload = WriteUpload(filename, Encoding.UTF8.GetBytes(RestoredConfig));

            Subject.Restore(upload);

            File.ReadAllText(_configPath).Should().Be(RestoredConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(_originalRestore);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [TestCase("uploaded.db")]
        [TestCase("uploaded.DB")]
        public void should_restore_direct_real_sqlite_database_without_replacing_config(string filename)
        {
            var upload = GivenDatabase(filename);
            var expected = File.ReadAllBytes(upload);

            Subject.Restore(upload);

            File.ReadAllBytes(_restorePath).Should().Equal(expected);
            File.ReadAllText(_configPath).Should().Be(OriginalConfig);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [TestCase("backup.zip", "config.xml", "readarr.db")]
        [TestCase("backup.ZIP", "CONFIG.XML", "READARR.DB")]
        public void should_restore_real_zip_payload_names_case_insensitively(string archiveName, string configName, string databaseName)
        {
            var database = GivenDatabase("payload.db");
            var expectedDatabase = File.ReadAllBytes(database);
            var archive = GivenZip(archiveName,
                (configName, Encoding.UTF8.GetBytes(RestoredConfig)),
                (databaseName, expectedDatabase),
                ("INFO", Encoding.UTF8.GetBytes("synthetic backup information")));

            Subject.Restore(archive);

            File.ReadAllText(_configPath).Should().Be(RestoredConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(expectedDatabase);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [TestCase("<Config><Port>8787")]
        [TestCase("<Settings><Port>8787</Port></Settings>")]
        [TestCase("<Envelope><Config><Port>8787</Port></Config></Envelope>")]
        [TestCase("<!DOCTYPE Config [<!ENTITY test 'synthetic-value'>]><Config><ApiKey>&test;</ApiKey></Config>")]
        public void should_reject_invalid_or_dtd_config_before_changing_home(string xml)
        {
            var upload = WriteUpload("bad.xml", Encoding.UTF8.GetBytes(xml));

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(upload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            File.Exists(upload).Should().BeTrue();
            AssertWorkCleaned();
        }

        [TestCase("empty")]
        [TestCase("text")]
        [TestCase("header-only")]
        [TestCase("truncated")]
        [TestCase("wrong-schema")]
        [TestCase("missing-books")]
        [TestCase("missing-user-identity")]
        [TestCase("missing-initial-version")]
        [TestCase("unknown-version")]
        [TestCase("fractional-version")]
        [TestCase("future-version")]
        public void should_reject_invalid_incompatible_or_future_database_before_changing_home(string kind)
        {
            var upload = GivenInvalidDatabase(kind);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(upload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            File.Exists(upload).Should().BeTrue();
            AssertWorkCleaned();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void mixed_zip_should_validate_all_payloads_before_replacing_either_target(bool invalidDatabase)
        {
            var database = invalidDatabase ? GivenInvalidDatabase("header-only") : GivenDatabase("payload.db");
            var xml = invalidDatabase ? RestoredConfig : "<Settings />";
            var archive = GivenZip("mixed.zip",
                ("config.xml", Encoding.UTF8.GetBytes(xml)),
                ("readarr.db", File.ReadAllBytes(database)));

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive));

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase("config.xml", "CONFIG.XML")]
        [TestCase("readarr.db", "READARR.DB")]
        public void should_reject_case_insensitive_duplicate_payloads(string first, string second)
        {
            var contents = first.EndsWith(".xml", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(RestoredConfig) : _databaseSeed;
            var archive = GivenZip("duplicates.zip", (first, contents), (second, contents));

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive));

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void archive_without_recognized_payload_should_not_change_home()
        {
            var archive = GivenZip("unrelated.zip", ("INFO", Encoding.UTF8.GetBytes("no config or database")));

            var exception = Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive));

            exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void zip_crc_failure_should_remove_partial_extraction_without_changing_home()
        {
            var archive = GivenZip("bad-crc.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            var bytes = File.ReadAllBytes(archive);
            var centralDirectory = Enumerable.Range(0, bytes.Length - 3).First(index =>
                bytes[index] == 0x50 && bytes[index + 1] == 0x4b && bytes[index + 2] == 0x01 && bytes[index + 3] == 0x02);
            bytes[centralDirectory + 16] ^= 0xff;
            File.WriteAllBytes(archive, bytes);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive));

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_restore_small_zip64_archives_with_or_without_legacy_sentinels(bool sentinels)
        {
            var archive = GivenZip("compatible-zip64.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            File.WriteAllBytes(archive, WithZip64(File.ReadAllBytes(archive), sentinels));

            Subject.Restore(archive);

            File.ReadAllText(_configPath).Should().Be(RestoredConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(_originalRestore);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void should_accept_entry_comment_containing_zip64_locator_bytes_away_from_locator_position()
        {
            var archive = GivenZip("comment-signature.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            var original = File.ReadAllBytes(archive);
            var end = original.Length - 22;
            var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 16, 4));
            var directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 12, 4));
            var comment = new byte[] { (byte)'o', (byte)'k', 0x50, 0x4b, 0x06, 0x07, (byte)'e', (byte)'n', (byte)'d' };
            var bytes = new byte[original.Length + comment.Length];
            Array.Copy(original, bytes, end);
            comment.CopyTo(bytes, end);
            Array.Copy(original, end, bytes, end + comment.Length, 22);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 32, 2), (ushort)comment.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + comment.Length + 12, 4), directoryLength + (uint)comment.Length);
            File.WriteAllBytes(archive, bytes);

            Subject.Restore(archive);

            File.ReadAllText(_configPath).Should().Be(RestoredConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(_originalRestore);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [TestCase(65UL)]
        [TestCase(ulong.MaxValue)]
        public void zip64_preflight_should_reject_unbounded_counts_before_archive_construction(ulong count)
        {
            var archive = GivenZip("unbounded-zip64.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            File.WriteAllBytes(archive, WithZip64(File.ReadAllBytes(archive), true, count));
            using var stream = File.OpenRead(archive);

            Assert.Throws<RestoreBackupFailedException>(() => BackupArchivePreflight.Validate(stream))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            _createdRestoreDirectories.Should().BeEmpty();
            Directory.GetDirectories(_home, ".readarr-restore-*").Should().BeEmpty();
            Directory.EnumerateFileSystemEntries(_work).Should().BeEmpty();
        }

        [Test]
        public void archive_with_more_than_64_entries_should_be_rejected()
        {
            var entries = Enumerable.Range(0, 65).Select(index => ($"file-{index}.txt", Array.Empty<byte>())).ToArray();
            var archive = GivenZip("too-many.zip", entries);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase("central-offset")]
        [TestCase("central-length")]
        [TestCase("central-count")]
        [TestCase("entry-length")]
        [TestCase("zip64-offset")]
        [TestCase("zip64-length")]
        [TestCase("fake-comment-record")]
        public void malformed_archive_directory_metadata_should_be_rejected(string corruption)
        {
            var archive = GivenZip("malformed-metadata.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            var bytes = File.ReadAllBytes(archive);
            var end = bytes.Length - 22;
            var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16, 4));
            switch (corruption)
            {
                case "central-offset":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 16, 4), uint.MaxValue - 1);
                    break;
                case "central-length":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 12, 4), uint.MaxValue - 1);
                    break;
                case "central-count":
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 8, 2), 2);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 10, 2), 2);
                    break;
                case "entry-length":
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 30, 2), ushort.MaxValue);
                    break;
                case "zip64-offset":
                    bytes = WithZip64(bytes, true);
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 22 - 20 + 8, 8), ulong.MaxValue);
                    break;
                case "zip64-length":
                    bytes = WithZip64(bytes, true);
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(end + 4, 8), ulong.MaxValue);
                    break;
                case "fake-comment-record":
                    var original = bytes;
                    bytes = new byte[original.Length + 22];
                    original.CopyTo(bytes, 0);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 20, 2), 22);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(original.Length, 4), 0x06054b50);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(original.Length + 8, 2), ushort.MaxValue);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(original.Length + 10, 2), ushort.MaxValue);
                    break;
            }

            File.WriteAllBytes(archive, bytes);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase("../config.xml")]
        [TestCase("nested/config.xml")]
        [TestCase("nested\\config.xml")]
        [TestCase("/config.xml")]
        [TestCase("C:\\config.xml")]
        [TestCase("config.xml:stream")]
        public void unsafe_archive_paths_should_be_rejected(string name)
        {
            var archive = GivenZip("unsafe-name.zip", (name, Encoding.UTF8.GetBytes(RestoredConfig)));

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase(0xa000)]
        [TestCase(0x1000)]
        [TestCase(0x4000)]
        public void symlink_and_special_archive_entries_should_be_rejected(int mode)
        {
            var archive = GivenZip("special-entry.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            var bytes = File.ReadAllBytes(archive);
            var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 22 + 16, 4));
            bytes[central + 5] = 3;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 38, 4), (uint)mode << 16);
            File.WriteAllBytes(archive, bytes);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void oversized_config_payload_should_be_rejected_before_publication(bool zip)
        {
            var contents = Encoding.UTF8.GetBytes("<Config><Notes>" + new string('a', 1024 * 1024) + "</Notes></Config>");
            var upload = zip ? GivenZip("oversized.zip", ("config.xml", contents)) : WriteUpload("oversized.xml", contents);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(upload))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void declared_payload_length_mismatch_should_be_rejected(int difference)
        {
            var archive = GivenZip("length-mismatch.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            var bytes = File.ReadAllBytes(archive);
            var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 22 + 16, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(central + 24, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24, 4), (uint)(length + difference));
            File.WriteAllBytes(archive, bytes);

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(archive))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void staging_copy_io_failure_should_propagate_without_changing_home_or_consuming_upload()
        {
            var upload = WriteUpload("copy-failure.xml", Encoding.UTF8.GetBytes(RestoredConfig));
            var failure = new IOException("synthetic staging copy failure");
            _beforeWrite = (source, destination) =>
            {
                if (source == upload)
                {
                    throw failure;
                }
            };

            Assert.Throws<IOException>(() => Subject.Restore(upload)).Should().BeSameAs(failure);

            AssertHomeUnchanged();
            File.ReadAllText(upload).Should().Be(RestoredConfig);
            AssertWorkCleaned();
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void second_publication_io_failure_should_restore_original_target_bytes_or_absence(bool originalTargetsExist, bool writeThenThrow)
        {
            if (!originalTargetsExist)
            {
                File.Delete(_configPath);
                File.Delete(_restorePath);
            }

            var database = GivenDatabase("publication-payload.db");
            var archive = GivenZip(
                "publication-failure.zip",
                ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)),
                ("readarr.db", File.ReadAllBytes(database)));
            var writes = 0;
            var failure = new IOException("synthetic publication failure");
            _beforeWrite = (source, destination) =>
            {
                if ((destination == _configPath || destination == _restorePath) && ++writes == 2)
                {
                    if (writeThenThrow)
                    {
                        File.Copy(source, destination, true);
                    }

                    throw failure;
                }
            };

            Assert.Throws<IOException>(() => Subject.Restore(archive)).Should().BeSameAs(failure);

            if (originalTargetsExist)
            {
                AssertHomeUnchanged();
            }
            else
            {
                File.Exists(_configPath).Should().BeFalse();
                File.Exists(_restorePath).Should().BeFalse();
                AssertLiveDatabaseUnchanged();
            }

            AssertWorkCleaned();
        }

        [Test]
        public void separate_restore_attempts_should_use_distinct_extraction_directories()
        {
            var first = GivenZip("first.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));
            var second = GivenZip("second.zip", ("config.xml", Encoding.UTF8.GetBytes(OriginalConfig)));

            Subject.Restore(first);
            var firstDirectories = _createdRestoreDirectories.ToArray();
            _createdRestoreDirectories.Clear();
            Subject.Restore(second);

            firstDirectories.Should().NotBeEmpty();
            _createdRestoreDirectories.Should().NotBeEmpty();
            _createdRestoreDirectories.Intersect(firstDirectories, StringComparer.Ordinal).Should().BeEmpty();
            File.ReadAllText(_configPath).Should().Be(OriginalConfig);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void restore_should_leave_another_attempts_stale_fixed_temporary_directory_untouched()
        {
            var stale = Path.Combine(_work, "readarr_backup_restore");
            Directory.CreateDirectory(stale);
            var staleConfig = Path.Combine(stale, "config.xml");
            File.WriteAllText(staleConfig, "<Config><Port>11111</Port></Config>");
            var archive = GivenZip("fresh.zip", ("config.xml", Encoding.UTF8.GetBytes(RestoredConfig)));

            Subject.Restore(archive);

            File.ReadAllText(_configPath).Should().Be(RestoredConfig);
            File.ReadAllText(staleConfig).Should().Be("<Config><Port>11111</Port></Config>");
            Directory.GetDirectories(_work).Should().Equal(stale);
            Directory.GetDirectories(_home, ".readarr-restore-*").Should().BeEmpty();
            AssertLiveDatabaseUnchanged();
        }

        [Test]
        public async Task simultaneous_restore_calls_should_serialize_without_mixing_payloads()
        {
            var first = WriteUpload("first.xml", Encoding.UTF8.GetBytes(RestoredConfig));
            var second = WriteUpload("second.xml", Encoding.UTF8.GetBytes(OriginalConfig));
            using var firstEntered = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            using var secondStarted = new ManualResetEventSlim();
            var gated = 0;
            _beforeWrite = (source, destination) =>
            {
                if (source == first && Interlocked.Exchange(ref gated, 1) == 0)
                {
                    firstEntered.Set();
                    if (!releaseFirst.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("First restore was not released by the fixture");
                    }
                }
            };

            var firstRestore = Task.Run(() => Subject.Restore(first));
            var secondRestore = Task.CompletedTask;
            try
            {
                firstEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                secondRestore = Task.Run(() =>
                {
                    secondStarted.Set();
                    Subject.Restore(second);
                });
                secondStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                secondRestore.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse();
            }
            finally
            {
                releaseFirst.Set();
                await Task.WhenAll(firstRestore, secondRestore).WaitAsync(TimeSpan.FromSeconds(5));
            }

            File.ReadAllText(_configPath).Should().Be(OriginalConfig);
            File.ReadAllText(first).Should().Be(RestoredConfig);
            File.ReadAllText(second).Should().Be(OriginalConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(_originalRestore);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void sqlite_restore_should_be_rejected_when_current_database_is_postgres()
        {
            Mocker.GetMock<IMainDatabase>().SetupGet(database => database.DatabaseType).Returns(DatabaseType.PostgreSQL);
            var upload = GivenDatabase("sqlite-on-postgres.db");

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(upload));

            AssertHomeUnchanged();
            AssertWorkCleaned();
        }

        [Test]
        public void config_only_restore_should_remain_available_when_current_database_is_postgres()
        {
            Mocker.GetMock<IMainDatabase>().SetupGet(database => database.DatabaseType).Returns(DatabaseType.PostgreSQL);
            var upload = WriteUpload("config-only.xml", Encoding.UTF8.GetBytes(RestoredConfig));

            Subject.Restore(upload);

            File.ReadAllText(_configPath).Should().Be(RestoredConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(_originalRestore);
            AssertLiveDatabaseUnchanged();
            AssertWorkCleaned();
        }

        private void ConfigureRealFileOperations()
        {
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(provider => provider.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(provider => provider.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            disk.Setup(provider => provider.EnsureFolder(It.IsAny<string>())).Callback<string>(CreateFolder);
            disk.Setup(provider => provider.CreateFolder(It.IsAny<string>())).Callback<string>(CreateFolder);
            disk.Setup(provider => provider.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns<string, bool>((path, recursive) => Directory.GetFiles(path, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            disk.Setup(provider => provider.GetFileSize(It.IsAny<string>())).Returns<string>(path => new FileInfo(path).Length);
            disk.Setup(provider => provider.OpenReadStream(It.IsAny<string>())).Returns<string>(File.OpenRead);
            disk.Setup(provider => provider.OpenWriteStream(It.IsAny<string>()))
                .Returns<string>(path => new FileStream(path, FileMode.Create));
            disk.Setup(provider => provider.SaveStream(It.IsAny<Stream>(), It.IsAny<string>()))
                .Callback<Stream, string>((stream, path) =>
                {
                    using var output = File.Create(path);
                    stream.CopyTo(output);
                });
            disk.Setup(provider => provider.ReadAllText(It.IsAny<string>())).Returns<string>(File.ReadAllText);
            disk.Setup(provider => provider.WriteAllText(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>(File.WriteAllText);
            disk.Setup(provider => provider.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((source, destination, overwrite) =>
                {
                    RecordRestoreDirectory(source);
                    RecordRestoreDirectory(destination);
                    _beforeWrite?.Invoke(source, destination);
                    File.Copy(source, destination, overwrite);
                });
            disk.Setup(provider => provider.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((source, destination, overwrite) =>
                {
                    RecordRestoreDirectory(source);
                    RecordRestoreDirectory(destination);
                    _beforeWrite?.Invoke(source, destination);
                    File.Move(source, destination, overwrite);
                });
            disk.Setup(provider => provider.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            disk.Setup(provider => provider.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, bool>((path, recursive) => Directory.Delete(path, recursive));
        }

        private string GivenDatabase(string filename)
        {
            var path = WriteUpload(filename, _databaseSeed);
            using (var connection = OpenDatabase(path))
            {
                connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('restorefixture', 'uploaded payload')");
            }

            return path;
        }

        private string GivenInvalidDatabase(string kind)
        {
            var filename = "bad-" + kind + ".db";
            if (kind == "empty")
            {
                return WriteUpload(filename, Array.Empty<byte>());
            }

            if (kind == "text")
            {
                return WriteUpload(filename, Encoding.UTF8.GetBytes("<Config />"));
            }

            if (kind == "header-only")
            {
                return WriteUpload(filename, Encoding.ASCII.GetBytes("SQLite format 3\0" + new string('\0', 84)));
            }

            if (kind == "truncated")
            {
                return WriteUpload(filename, _databaseSeed.Take(256).ToArray());
            }

            var path = WriteUpload(filename, kind == "wrong-schema" ? Array.Empty<byte>() : _databaseSeed);
            using (var connection = OpenDatabase(path))
            {
                switch (kind)
                {
                    case "wrong-schema":
                        connection.Execute("CREATE TABLE \"Series\" (\"Id\" INTEGER PRIMARY KEY, \"Title\" TEXT)");
                        break;
                    case "missing-books":
                        connection.Execute("DROP TABLE \"Books\"");
                        break;
                    case "missing-user-identity":
                        connection.Execute("DROP TABLE \"Users\"; CREATE TABLE \"Users\" (\"Id\" INTEGER PRIMARY KEY)");
                        break;
                    case "missing-initial-version":
                        connection.Execute("DELETE FROM \"VersionInfo\" WHERE \"Version\" = 1");
                        break;
                    case "unknown-version":
                        connection.Execute("INSERT INTO \"VersionInfo\" (\"Version\", \"AppliedOn\", \"Description\") VALUES (49, CURRENT_TIMESTAMP, 'unknown fixture')");
                        break;
                    case "fractional-version":
                        connection.Execute("INSERT INTO \"VersionInfo\" (\"Version\", \"AppliedOn\", \"Description\") VALUES (42.25, CURRENT_TIMESTAMP, 'fractional fixture')");
                        break;
                    case "future-version":
                        connection.Execute("INSERT INTO \"VersionInfo\" (\"Version\", \"AppliedOn\", \"Description\") VALUES (@version, CURRENT_TIMESTAMP, 'future fixture')", new { version = _latestMigration + 1 });
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }

            return path;
        }

        private string WriteUpload(string filename, byte[] contents)
        {
            var path = Path.Combine(_uploads, filename);
            File.WriteAllBytes(path, contents);
            return path;
        }

        private string GivenZip(string filename, params (string Name, byte[] Contents)[] entries)
        {
            var path = Path.Combine(_uploads, filename);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    using var stream = archive.CreateEntry(entry.Name).Open();
                    stream.Write(entry.Contents);
                }
            }

            return path;
        }

        private static byte[] WithZip64(byte[] original, bool sentinels, ulong? countOverride = null)
        {
            var end = original.Length - 22;
            var count = countOverride ?? BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(end + 10, 2));
            var directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 12, 4));
            var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 16, 4));
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            writer.Write(original, 0, end);
            writer.Write(0x06064b50u);
            writer.Write(44UL);
            writer.Write((ushort)45);
            writer.Write((ushort)45);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(count);
            writer.Write(count);
            writer.Write((ulong)directoryLength);
            writer.Write((ulong)directoryOffset);
            writer.Write(0x07064b50u);
            writer.Write(0u);
            writer.Write((ulong)end);
            writer.Write(1u);
            var legacy = original.AsSpan(end, 22).ToArray();
            if (sentinels)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(8, 2), ushort.MaxValue);
                BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(10, 2), ushort.MaxValue);
                BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(12, 4), uint.MaxValue);
                BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(16, 4), uint.MaxValue);
            }

            writer.Write(legacy);
            writer.Flush();
            return output.ToArray();
        }

        private void AssertHomeUnchanged()
        {
            File.ReadAllText(_configPath).Should().Be(OriginalConfig);
            File.ReadAllBytes(_restorePath).Should().Equal(_originalRestore);
            AssertLiveDatabaseUnchanged();
        }

        private void AssertLiveDatabaseUnchanged()
        {
            File.ReadAllBytes(_liveDatabasePath).Should().Equal(_originalLiveDatabase);
        }

        private void AssertWorkCleaned()
        {
            _createdRestoreDirectories.Should().OnlyContain(path => !Directory.Exists(path));
            Directory.GetDirectories(_home, ".readarr-restore-*").Should().BeEmpty();
            Directory.EnumerateFileSystemEntries(_work).Should().BeEmpty();
        }

        private void CreateFolder(string path)
        {
            RecordRestoreDirectory(path);
            Directory.CreateDirectory(path);
        }

        private void RecordRestoreDirectory(string path)
        {
            var name = Path.GetRelativePath(_home, path).Split(Path.DirectorySeparatorChar)[0];
            if (name.StartsWith(".readarr-restore-", StringComparison.Ordinal))
            {
                var directory = Path.Combine(_home, name);
                if (!_createdRestoreDirectories.Contains(directory))
                {
                    _createdRestoreDirectories.Add(directory);
                }
            }
        }

        private static string ConnectionString(string path) => new SQLiteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            ForeignKeys = false,
            DateTimeKind = DateTimeKind.Utc
        }.ConnectionString;

        private static SQLiteConnection OpenDatabase(string path)
        {
            var connection = new SQLiteConnection(ConnectionString(path));
            connection.Open();
            return connection;
        }
    }
}
