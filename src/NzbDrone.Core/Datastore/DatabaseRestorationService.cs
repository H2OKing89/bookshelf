using System;
using System.Collections.Generic;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Backup;

namespace NzbDrone.Core.Datastore
{
    public interface IRestoreDatabase
    {
        void Restore();
    }

    public class DatabaseRestorationService : IRestoreDatabase
    {
        private static readonly object RestoreLock = new object();
        private static readonly string[] DatabaseFileSuffixes = { "", "-shm", "-wal", "-journal" };
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IBackupRestoreValidator _restoreValidator;
        private readonly IConnectionStringFactory _connectionStringFactory;
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DatabaseRestorationService));

        public DatabaseRestorationService(IDiskProvider diskProvider,
                                          IAppFolderInfo appFolderInfo,
                                          IBackupRestoreValidator restoreValidator,
                                          IConnectionStringFactory connectionStringFactory)
        {
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _restoreValidator = restoreValidator;
            _connectionStringFactory = connectionStringFactory;
        }

        public void Restore()
        {
            lock (RestoreLock)
            {
                RestoreDatabase();
            }
        }

        private void RestoreDatabase()
        {
            var dbRestorePath = _appFolderInfo.GetDatabaseRestore();

            if (!_diskProvider.FileExists(dbRestorePath))
            {
                return;
            }

            if (_connectionStringFactory.MainDbConnection.DatabaseType != DatabaseType.SQLite)
            {
                throw new RestoreBackupFailedException(HttpStatusCode.BadRequest, "A SQLite database backup cannot be restored while PostgreSQL is configured");
            }

            _restoreValidator.ValidateDatabase(dbRestorePath);

            var dbPath = _appFolderInfo.GetDatabase();
            var backupSuffix = ".pre-restore-" + Guid.NewGuid().ToString("N");
            var backupPath = dbPath + backupSuffix;
            var movedFiles = new List<KeyValuePair<string, string>>();
            var restoreMoveStarted = false;

            try
            {
                Logger.Info("Restoring Database");

                // Metadata cache entries belong to the old main database and must be rebuilt.
                foreach (var databasePath in new[] { dbPath, _appFolderInfo.GetCacheDatabase() })
                {
                    foreach (var suffix in DatabaseFileSuffixes)
                    {
                        var source = databasePath + suffix;
                        if (_diskProvider.FileExists(source))
                        {
                            var destination = databasePath + backupSuffix + suffix;

                            // Track the attempt too: a provider can throw after moving the file.
                            movedFiles.Add(new KeyValuePair<string, string>(source, destination));
                            _diskProvider.MoveFile(source, destination);
                        }
                    }
                }

                restoreMoveStarted = true;
                _diskProvider.MoveFile(dbRestorePath, dbPath);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to restore database");

                if (restoreMoveStarted)
                {
                    TryRollback(() =>
                    {
                        if (_diskProvider.FileExists(dbPath))
                        {
                            // Keep the candidate recoverable even if its move completed before throwing.
                            var recoveryPath = _diskProvider.FileExists(dbRestorePath) ? backupPath + ".failed-restore" : dbRestorePath;
                            _diskProvider.MoveFile(dbPath, recoveryPath);
                        }
                    });
                }

                for (var i = movedFiles.Count - 1; i >= 0; i--)
                {
                    var file = movedFiles[i];
                    TryRollback(() =>
                    {
                        if (_diskProvider.FileExists(file.Value) && !_diskProvider.FileExists(file.Key))
                        {
                            _diskProvider.MoveFile(file.Value, file.Key);
                        }
                    });
                }

                throw;
            }
        }

        private static void TryRollback(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                // Continue restoring the remaining files; never delete a recovery copy.
                Logger.Error(e, "Failed to roll back a database restore file; recovery files have been retained");
            }
        }
    }
}
