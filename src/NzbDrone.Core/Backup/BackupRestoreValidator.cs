using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentMigrator;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Backup
{
    public interface IBackupRestoreValidator
    {
        void ValidateConfig(string file);
        void ValidateDatabase(string file);
    }

    public class BackupRestoreValidator : IBackupRestoreValidator
    {
        public const long MaximumConfigBytes = 1024 * 1024;
        public const long MaximumDatabaseBytes = 8L * 1024 * 1024 * 1024;

        private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");
        private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(30);
        private static readonly HashSet<long> KnownMigrations = typeof(NzbDroneMigrationBase).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(NzbDroneMigrationBase)))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>())
            .Where(attribute => attribute != null)
            .Select(attribute => attribute.Version)
            .ToHashSet();

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>
        {
            { "VersionInfo", new[] { "Version" } },
            { "Config", new[] { "Key", "Value" } },
            { "AuthorMetadata", new[] { "Id", "ForeignAuthorId" } },
            { "Authors", new[] { "Id", "AuthorMetadataId" } },
            { "Books", new[] { "Id", "AuthorMetadataId", "ForeignBookId" } },
            { "Editions", new[] { "Id", "BookId", "ForeignEditionId" } },
            { "BookFiles", new[] { "Id", "EditionId", "Path" } },
            { "History", new[] { "Id", "BookId", "AuthorId" } },
            { "Users", new[] { "Id", "Identifier", "Username", "Password" } },
            { "RootFolders", new[] { "Id", "Path" } }
        };

        private readonly IDiskProvider _diskProvider;

        public BackupRestoreValidator(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        public void ValidateConfig(string file)
        {
            if (_diskProvider.GetFileSize(file) > MaximumConfigBytes)
            {
                throw InvalidConfig();
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumConfigBytes
                };
                using var stream = _diskProvider.OpenReadStream(file);
                using var reader = XmlReader.Create(stream, settings);
                var document = XDocument.Load(reader);
                if (document.Root?.Name != XName.Get(ConfigFileProvider.CONFIG_ELEMENT_NAME) ||
                    document.Descendants(ConfigFileProvider.CONFIG_ELEMENT_NAME).Count() != 1)
                {
                    throw InvalidConfig();
                }
            }
            catch (XmlException)
            {
                // Parser messages can contain the uploaded configuration or credentials.
                throw InvalidConfig();
            }
        }

        public void ValidateDatabase(string file)
        {
            var length = _diskProvider.GetFileSize(file);
            if (length < 100 || length > MaximumDatabaseBytes)
            {
                throw InvalidDatabase();
            }

            using (var stream = _diskProvider.OpenReadStream(file))
            {
                var header = new byte[SqliteHeader.Length];
                var read = 0;
                while (read < header.Length)
                {
                    var count = stream.Read(header, read, header.Length - read);
                    if (count == 0)
                    {
                        throw InvalidDatabase();
                    }

                    read += count;
                }

                if (!header.SequenceEqual(SqliteHeader))
                {
                    throw InvalidDatabase();
                }
            }

            var elapsed = Stopwatch.StartNew();
            try
            {
                var builder = new SQLiteConnectionStringBuilder
                {
                    DataSource = file,
                    ReadOnly = true,
                    FailIfMissing = true,
                    Pooling = false,
                    DefaultTimeout = 5
                };
                using var connection = new SQLiteConnection(builder.ConnectionString);
                connection.ProgressOps = 1000;
                connection.Progress += (sender, args) =>
                {
                    if (elapsed.Elapsed >= ValidationTimeout)
                    {
                        args.ReturnCode = SQLiteProgressReturnCode.Interrupt;
                    }
                };
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA integrity_check(1)";
                if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                {
                    throw InvalidDatabase();
                }

                foreach (var table in RequiredColumns)
                {
                    command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @Name COLLATE NOCASE";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@Name", table.Key);
                    var definition = command.ExecuteScalar() as string;
                    if (definition == null || definition.TrimStart().StartsWith("CREATE VIRTUAL", StringComparison.OrdinalIgnoreCase))
                    {
                        throw InvalidDatabase();
                    }

                    command.Parameters.Clear();
                    command.CommandText = $"PRAGMA table_info(\"{table.Key}\")";
                    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            columns.Add(reader.GetString(1));
                        }
                    }

                    if (!table.Value.All(columns.Contains))
                    {
                        throw InvalidDatabase();
                    }
                }

                command.CommandText = "SELECT \"Version\", typeof(\"Version\") FROM \"VersionInfo\"";
                var applied = new HashSet<long>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1) != "integer")
                        {
                            throw InvalidDatabase();
                        }

                        var version = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                        if (!KnownMigrations.Contains(version) || !applied.Add(version))
                        {
                            throw new RestoreBackupFailedException(HttpStatusCode.BadRequest, "Backup database contains unsupported migrations");
                        }
                    }
                }

                if (!applied.Contains(1) || elapsed.Elapsed >= ValidationTimeout)
                {
                    throw InvalidDatabase();
                }
            }
            catch (SQLiteException)
            {
                throw InvalidDatabase();
            }
            catch (FormatException)
            {
                throw InvalidDatabase();
            }
            catch (InvalidCastException)
            {
                throw InvalidDatabase();
            }
            catch (OverflowException)
            {
                throw InvalidDatabase();
            }
        }

        private static RestoreBackupFailedException InvalidConfig()
        {
            return new RestoreBackupFailedException(HttpStatusCode.BadRequest, "Backup config file is not a valid Config document");
        }

        private static RestoreBackupFailedException InvalidDatabase()
        {
            return new RestoreBackupFailedException(HttpStatusCode.BadRequest, "Backup database is not a valid, supported Bookshelf/Readarr main database");
        }
    }
}
