using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Backup
{
    public interface IBackupService
    {
        void Backup(BackupType backupType);
        List<Backup> GetBackups();
        void Restore(string backupFileName);
        string GetBackupFolder();
        string GetBackupFolder(BackupType backupType);
    }

    public class BackupService : IBackupService, IExecute<BackupCommand>
    {
        private readonly IMainDatabase _maindDb;
        private readonly IMakeDatabaseBackup _makeDatabaseBackup;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IArchiveService _archiveService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly IBackupRestoreValidator _restoreValidator;
        private static readonly object RestoreLock = new object();

        private string _backupTempFolder;

        public static readonly Regex BackupFileRegex = new Regex(@"readarr_backup_(v[0-9.]+_)?[._0-9]+\.zip", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public BackupService(IMainDatabase maindDb,
                             IMakeDatabaseBackup makeDatabaseBackup,
                             IDiskTransferService diskTransferService,
                             IDiskProvider diskProvider,
                             IAppFolderInfo appFolderInfo,
                             IArchiveService archiveService,
                             IConfigService configService,
                             IBackupRestoreValidator restoreValidator,
                             Logger logger)
        {
            _maindDb = maindDb;
            _makeDatabaseBackup = makeDatabaseBackup;
            _diskTransferService = diskTransferService;
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _archiveService = archiveService;
            _configService = configService;
            _logger = logger;
            _restoreValidator = restoreValidator;

            _backupTempFolder = Path.Combine(_appFolderInfo.TempFolder, "readarr_backup");
        }

        public void Backup(BackupType backupType)
        {
            _logger.ProgressInfo("Starting Backup");

            var backupFolder = GetBackupFolder(backupType);

            _diskProvider.EnsureFolder(_backupTempFolder);
            _diskProvider.EnsureFolder(backupFolder);

            if (!_diskProvider.FolderWritable(backupFolder))
            {
                throw new UnauthorizedAccessException($"Backup folder {backupFolder} is not writable");
            }

            var dateNow = DateTime.Now;
            var backupFilename = $"readarr_backup_v{BuildInfo.Version}_{dateNow:yyyy.MM.dd_HH.mm.ss}.zip";
            var backupPath = Path.Combine(backupFolder, backupFilename);

            Cleanup();

            if (backupType != BackupType.Manual)
            {
                CleanupOldBackups(backupType);
            }

            BackupConfigFile();
            BackupDatabase();
            CreateVersionInfo(dateNow);

            _logger.ProgressDebug("Creating backup zip");

            // Delete journal file created during database backup
            _diskProvider.DeleteFile(Path.Combine(_backupTempFolder, "readarr.db-journal"));

            _archiveService.CreateZip(backupPath, _diskProvider.GetFiles(_backupTempFolder, false));

            Cleanup();

            _logger.ProgressDebug("Backup zip created");
        }

        public List<Backup> GetBackups()
        {
            var backups = new List<Backup>();

            foreach (var backupType in Enum.GetValues(typeof(BackupType)).Cast<BackupType>())
            {
                var folder = GetBackupFolder(backupType);

                if (_diskProvider.FolderExists(folder))
                {
                    backups.AddRange(GetBackupFiles(folder).Select(b => new Backup
                    {
                        Name = Path.GetFileName(b),
                        Type = backupType,
                        Size = _diskProvider.GetFileSize(b),
                        Time = _diskProvider.FileGetLastWrite(b)
                    }));
                }
            }

            return backups;
        }

        public void Restore(string backupFileName)
        {
            // A restore publishes a config/pending-database pair. Other restores in this
            // process must not interleave their snapshots, replacement or rollback.
            lock (RestoreLock)
            {
                RestoreInternal(backupFileName);
            }
        }

        private void RestoreInternal(string backupFileName)
        {
            // Keep all staging/preimages private and on the destination filesystem.
            // Never consume or delete another instance's old shared temp directory.
            var workspace = Path.Combine(_appFolderInfo.GetAppDataPath(), ".readarr-restore-" + Guid.NewGuid().ToString("N"));
            var retainWorkspace = false;
            try
            {
                _diskProvider.EnsureFolder(workspace);
                if (OsInfo.IsNotWindows)
                {
                    _diskProvider.SetPermissions(workspace, "700", null);
                }

                var payloads = new List<RestorePayload>();
                var extension = Path.GetExtension(backupFileName);
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractRestorePayloads(backupFileName, workspace, payloads);
                }
                else
                {
                    var config = extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
                    var copy = Path.Combine(workspace, config ? "config.xml" : "readarr.db");
                    var limit = config ? BackupRestoreValidator.MaximumConfigBytes : BackupRestoreValidator.MaximumDatabaseBytes;
                    if (_diskProvider.GetFileSize(backupFileName) > limit)
                    {
                        throw InvalidBackup("Backup payload exceeds its restore size limit");
                    }

                    _diskProvider.CopyFile(backupFileName, copy);
                    payloads.Add(new RestorePayload(copy, config ? _appFolderInfo.GetConfigPath() : _appFolderInfo.GetDatabaseRestore(), config));
                }

                foreach (var payload in payloads)
                {
                    if (payload.IsConfig)
                    {
                        _restoreValidator.ValidateConfig(payload.Source);
                    }
                    else
                    {
                        if (_maindDb.DatabaseType != DatabaseType.SQLite)
                        {
                            throw InvalidBackup("SQLite database backups cannot be restored while using PostgreSQL");
                        }

                        _restoreValidator.ValidateDatabase(payload.Source);
                    }
                }

                // ConfigFileProvider can rewrite defaults asynchronously after the
                // host becomes ready. Serialize the entire publication/rollback
                // against its read-modify-write transactions, not just restores.
                // The outer restore lock is sufficient for database-only restores.
                var publicationLock = payloads.Any(payload => payload.IsConfig) ? ConfigFileProvider.FileLock : RestoreLock;
                lock (publicationLock)
                {
                    // Every payload is valid before any destination can be replaced. Keep
                    // preimages until the entire publication succeeds, including an existing
                    // pending restore. MoveFile(overwrite) can delete before a failing move.
                    foreach (var payload in payloads)
                    {
                        payload.HadOriginal = _diskProvider.FileExists(payload.Destination);
                        if (payload.HadOriginal)
                        {
                            payload.Previous = Path.Combine(workspace, "previous-" + Path.GetFileName(payload.Destination));
                            _diskProvider.CopyFile(payload.Destination, payload.Previous);
                        }
                    }

                    try
                    {
                        foreach (var payload in payloads.OrderBy(payload => payload.IsConfig))
                        {
                            payload.Attempted = true;
                            _diskProvider.MoveFile(payload.Source, payload.Destination, true);
                        }
                    }
                    catch
                    {
                        foreach (var payload in payloads.Where(payload => payload.Attempted).Reverse())
                        {
                            try
                            {
                                if (payload.HadOriginal)
                                {
                                    _diskProvider.CopyFile(payload.Previous, payload.Destination, true);
                                }
                                else
                                {
                                    _diskProvider.DeleteFile(payload.Destination);
                                }
                            }
                            catch (Exception rollbackError)
                            {
                                retainWorkspace = true;
                                _logger.Error(rollbackError, "Unable to roll back restore file; recovery files retained in {0}", workspace);
                            }
                        }

                        if (retainWorkspace)
                        {
                            throw new RestoreBackupFailedException(HttpStatusCode.InternalServerError,
                                "Restore failed and could not be fully rolled back; recovery files were retained in a private restore directory");
                        }

                        throw;
                    }
                }
            }
            finally
            {
                if (!retainWorkspace && _diskProvider.FolderExists(workspace))
                {
                    _diskProvider.DeleteFolder(workspace, true);
                }
            }
        }

        private void ExtractRestorePayloads(string backupFileName, string workspace, List<RestorePayload> payloads)
        {
            try
            {
                using var stream = _diskProvider.OpenReadStream(backupFileName);
                BackupArchivePreflight.Validate(stream);
                using var archive = new ZipFile(stream);
                if (archive.Count > 64)
                {
                    throw InvalidBackup("Backup archive contains too many entries");
                }

                var entries = new List<ZipEntry>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipEntry entry in archive)
                {
                    var name = entry.Name;
                    var mode = (entry.ExternalFileAttributes >> 16) & 0xF000;
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl) ||
                        name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == "." || name == ".." ||
                        !entry.IsFile || entry.IsCrypted || !names.Add(name) ||
                        ((entry.HostSystem == 3 || entry.HostSystem == 19) && mode != 0 && mode != 0x8000) ||
                        entry.Size < 0 || entry.Size > BackupRestoreValidator.MaximumDatabaseBytes)
                    {
                        throw InvalidBackup("Backup archive contains unsafe, duplicate or unsupported entries");
                    }

                    var config = name.Equals("config.xml", StringComparison.OrdinalIgnoreCase);
                    var database = name.Equals("readarr.db", StringComparison.OrdinalIgnoreCase);
                    if (config || database)
                    {
                        if (config && entry.Size > BackupRestoreValidator.MaximumConfigBytes)
                        {
                            throw InvalidBackup("Backup config exceeds its restore size limit");
                        }

                        entries.Add(entry);
                    }
                }

                if (entries.Count == 0)
                {
                    throw new RestoreBackupFailedException(HttpStatusCode.NotFound, "Unable to find config or database files in backup");
                }

                // Preflight every name/duplicate/size before decompressing anything.
                foreach (var entry in entries)
                {
                    var config = entry.Name.Equals("config.xml", StringComparison.OrdinalIgnoreCase);
                    var destination = Path.Combine(workspace, config ? "config.xml" : "readarr.db");
                    var limit = config ? BackupRestoreValidator.MaximumConfigBytes : BackupRestoreValidator.MaximumDatabaseBytes;
                    using var input = archive.GetInputStream(entry);
                    using var output = _diskProvider.OpenWriteStream(destination);
                    var crc = new Crc32();
                    var buffer = new byte[81920];
                    long length = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        length += count;
                        if (length > limit || length > entry.Size)
                        {
                            throw InvalidBackup("Backup payload exceeds its declared or supported size");
                        }

                        crc.Update(new ArraySegment<byte>(buffer, 0, count));
                        output.Write(buffer, 0, count);
                    }

                    if (length != entry.Size || crc.Value != entry.Crc)
                    {
                        throw InvalidBackup("Backup archive payload failed its integrity check");
                    }

                    payloads.Add(new RestorePayload(
                        destination,
                        config ? _appFolderInfo.GetConfigPath() : _appFolderInfo.GetDatabaseRestore(),
                        config));
                }
            }
            catch (ZipException)
            {
                throw InvalidBackup("Backup archive is not a valid supported ZIP file");
            }
        }

        private static RestoreBackupFailedException InvalidBackup(string message)
        {
            return new RestoreBackupFailedException(HttpStatusCode.BadRequest, message);
        }

        private sealed class RestorePayload
        {
            public RestorePayload(string source, string destination, bool isConfig)
            {
                Source = source;
                Destination = destination;
                IsConfig = isConfig;
            }

            public string Source { get; }
            public string Destination { get; }
            public bool IsConfig { get; }
            public bool HadOriginal { get; set; }
            public bool Attempted { get; set; }
            public string Previous { get; set; }
        }

        public string GetBackupFolder()
        {
            var backupFolder = _configService.BackupFolder;

            if (Path.IsPathRooted(backupFolder))
            {
                return backupFolder;
            }

            return Path.Combine(_appFolderInfo.GetAppDataPath(), backupFolder);
        }

        public string GetBackupFolder(BackupType backupType)
        {
            return Path.Combine(GetBackupFolder(), backupType.ToString().ToLower());
        }

        private void Cleanup()
        {
            if (_diskProvider.FolderExists(_backupTempFolder))
            {
                _diskProvider.EmptyFolder(_backupTempFolder);
            }
        }

        private void BackupDatabase()
        {
            if (_maindDb.DatabaseType == DatabaseType.SQLite)
            {
                _logger.ProgressDebug("Backing up database");

                _makeDatabaseBackup.BackupDatabase(_maindDb, _backupTempFolder);
            }
        }

        private void BackupConfigFile()
        {
            _logger.ProgressDebug("Backing up config.xml");

            var configFile = _appFolderInfo.GetConfigPath();
            var tempConfigFile = Path.Combine(_backupTempFolder, Path.GetFileName(configFile));

            lock (ConfigFileProvider.FileLock)
            {
                _diskTransferService.TransferFile(configFile, tempConfigFile, TransferMode.Copy);
            }
        }

        private void CreateVersionInfo(DateTime dateNow)
        {
            var tempFile = Path.Combine(_backupTempFolder, "INFO");

            var builder = new StringBuilder();
            builder.AppendLine($"v{BuildInfo.Version}");
            builder.AppendLine($"{dateNow:yyyy-MM-dd HH:mm:ss}");

            _diskProvider.WriteAllText(tempFile, builder.ToString());
        }

        private void CleanupOldBackups(BackupType backupType)
        {
            var retention = _configService.BackupRetention;

            _logger.Debug("Cleaning up backup files older than {0} days", retention);
            var files = GetBackupFiles(GetBackupFolder(backupType));

            foreach (var file in files)
            {
                var lastWriteTime = _diskProvider.FileGetLastWrite(file);

                if (lastWriteTime.AddDays(retention) < DateTime.UtcNow)
                {
                    _logger.Debug("Deleting old backup file: {0}", file);
                    _diskProvider.DeleteFile(file);
                }
            }

            _logger.Debug("Finished cleaning up old backup files");
        }

        private IEnumerable<string> GetBackupFiles(string path)
        {
            var files = _diskProvider.GetFiles(path, false);

            return files.Where(f => BackupFileRegex.IsMatch(f));
        }

        public void Execute(BackupCommand message)
        {
            Backup(message.Type);
        }
    }
}
