using System;
using System.Data.SQLite;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore
{
    public interface IConnectionStringFactory
    {
        DatabaseConnectionInfo MainDbConnection { get; }
        DatabaseConnectionInfo LogDbConnection { get; }
        DatabaseConnectionInfo CacheDbConnection { get; }
        string GetDatabasePath(string connectionString);
    }

    public class ConnectionStringFactory : IConnectionStringFactory
    {
        private readonly IConfigFileProvider _configFileProvider;

        public ConnectionStringFactory(IAppFolderInfo appFolderInfo, IConfigFileProvider configFileProvider)
        {
            _configFileProvider = configFileProvider;

            var main = _configFileProvider.PostgresMainDbConnectionString;
            var cache = _configFileProvider.PostgresCacheDbConnectionString;
            var logEnabled = _configFileProvider.LogDbEnabled;
            var log = logEnabled ? _configFileProvider.PostgresLogDbConnectionString : null;
            var usesConnectionStrings = main.IsNotNullOrWhiteSpace() || cache.IsNotNullOrWhiteSpace() || log.IsNotNullOrWhiteSpace();
            if (usesConnectionStrings)
            {
                if (_configFileProvider.HasPostgresComponentSettings())
                {
                    throw new ReadarrStartupException("PostgreSQL connection strings cannot be combined with individual PostgreSQL settings.");
                }

                if (main.IsNullOrWhiteSpace() || cache.IsNullOrWhiteSpace() || (logEnabled && log.IsNullOrWhiteSpace()))
                {
                    throw new ReadarrStartupException("PostgreSQL connection strings require MainDbConnectionString and CacheDbConnectionString, plus LogDbConnectionString when database logging is enabled.");
                }

                MainDbConnection = GetPostgresConnectionInfo(main, "main");
                CacheDbConnection = GetPostgresConnectionInfo(cache, "cache");
                LogDbConnection = logEnabled ? GetPostgresConnectionInfo(log, "log") : null;
                return;
            }

            // Keep legacy component selection and defaults when full strings are absent.
            MainDbConnection = _configFileProvider.PostgresHost.IsNotNullOrWhiteSpace() ? GetPostgresConnectionString(_configFileProvider.PostgresMainDb) : GetConnectionString(appFolderInfo.GetDatabase());

            LogDbConnection = _configFileProvider.PostgresHost.IsNotNullOrWhiteSpace() ? GetPostgresConnectionString(_configFileProvider.PostgresLogDb) : GetConnectionString(appFolderInfo.GetLogDatabase());

            CacheDbConnection = _configFileProvider.PostgresHost.IsNotNullOrWhiteSpace() ? GetPostgresConnectionString(_configFileProvider.PostgresCacheDb) : GetConnectionString(appFolderInfo.GetCacheDatabase());
        }

        public DatabaseConnectionInfo MainDbConnection { get; private set; }
        public DatabaseConnectionInfo LogDbConnection { get; private set; }
        public DatabaseConnectionInfo CacheDbConnection { get; private set; }

        public string GetDatabasePath(string connectionString)
        {
            var connectionBuilder = new SQLiteConnectionStringBuilder(connectionString);

            return connectionBuilder.DataSource;
        }

        private static DatabaseConnectionInfo GetConnectionString(string dbPath)
        {
            // Preserve legacy foreign-key behavior independently of native build defaults.
            var connectionBuilder = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                CacheSize = -20000,
                DateTimeKind = DateTimeKind.Utc,
                JournalMode = OsInfo.IsOsx ? SQLiteJournalModeEnum.Truncate : SQLiteJournalModeEnum.Wal,
                Pooling = true,
                ForeignKeys = false,
                Version = 3,
                BusyTimeout = 1000
            };

            if (OsInfo.IsOsx)
            {
                connectionBuilder.Add("Full FSync", true);
            }

            return new DatabaseConnectionInfo(DatabaseType.SQLite, connectionBuilder.ConnectionString);
        }

        private static DatabaseConnectionInfo GetPostgresConnectionInfo(string connectionString, string role)
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    Enlist = false
                };
                if (builder.Host.IsNullOrWhiteSpace() || builder.Database.IsNullOrWhiteSpace())
                {
                    throw new ArgumentException();
                }

                return new DatabaseConnectionInfo(DatabaseType.PostgreSQL, builder.ConnectionString);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            {
                // Provider parser errors may include the password or arbitrary values.
                // Never retain them as an inner exception or echo the supplied string.
                throw new ReadarrStartupException($"The PostgreSQL {role} connection string is invalid; Host and Database are required.");
            }
        }

        private DatabaseConnectionInfo GetPostgresConnectionString(string dbName)
        {
            var connectionBuilder = new NpgsqlConnectionStringBuilder
            {
                Database = dbName,
                Host = _configFileProvider.PostgresHost,
                Username = _configFileProvider.PostgresUser,
                Password = _configFileProvider.PostgresPassword,
                Port = _configFileProvider.PostgresPort,
                Enlist = false
            };

            return new DatabaseConnectionInfo(DatabaseType.PostgreSQL, connectionBuilder.ConnectionString);
        }
    }
}
