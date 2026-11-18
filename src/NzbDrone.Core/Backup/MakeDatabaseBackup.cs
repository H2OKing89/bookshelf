using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Backup
{
    public interface IMakeDatabaseBackup
    {
        void BackupDatabase(IDatabase database, string targetDirectory);
    }

    public class MakeDatabaseBackup : IMakeDatabaseBackup
    {
        private readonly Logger _logger;

        public MakeDatabaseBackup(Logger logger)
        {
            _logger = logger;
        }

        public void BackupDatabase(IDatabase database, string targetDirectory)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
            using var source = database.OpenConnection();
            if (source is not SQLiteConnection sourceConnection)
            {
                throw new ArgumentException("A SQLite connection is required for a database backup.", nameof(database));
            }

            var builder = new SQLiteConnectionStringBuilder(sourceConnection.ConnectionString);
            var sourcePath = Path.GetFullPath(builder.DataSource);
            var backupPath = Path.GetFullPath(Path.Combine(targetDirectory, Path.GetFileName(sourcePath)));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(ResolveFilePath(sourcePath), ResolveFilePath(backupPath), comparison))
            {
                throw new ArgumentException("A database cannot be backed up over its source file.", nameof(targetDirectory));
            }

            EnsureNoDestinationJournal(backupPath);
            var stagingPath = Path.Combine(Path.GetDirectoryName(backupPath), "." + Path.GetFileName(backupPath) + ".backup-" + Guid.NewGuid().ToString("N") + ".part");
            var created = false;
            var owned = false;
            try
            {
                using (var reservation = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    owned = true;
                }

                builder.DataSource = stagingPath;
                builder.Pooling = false;
                builder.ReadOnly = false;
                builder.FailIfMissing = false;

                // A backup is a standalone rollback-journal database. WAL sidecars
                // must not be required when its bytes are archived and restored.
                builder.JournalMode = SQLiteJournalModeEnum.Truncate;
                using (var backupConnection = new SQLiteConnection(builder.ConnectionString))
                {
                    backupConnection.Open();
                    CopyDatabase(sourceConnection, backupConnection);
                    using var command = backupConnection.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode=TRUNCATE";
                    var journalMode = Convert.ToString(command.ExecuteScalar());
                    if (!string.Equals(journalMode, "truncate", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("The backup database did not leave WAL mode.");
                    }

                    command.CommandText = "PRAGMA quick_check";
                    if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.Ordinal))
                    {
                        throw new IOException("The backup database failed its integrity check.");
                    }
                }

                EnsureNoDestinationJournal(backupPath);
                File.Move(stagingPath, backupPath, true);
                created = false;
                _logger.Debug("Database backup completed: {0}", backupPath);
            }
            finally
            {
                if (created)
                {
                    File.Delete(stagingPath);
                }

                // Only this invocation's unique staging files are owned here.
                foreach (var suffix in owned ? new[] { "-journal", "-wal", "-shm" } : Array.Empty<string>())
                {
                    File.Delete(stagingPath + suffix);
                }
            }
        }

        private static void CopyDatabase(SQLiteConnection source, SQLiteConnection destination)
        {
            var stalled = new Stopwatch();
            var previousRemaining = int.MaxValue;
            var interrupted = false;
            var timeout = TimeSpan.FromSeconds(source.DefaultTimeout);
            try
            {
                source.BackupDatabase(
                    destination,
                    "main",
                    "main",
                    256,
                    (from, fromName, to, toName, pages, remaining, total, retry) =>
                    {
                        if (!retry && remaining < previousRemaining)
                        {
                            previousRemaining = remaining;
                            stalled.Reset();
                        }
                        else
                        {
                            stalled.Start();
                        }

                        // A zero command timeout explicitly allows an unlimited
                        // wait. Otherwise only real copy progress resets the budget.
                        interrupted = timeout > TimeSpan.Zero && stalled.Elapsed >= timeout;
                        return !interrupted;
                    },
                    100);
            }
            catch (Exception error) when (interrupted)
            {
                throw new TimeoutException("The database backup could not make progress before its lock timeout.", error);
            }

            // The provider may return normally after a callback cancels a backup.
            // Never publish its rolled-back or incomplete destination in that case.
            if (interrupted)
            {
                throw new TimeoutException("The database backup could not make progress before its lock timeout.");
            }
        }

        private static void EnsureNoDestinationJournal(string path)
        {
            if (File.Exists(path + "-wal") || File.Exists(path + "-shm") ||
                (File.Exists(path + "-journal") && new FileInfo(path + "-journal").Length > 0))
            {
                throw new IOException("An existing backup has SQLite journal sidecars and cannot be replaced safely.");
            }
        }

        private static string ResolveFilePath(string path)
        {
            var file = new FileInfo(path);
            var target = file.LinkTarget == null ? null : file.ResolveLinkTarget(true);
            var resolved = target?.FullName ?? file.FullName;
            return Path.Combine(ResolveDirectoryPath(Path.GetDirectoryName(resolved)), Path.GetFileName(resolved));
        }

        private static string ResolveDirectoryPath(string path)
        {
            var directory = new DirectoryInfo(path);
            var target = directory.LinkTarget == null ? null : directory.ResolveLinkTarget(true);
            if (target != null)
            {
                return ResolveDirectoryPath(target.FullName);
            }

            return directory.Parent == null
                ? directory.FullName
                : Path.Combine(ResolveDirectoryPath(directory.Parent.FullName), directory.Name);
        }
    }
}
