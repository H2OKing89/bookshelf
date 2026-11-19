using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using Npgsql;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Authentication
{
    public static class PasswordRecoveryDatabase
    {
        public static IDatabase Create(DatabaseConnectionInfo info)
        {
            try
            {
                using (var connection = Open(info, true))
                {
                    var expected = typeof(NzbDroneMigrationBase).Assembly.GetTypes()
                        .Where(type => type.IsSubclassOf(typeof(NzbDroneMigrationBase)))
                        .Select(type => type.GetCustomAttribute<MigrationAttribute>())
                        .Where(attribute => attribute != null)
                        .Select(attribute => attribute.Version).ToHashSet();
                    var applied = connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\"").ToList();
                    if (applied.Count != expected.Count || !expected.SetEquals(applied))
                    {
                        throw new InvalidOperationException();
                    }

                    // An existing current schema is required; never initialize or migrate a home.
                    connection.Query("SELECT \"Id\", \"Identifier\", \"Username\", \"Password\", \"Salt\", \"Iterations\" FROM \"Users\" WHERE 1 = 0").ToList();
                    foreach (var table in new[] { "AuthorMetadata", "Authors", "Books", "Editions", "BookFiles", "History", "Config", "RootFolders" })
                    {
                        connection.Query($"SELECT \"Id\" FROM \"{table}\" WHERE 1 = 0").ToList();
                    }
                }

                if (TableMapping.Mapper.TableNameMapping(typeof(User)) == null)
                {
                    TableMapping.Map();
                }

                return new Database("Password recovery", () => Open(info, false));
            }
            catch (Exception)
            {
                throw new InvalidOperationException("Password recovery requires an existing compatible main database. Check the selected home and database settings.");
            }
        }

        private static IDbConnection Open(DatabaseConnectionInfo info, bool readOnly)
        {
            IDbConnection connection = null;
            try
            {
                if (info.DatabaseType == DatabaseType.SQLite)
                {
                    var builder = new SQLiteConnectionStringBuilder(info.ConnectionString)
                    {
                        ReadOnly = readOnly,
                        FailIfMissing = true,
                        Pooling = false,
                        DefaultTimeout = 5
                    };
                    if (!File.Exists(builder.DataSource))
                    {
                        throw new InvalidOperationException();
                    }

                    // Validation must not change the existing database's journal mode.
                    builder.Remove("journal mode");
                    connection = new SQLiteConnection(builder.ConnectionString);
                }
                else
                {
                    var builder = new NpgsqlConnectionStringBuilder(info.ConnectionString)
                    {
                        Pooling = false, Enlist = false, Timeout = 5, CommandTimeout = 5
                    };
                    connection = new NpgsqlConnection(builder.ConnectionString);
                }

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
                    // Provider disposal errors can also contain credentials.
                }

                throw new InvalidOperationException("Unable to open the existing password recovery database.");
            }
        }
    }
}
