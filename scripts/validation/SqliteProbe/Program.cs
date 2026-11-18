using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

internal static class Program
{
    private static readonly Dictionary<string, bool> Checks = new();
    private static readonly Dictionary<string, object> Metadata = new();
    private static readonly List<object> Measurements = new();
    private static string Fixture;
    private static bool NativeOpened;

    private static async Task<int> Main(string[] args)
    {
        var fixture = Path.GetFullPath(args.Length == 1 ? args[0] : throw new ArgumentException("An owned fixture directory is required."));
        Fixture = fixture;
        if (!Directory.Exists(fixture) || Directory.EnumerateFiles(fixture, "*.db").Any())
        {
            throw new InvalidOperationException("The probe requires a fresh owned fixture directory.");
        }

        foreach (var name in new[] { "No_Expand", "No_SQLiteXmlConfigFile", "No_PreLoadSQLite", "No_SQLiteFunctions" })
        {
            Environment.SetEnvironmentVariable(name, "true");
        }

        try
        {
            AssemblyLoader.RegisterSQLiteResolver();
            var factory = new ConnectionStringFactory(new Folders(fixture), DispatchProxy.Create<IConfigFileProvider, ConfigProxy>());
            var main = factory.MainDbConnection.ConnectionString;
            Metadata["managedProviderVersion"] = typeof(SQLiteConnection).Assembly.GetName().Version?.ToString();
            Metadata["managedProviderLocation"] = Path.GetRelativePath(AppContext.BaseDirectory, typeof(SQLiteConnection).Assembly.Location);
            Metadata["runtimeVersion"] = Environment.Version.ToString();
            Metadata["busyTimeoutUnits"] = "milliseconds; command timeout is a separate managed retry budget in seconds";
            using (var connection = Open(main))
            {
                NativeOpened = true;
                Metadata["engineVersion"] = Scalar(connection, "SELECT sqlite_version()").ToString();
                Metadata["engineSourceId"] = Scalar(connection, "SELECT sqlite_source_id()").ToString();
                Metadata["compileOptions"] = Strings(connection, "PRAGMA compile_options").OrderBy(value => value, StringComparer.Ordinal).ToArray();
                Metadata["configuredBusyTimeoutMilliseconds"] = Convert.ToInt32(Scalar(connection, "PRAGMA busy_timeout"));
                Metadata["configuredDefaultCommandTimeoutSeconds"] = connection.DefaultTimeout;
                Check("application_connection_uses_wal_on_linux", Scalar(connection, "PRAGMA journal_mode").ToString().Equals("wal", StringComparison.OrdinalIgnoreCase));
                Check("application_connection_uses_requested_cache_budget", Convert.ToInt32(Scalar(connection, "PRAGMA cache_size")) == -20000);
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, "libe_sqlite3.so")))
                {
                    Check("bundled_engine_has_expected_version", Metadata["engineVersion"].ToString() == "3.53.4");
                    Check("application_connection_uses_donor_busy_timeout", (int)Metadata["configuredBusyTimeoutMilliseconds"] == 1000);
                }
                Execute(connection, "CREATE TABLE Authors (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);" +
                    "CREATE TABLE Books (Id INTEGER PRIMARY KEY, AuthorId INTEGER NOT NULL, Title TEXT NOT NULL);" +
                    "CREATE TABLE BookFiles (Id INTEGER PRIMARY KEY, BookId INTEGER NOT NULL, Part INTEGER NOT NULL, Path TEXT NOT NULL, UNIQUE(BookId,Part));" +
                    "CREATE TABLE History (Id INTEGER PRIMARY KEY, BookId INTEGER NOT NULL, EventType INTEGER NOT NULL);" +
                    "CREATE TABLE Marker (Id INTEGER PRIMARY KEY, Value INTEGER NOT NULL);" +
                    "INSERT INTO Authors VALUES (1,'Synthetic Author'); INSERT INTO Marker VALUES (1,0);");
            }

            Check("provider_loaded_from_owned_package_clone", Path.GetDirectoryName(typeof(SQLiteConnection).Assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            Metadata["nativeSqliteMappings"] = NativeMappings();
            Metadata["nativeSqliteChecksums"] = ((string[])Metadata["nativeSqliteMappings"]).ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            Check("native_sqlite_mapping_was_observed", ((string[])Metadata["nativeSqliteMappings"]).Length > 0);
            Checkpoint("initialized");
            foreach (var timeout in new[] { 100, 1000 })
            {
                await MeasureLock(main, timeout, releaseAfterMilliseconds: 350, commandSeconds: 3, "released");
                Checkpoint("released_lock_" + timeout);
                await MeasureLock(main, timeout, releaseAfterMilliseconds: 1600, commandSeconds: 1, "sustained");
                Checkpoint("sustained_lock_" + timeout);
            }

            Checkpoint("mixed_load_starting");
            await MixedLoad(main, factory.LogDbConnection.ConnectionString, factory.CacheDbConnection.ConnectionString, fixture);
            Checkpoint("mixed_load_completed");
            await CancelAndRollback(main);
            Checkpoint("cancellation_completed");
            using (var connection = Open(main))
            {
                Check("database_integrity_after_all_work", Scalar(connection, "PRAGMA integrity_check").ToString() == "ok");
                Check("source_journal_still_wal", Scalar(connection, "PRAGMA journal_mode").ToString().Equals("wal", StringComparison.OrdinalIgnoreCase));
            }

            Check("probe_completed", true);
        }
        catch (Exception error)
        {
            Metadata["failureType"] = error.GetType().Name;
            var types = new List<string>();
            for (var cause = error; cause != null; cause = cause.InnerException) types.Add(cause.GetType().Name);
            Metadata["failureChain"] = types;
            Metadata["nativeSqliteMappings"] = NativeMappings();
            if (error is SQLiteException sqlite) Metadata["sqliteFailureCode"] = sqlite.ResultCode.ToString();
            Checks["probe_completed"] = false;
        }
        finally
        {
            try
            {
                if (NativeOpened) SQLiteConnection.ClearAllPools();
            }
            catch (Exception error)
            {
                Metadata["cleanupFailureType"] = error.GetType().Name;
                Checks["owned_pool_cleanup_completed"] = false;
            }
            Checkpoint("finished");
        }

        return Checks.Count > 0 && Checks.Values.All(value => value) ? 0 : 1;
    }

    private static async Task MeasureLock(string original, int busyMilliseconds, int releaseAfterMilliseconds, int commandSeconds, string scenario)
    {
        var text = new SQLiteConnectionStringBuilder(original) { BusyTimeout = busyMilliseconds, Pooling = false }.ConnectionString;
        using var blocker = Open(text);
        using var contender = Open(text);
        Check($"busy_timeout_{busyMilliseconds}_is_applied", Convert.ToInt32(Scalar(contender, "PRAGMA busy_timeout")) == busyMilliseconds);
        var committedValue = Convert.ToInt64(Scalar(blocker, "SELECT Value FROM Marker WHERE Id=1"));
        Execute(blocker, "BEGIN IMMEDIATE; UPDATE Marker SET Value = Value + 1 WHERE Id=1;");
        using var entered = new ManualResetEventSlim();
        var operation = Task.Run(() =>
        {
            using var command = contender.CreateCommand();
            command.CommandText = "UPDATE Marker SET Value = Value + 1 WHERE Id=1";
            command.CommandTimeout = commandSeconds;
            entered.Set();
            var started = Stopwatch.StartNew();
            try
            {
                command.ExecuteNonQuery();
                return (Succeeded: true, Code: 0, Milliseconds: started.Elapsed.TotalMilliseconds);
            }
            catch (SQLiteException error)
            {
                return (Succeeded: false, Code: (int)error.ResultCode & 0xff, Milliseconds: started.Elapsed.TotalMilliseconds);
            }
        });
        if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned operation did not enter.");
        var reader = Stopwatch.StartNew();
        using (var concurrentReader = Open(text))
        {
            Check($"wal_reader_sees_committed_snapshot_{busyMilliseconds}_{scenario}", Convert.ToInt64(Scalar(concurrentReader, "SELECT Value FROM Marker WHERE Id=1")) == committedValue && reader.Elapsed < TimeSpan.FromSeconds(2));
        }

        await Task.Delay(releaseAfterMilliseconds);
        Execute(blocker, "COMMIT;");
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Measurements.Add(new { busyTimeoutMilliseconds = busyMilliseconds, commandTimeoutSeconds = commandSeconds, heldMilliseconds = releaseAfterMilliseconds, scenario, success = result.Succeeded, sqlitePrimaryCode = result.Code, elapsedMilliseconds = Math.Round(result.Milliseconds, 2) });
        if (scenario == "released")
        {
            Check($"released_write_lock_recovers_{busyMilliseconds}", result.Succeeded && result.Milliseconds < 4000);
        }
        else
        {
            Check($"sustained_write_lock_is_bounded_{busyMilliseconds}", !result.Succeeded && (result.Code is 5 or 6) && result.Milliseconds >= 750 && result.Milliseconds < 3500);
        }

        Execute(contender, "UPDATE Marker SET Value = Value + 1 WHERE Id=1");
        Check($"writer_recovers_after_lock_{busyMilliseconds}_{scenario}", true);
    }

    private static async Task MixedLoad(string main, string log, string cache, string fixture)
    {
        const int imports = 30;
        var file = Path.Combine(fixture, "owned-recording.m4b");
        await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes("Synthetic SQLite workload recording\0"));
        var original = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var backupRoot = Path.Combine(fixture, "backups");
        Directory.CreateDirectory(backupRoot);
        var writerDurations = new List<double>();
        var backupDurations = new List<double>();
        var readerCount = 0;
        var inconsistent = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = 0;
        var writer = Task.Run(async () =>
        {
            using var connection = Open(main);
            started.SetResult();
            try
            {
                for (var book = 1; book <= imports; book++)
                {
                    var timer = Stopwatch.StartNew();
                    using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                    Execute(connection, "INSERT INTO Books VALUES (@id,1,@title)", transaction, ("@id", book), ("@title", "Synthetic recording " + book));
                    Execute(connection, "INSERT INTO BookFiles VALUES (@id,@book,1,@path)", transaction, ("@id", book * 2 - 1), ("@book", book), ("@path", file));
                    await Task.Delay(4);
                    Execute(connection, "INSERT INTO BookFiles VALUES (@id,@book,2,@path)", transaction, ("@id", book * 2), ("@book", book), ("@path", file));
                    Execute(connection, "INSERT INTO History VALUES (@id,@id,3)", transaction, ("@id", book));
                    transaction.Commit();
                    writerDurations.Add(timer.Elapsed.TotalMilliseconds);
                    await Task.Delay(2);
                }
            }
            finally { Volatile.Write(ref finished, 1); }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reader = Task.Run(async () =>
        {
            using var connection = Open(main);
            while (Volatile.Read(ref finished) == 0)
            {
                if (!AtomicSnapshot(connection)) Interlocked.Increment(ref inconsistent);
                Interlocked.Increment(ref readerCount);
                await Task.Delay(1);
            }
        });
        var metadata = Task.Run(async () =>
        {
            using var connection = Open(main);
            for (var index = 0; index < 12; index++)
            {
                Execute(connection, "UPDATE Authors SET Name=@name WHERE Id=1", null, ("@name", "Synthetic Author " + index));
                await Task.Delay(3);
            }
        });
        var auxiliary = Task.WhenAll(AuxiliaryWrites(log, "DeliveryLog"), AuxiliaryWrites(cache, "MetadataCache"));
        var backup = Task.Run(async () =>
        {
            var database = new Database("Synthetic workload", () => Open(main));
            var service = new MakeDatabaseBackup(LogManager.GetLogger("SqliteContentionProbe"));
            for (var index = 0; index < 3; index++)
            {
                var target = Path.Combine(backupRoot, index.ToString());
                Directory.CreateDirectory(target);
                var timer = Stopwatch.StartNew();
                service.BackupDatabase(database, target);
                backupDurations.Add(timer.Elapsed.TotalMilliseconds);
                var backed = new SQLiteConnectionStringBuilder { DataSource = Path.Combine(target, "readarr.db"), Pooling = false };
                using var connection = Open(backed.ConnectionString);
                Check("backup_snapshot_is_atomic_" + index, AtomicSnapshot(connection));
                Check("backup_integrity_" + index, Scalar(connection, "PRAGMA integrity_check").ToString() == "ok");
                Check("backup_journal_is_not_wal_" + index, !Scalar(connection, "PRAGMA journal_mode").ToString().Equals("wal", StringComparison.OrdinalIgnoreCase));
                await Task.Delay(8);
            }
        });
        await Task.WhenAll(writer, reader, metadata, auxiliary, backup).WaitAsync(TimeSpan.FromSeconds(30));
        using (var connection = Open(main))
        {
            Check("mixed_load_preserves_every_recording_and_part", Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM Books")) == imports && Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM BookFiles")) == imports * 2 && Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM History")) == imports);
            Check("mixed_load_reader_observed_only_complete_units", readerCount > 0 && inconsistent == 0);
            Check("mixed_load_foreign_key_shape_is_intact", Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM BookFiles f LEFT JOIN Books b ON b.Id=f.BookId WHERE b.Id IS NULL")) == 0);
            Check("pool_reopen_keeps_wal", Scalar(connection, "PRAGMA journal_mode").ToString().Equals("wal", StringComparison.OrdinalIgnoreCase));
            Check("mixed_load_preserves_exact_book_values", Strings(connection, "SELECT Id || ':' || AuthorId || ':' || Title FROM Books ORDER BY Id").SequenceEqual(Enumerable.Range(1, imports).Select(id => id + ":1:Synthetic recording " + id)));
            Check("mixed_load_preserves_exact_history_values", Strings(connection, "SELECT Id || ':' || BookId || ':' || EventType FROM History ORDER BY Id").SequenceEqual(Enumerable.Range(1, imports).Select(id => id + ":" + id + ":3")));
            Check("mixed_load_preserves_exact_file_values", Strings(connection, "SELECT Id || ':' || BookId || ':' || Part || ':' || Path FROM BookFiles ORDER BY Id").SequenceEqual(Enumerable.Range(1, imports * 2).Select(id => id + ":" + ((id + 1) / 2) + ":" + ((id - 1) % 2 + 1) + ":" + file)));
        }

        var afterBytes = await File.ReadAllBytesAsync(file);
        Check("synthetic_media_bytes_unchanged", original.SequenceEqual(SHA256.HashData(afterBytes)));
        Metadata["mixedLoad"] = new { imports, parts = imports * 2, readerSnapshots = readerCount, maxImportMilliseconds = Math.Round(writerDurations.Max(), 2), backupMilliseconds = backupDurations.Select(value => Math.Round(value, 2)).ToArray() };
    }

    private static async Task AuxiliaryWrites(string text, string table)
    {
        using var connection = Open(text);
        Execute(connection, $"CREATE TABLE {table} (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
        for (var index = 0; index < 20; index++)
        {
            Execute(connection, $"INSERT INTO {table} VALUES (@id,'owned')", null, ("@id", index));
            await Task.Delay(2);
        }
        Check(table.ToLowerInvariant() + "_writes_complete", Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table}")) == 20);
    }

    private static async Task CancelAndRollback(string main)
    {
        using var connection = Open(main);
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        Execute(connection, "INSERT INTO Books VALUES (9000,1,'Unfinished import')", transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "WITH RECURSIVE seq(v) AS (VALUES(1) UNION ALL SELECT v+1 FROM seq WHERE v<100000000) SELECT SUM(v) FROM seq";
        using var entered = new ManualResetEventSlim();
        Checkpoint("cancellation_query_starting");
        var running = Task.Run(() =>
        {
            entered.Set();
            try { command.ExecuteScalar(); return false; }
            catch (SQLiteException error) { return ((int)error.ResultCode & 0xff) == 9; }
        });
        entered.Wait();
        var completedTooSoon = running.Wait(TimeSpan.FromMilliseconds(150));
        var timer = Stopwatch.StartNew();
        Exception cancellationError = null;
        try { connection.Cancel(); }
        catch (Exception error) { cancellationError = error; }
        // SQLiteCommand.Cancel is not an interrupt API. The connection API is
        // documented safe from another thread. Always join the executing worker
        // before disposing a command/transaction; the owned process deadline
        // bounds a provider that fails to interrupt without unsafe cleanup.
        var interrupted = await running;
        var elapsed = timer.Elapsed;
        Checkpoint("cancellation_worker_completed");
        transaction.Rollback();
        Check("executing_query_cancellation_is_bounded", cancellationError == null && !completedTooSoon && interrupted && elapsed < TimeSpan.FromSeconds(5));
        using var after = Open(main);
        Check("cancelled_import_has_no_partial_book", Convert.ToInt32(Scalar(after, "SELECT COUNT(*) FROM Books WHERE Id=9000")) == 0);
        Execute(after, "UPDATE Marker SET Value=Value+1 WHERE Id=1");
        Check("later_writer_succeeds_after_cancellation", true);
        Metadata["cancellation"] = new { api = "SQLiteConnection.Cancel", sqlitePrimaryCode = 9, elapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 2), workerJoinedBeforeDisposal = true };
    }

    private static bool AtomicSnapshot(SQLiteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM Books), (SELECT COUNT(*) FROM BookFiles), (SELECT COUNT(*) FROM History)";
        using var reader = command.ExecuteReader();
        reader.Read();
        return reader.GetInt64(1) == reader.GetInt64(0) * 2 && reader.GetInt64(2) == reader.GetInt64(0);
    }

    private static SQLiteConnection Open(string text)
    {
        var connection = new SQLiteConnection(text);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static object Scalar(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static string[] Strings(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static void Execute(SQLiteConnection connection, string sql, SQLiteTransaction transaction = null, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var parameter in parameters)
        {
            var value = command.CreateParameter();
            value.ParameterName = parameter.Name;
            value.Value = parameter.Value;
            command.Parameters.Add(value);
        }
        command.ExecuteNonQuery();
    }

    private static string[] NativeMappings()
    {
        if (!File.Exists("/proc/self/maps")) return Array.Empty<string>();
        return File.ReadLines("/proc/self/maps").Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 6).Select(fields => fields[5].TrimStart())
            .Where(path => path.StartsWith('/') && (Path.GetFileName(path).StartsWith("libsqlite3") || Path.GetFileName(path).StartsWith("libe_sqlite3")))
            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static void Check(string name, bool value)
    {
        lock (Checks) Checks[name] = value;
        if (!value) throw new InvalidOperationException(name);
    }

    private static void Checkpoint(string phase)
    {
        Metadata["phase"] = phase;
        Metadata["contentionMeasurements"] = Measurements;
        var report = Path.Combine(Fixture, "sqlite-probe-result.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { checks = Checks, metadata = Metadata }, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(report, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed class Folders(string root) : IAppFolderInfo
    {
        public string AppDataFolder => root;
        public string TempFolder => Path.Combine(root, "tmp");
        public string StartUpFolder => AppContext.BaseDirectory;
    }

}

public class ConfigProxy : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "get_PostgresHost") return string.Empty;
        throw new NotSupportedException("Only synthetic SQLite settings are allowed in this probe.");
    }
}
