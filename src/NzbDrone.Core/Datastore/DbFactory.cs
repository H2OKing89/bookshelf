using System;
using System.Data.Common;
using System.Data.SQLite;
using System.Net.Sockets;
using System.Threading;
using NLog;
using Npgsql;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore
{
    public interface IDbFactory
    {
        IDatabase Create(MigrationType migrationType = MigrationType.Main);
        IDatabase Create(MigrationContext migrationContext);
    }

    public class DbFactory : IDbFactory
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DbFactory));
        private readonly IMigrationController _migrationController;
        private readonly IConnectionStringFactory _connectionStringFactory;
        private readonly IDiskProvider _diskProvider;
        private readonly IRestoreDatabase _restoreDatabaseService;

        static DbFactory()
        {
            InitializeEnvironment();

            TableMapping.Map();
        }

        private static void InitializeEnvironment()
        {
            // Speed up sqlite3 initialization since we don't use the config file and can't rely on preloading.
            Environment.SetEnvironmentVariable("No_Expand", "true");
            Environment.SetEnvironmentVariable("No_SQLiteXmlConfigFile", "true");
            Environment.SetEnvironmentVariable("No_PreLoadSQLite", "true");
            Environment.SetEnvironmentVariable("No_SQLiteFunctions", "true");
        }

        public DbFactory(IMigrationController migrationController,
                         IConnectionStringFactory connectionStringFactory,
                         IDiskProvider diskProvider,
                         IRestoreDatabase restoreDatabaseService)
        {
            _migrationController = migrationController;
            _connectionStringFactory = connectionStringFactory;
            _diskProvider = diskProvider;
            _restoreDatabaseService = restoreDatabaseService;
        }

        public IDatabase Create(MigrationType migrationType = MigrationType.Main)
        {
            return Create(new MigrationContext(migrationType));
        }

        public IDatabase Create(MigrationContext migrationContext)
        {
            DatabaseConnectionInfo connectionInfo;

            switch (migrationContext.MigrationType)
            {
                case MigrationType.Main:
                    {
                        connectionInfo = _connectionStringFactory.MainDbConnection;
                        CreateMain(connectionInfo.ConnectionString, migrationContext, connectionInfo.DatabaseType);

                        break;
                    }

                case MigrationType.Log:
                    {
                        connectionInfo = _connectionStringFactory.LogDbConnection;
                        CreateLog(connectionInfo.ConnectionString, migrationContext, connectionInfo.DatabaseType);

                        break;
                    }

                case MigrationType.Cache:
                    {
                        connectionInfo = _connectionStringFactory.CacheDbConnection;
                        CreateLog(connectionInfo.ConnectionString, migrationContext, connectionInfo.DatabaseType);

                        break;
                    }

                default:
                    {
                        throw new ArgumentException("Invalid MigrationType");
                    }
            }

            var db = new Database(migrationContext.MigrationType.ToString(), () =>
            {
                DbConnection conn;

                if (connectionInfo.DatabaseType == DatabaseType.SQLite)
                {
                    conn = SQLiteFactory.Instance.CreateConnection();
                    conn.ConnectionString = connectionInfo.ConnectionString;
                }
                else
                {
                    return OpenPostgresConnection(connectionInfo.ConnectionString);
                }

                conn.Open();
                return conn;
            });

            return db;
        }

        private void CreateMain(string connectionString, MigrationContext migrationContext, DatabaseType databaseType)
        {
            try
            {
                _restoreDatabaseService.Restore();
                _migrationController.Migrate(connectionString, migrationContext, databaseType);
            }
            catch (SQLiteException e) when (IsCorruptDatabase(e))
            {
                var fileName = _connectionStringFactory.GetDatabasePath(connectionString);

                if (OsInfo.IsOsx)
                {
                    throw new CorruptDatabaseException("Database file: {0} is corrupt, restore from backup if available. See: https://wiki.servarr.com/readarr/faq#i-use-readarr-on-a-mac-and-it-suddenly-stopped-working-what-happened", e, fileName);
                }

                throw new CorruptDatabaseException("Database file: {0} is corrupt, restore from backup if available. See: https://wiki.servarr.com/readarr/faq#i-am-getting-an-error-database-disk-image-is-malformed", e, fileName);
            }
            catch (NpgsqlException e)
            {
                if (e.InnerException is SocketException)
                {
                    var retryCount = 3;

                    while (true)
                    {
                        Logger.Error("Failure to connect to Postgres DB, {0} retries remaining", retryCount);

                        Thread.Sleep(5000);

                        try
                        {
                            _migrationController.Migrate(connectionString, migrationContext, databaseType);
                            return;
                        }
                        catch (Exception ex)
                        {
                            if (--retryCount > 0)
                            {
                                continue;
                            }

                            throw PostgresStartupError(MigrationType.Main, ex);
                        }
                    }
                }
                else
                {
                    throw PostgresStartupError(MigrationType.Main, e);
                }
            }
            catch (Exception e)
            {
                if (databaseType == DatabaseType.PostgreSQL)
                {
                    throw PostgresStartupError(MigrationType.Main, e);
                }

                throw new ReadarrStartupException(e, "Error creating main database");
            }
        }

        private void CreateLog(string connectionString, MigrationContext migrationContext, DatabaseType databaseType)
        {
            try
            {
                _migrationController.Migrate(connectionString, migrationContext, databaseType);
            }
            catch (SQLiteException e) when (IsCorruptDatabase(e))
            {
                var fileName = _connectionStringFactory.GetDatabasePath(connectionString);

                Logger.Error(e, "Logging database is corrupt, attempting to recreate it automatically");

                try
                {
                    _diskProvider.DeleteFile(fileName + "-shm");
                    _diskProvider.DeleteFile(fileName + "-wal");
                    _diskProvider.DeleteFile(fileName + "-journal");
                    _diskProvider.DeleteFile(fileName);
                }
                catch (Exception)
                {
                    Logger.Error("Unable to recreate logging database automatically. It will need to be removed manually.");
                }

                _migrationController.Migrate(connectionString, migrationContext, databaseType);
            }
            catch (Exception e)
            {
                if (databaseType == DatabaseType.PostgreSQL)
                {
                    throw PostgresStartupError(migrationContext.MigrationType, e);
                }

                throw new ReadarrStartupException(e, "Error creating log database");
            }
        }

        private static DbConnection OpenPostgresConnection(string connectionString)
        {
            NpgsqlConnection connection = null;
            try
            {
                connection = new NpgsqlConnection(connectionString);
                connection.Open();
                return connection;
            }
            catch (Exception)
            {
                try
                {
                    connection?.Dispose();
                }
                catch (Exception)
                {
                    // Cleanup failures must not expose connection settings either.
                }

                // Provider errors may contain credentials, server fields or TLS paths
                // without key/value labels. Keep them out of API and command errors.
                throw new NpgsqlException("Unable to open the PostgreSQL database. Check its connection settings and server availability.");
            }
        }

        private static ReadarrStartupException PostgresStartupError(MigrationType database, Exception error)
        {
            var state = (error as PostgresException)?.SqlState;
            var validState = state?.Length == 5;
            if (validState)
            {
                foreach (var character in state)
                {
                    if (!char.IsAsciiDigit(character) && !char.IsAsciiLetterUpper(character))
                    {
                        validState = false;
                        break;
                    }
                }
            }

            var code = validState ? $" (SQLSTATE {state})" : string.Empty;
            return new ReadarrStartupException($"Error creating PostgreSQL {database} database{code}. Check its connection settings and server availability.");
        }

        private static bool IsCorruptDatabase(SQLiteException error)
        {
            var primaryCode = (SQLiteErrorCode)((int)error.ResultCode & 0xff);
            return primaryCode == SQLiteErrorCode.Corrupt || primaryCode == SQLiteErrorCode.NotADb;
        }
    }
}
