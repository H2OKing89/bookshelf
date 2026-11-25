using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Instrumentation
{
    public sealed class PerformanceProfileGroup
    {
        public string Operation { get; init; }
        public string Method { get; init; }
        public int Requests { get; init; }
        public int Failed { get; init; }
        public int Cancelled { get; init; }
        public double MeanMilliseconds { get; init; }
        public double MaxMilliseconds { get; init; }
        public long SqliteStatements { get; init; }
        public double SqliteMilliseconds { get; init; }
        public long PostgresOperations { get; init; }
        public double PostgresMilliseconds { get; init; }
    }

    public sealed class PerformanceProfileSnapshot
    {
        public bool Enabled { get; init; }
        public int RetentionMinutes { get; init; }
        public int Capacity { get; init; }
        public int Samples { get; init; }
        public List<PerformanceProfileGroup> Operations { get; init; }
    }

    // Short-lived request aggregates, not a SQL/logging facility. Provider
    // objects, request IDs, URLs, statements, parameters and user data are
    // deliberately not retained. Only provider timing aggregates are sampled. PostgreSQL
    // includes command and physical connection-open activities.
    public sealed class PerformanceProfiler : IDisposable
    {
        public const string AuthorizationPolicy = "PerformanceDiagnostics";
        public const int Capacity = 256;
        public const int RetentionMinutes = 10;
        private const string ActivityCaptureKey = "Bookshelf.PerformanceCapture";
        private static readonly AsyncLocal<RequestCapture> Current = new ();
        private readonly Queue<RequestSample> _samples = new ();
        private readonly object _gate = new ();
        private readonly TimeProvider _time;
        private readonly ActivityListener _listener;
        private readonly ITimer _expiryTimer;
        private volatile bool _disposed;

        // The host configuration is read once per host lifecycle. Turning this
        // off on restart removes the listener and all previously captured data.
        public PerformanceProfiler(IConfigFileProvider config, TimeProvider time)
        {
            Enabled = config.PerformanceProfilingEnabled;
            _time = time;
            if (!Enabled)
            {
                return;
            }

            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => Sample(),
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => Sample(),
                ActivityStarted = activity =>
                {
                    var capture = Current.Value;
                    if (capture?.Owner == this)
                    {
                        activity.SetCustomProperty(ActivityCaptureKey, capture);
                    }
                },
                ActivityStopped = activity =>
                {
                    if (activity.GetCustomProperty(ActivityCaptureKey) is RequestCapture capture && capture.Owner == this)
                    {
                        capture.RecordPostgres(activity.Duration.TotalMilliseconds);
                        activity.SetCustomProperty(ActivityCaptureKey, null);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
            _expiryTimer = time.CreateTimer(_ => Expire(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public bool Enabled { get; }

        // Called with only application-owned controller/action metadata.
        public RequestCapture Begin(string operation, string method)
        {
            if (!Enabled || _disposed || string.IsNullOrEmpty(operation) || operation.Length > 128 ||
                !operation.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_') ||
                !IsSupportedMethod(method))
            {
                return null;
            }

            var capture = new RequestCapture(this, Current.Value, operation, method, _time.GetTimestamp());
            Current.Value = capture;
            return capture;
        }

        private static bool IsSupportedMethod(string method)
        {
            return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "OTHER";
        }

        // Attach to the actual SQLite command-profile event only in the current
        // recording scope. Leave the genuine concrete connection intact for
        // transactions, backups, provider-specific code and connection pooling.
        public static IDbConnection ObserveConnection(IDbConnection connection)
        {
            var capture = Current.Value;
            if (capture != null && connection is SQLiteConnection sqlite)
            {
                // A diagnostic hook must not compromise ordinary DB access if
                // the native provider on some architecture lacks this feature.
                try
                {
                    sqlite.TraceFlags = SQLiteTraceFlags.SQLITE_TRACE_PROFILE;
                    sqlite.Trace2 += (_, args) =>
                    {
                        if (args.Flags == SQLiteTraceFlags.SQLITE_TRACE_PROFILE && args.Elapsed is >= 0)
                        {
                            // SQLite's PROFILE trace gives nanoseconds. Never
                            // read args.Statement or any expanded SQL value.
                            capture.RecordSqlite(args.Elapsed.Value / 1_000_000.0);
                        }
                    };
                }
                catch (Exception)
                {
                    // Request timing remains useful even without SQL telemetry.
                }
            }

            return connection;
        }

        public PerformanceProfileSnapshot Snapshot()
        {
            lock (_gate)
            {
                ExpireLocked();
                var groups = _samples.GroupBy(sample => (sample.Operation, sample.Method))
                    .Select(group => new PerformanceProfileGroup
                    {
                        Operation = group.Key.Operation,
                        Method = group.Key.Method,
                        Requests = group.Count(),
                        Failed = group.Count(sample => sample.Failed),
                        Cancelled = group.Count(sample => sample.Cancelled),
                        MeanMilliseconds = group.Average(sample => sample.DurationMilliseconds),
                        MaxMilliseconds = group.Max(sample => sample.DurationMilliseconds),
                        SqliteStatements = group.Sum(sample => sample.SqliteStatements),
                        SqliteMilliseconds = group.Sum(sample => sample.SqliteMilliseconds),
                        PostgresOperations = group.Sum(sample => sample.PostgresOperations),
                        PostgresMilliseconds = group.Sum(sample => sample.PostgresMilliseconds)
                    })
                    .OrderByDescending(group => group.MaxMilliseconds)
                    .ThenBy(group => group.Operation, StringComparer.Ordinal)
                    .ThenBy(group => group.Method, StringComparer.Ordinal)
                    .ToList();
                return new PerformanceProfileSnapshot
                {
                    Enabled = Enabled && !_disposed,
                    RetentionMinutes = RetentionMinutes,
                    Capacity = Capacity,
                    Samples = _samples.Count,
                    Operations = groups
                };
            }
        }

        private ActivitySamplingResult Sample() => Current.Value?.Owner == this
            ? ActivitySamplingResult.PropagationData
            : ActivitySamplingResult.None;

        private void Expire()
        {
            lock (_gate)
            {
                ExpireLocked();
            }
        }

        private void ExpireLocked()
        {
            ExpireLocked(_time.GetTimestamp());
        }

        private void ExpireLocked(long now)
        {
            while (_samples.TryPeek(out var item) && _time.GetElapsedTime(item.RecordedTimestamp, now) >= TimeSpan.FromMinutes(RetentionMinutes))
            {
                _samples.Dequeue();
            }
        }

        private void Record(RequestSample sample)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                // Timestamp while holding the queue's lock: concurrent
                // completions cannot enqueue an older timestamp behind a
                // newer one, and wall-clock adjustments do not extend TTL.
                var now = _time.GetTimestamp();
                ExpireLocked(now);
                if (_samples.Count == Capacity)
                {
                    _samples.Dequeue();
                }

                sample.RecordedTimestamp = now;
                _samples.Enqueue(sample);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _samples.Clear();
            }

            _expiryTimer?.Dispose();
            _listener?.Dispose();
        }

        private sealed class RequestSample
        {
            public long RecordedTimestamp { get; set; }
            public string Operation { get; init; }
            public string Method { get; init; }
            public double DurationMilliseconds { get; init; }
            public long SqliteStatements { get; init; }
            public double SqliteMilliseconds { get; init; }
            public long PostgresOperations { get; init; }
            public double PostgresMilliseconds { get; init; }
            public bool Failed { get; init; }
            public bool Cancelled { get; init; }
        }

        public sealed class RequestCapture : IDisposable
        {
            private readonly object _gate = new ();
            private readonly RequestCapture _parent;
            private readonly string _operation;
            private readonly string _method;
            private readonly long _started;
            private long _sqliteStatements;
            private long _postgresOperations;
            private double _sqliteMilliseconds;
            private double _postgresMilliseconds;
            private bool _complete;

            internal RequestCapture(PerformanceProfiler owner, RequestCapture parent, string operation, string method, long started)
            {
                Owner = owner;
                _parent = parent;
                _operation = operation;
                _method = method;
                _started = started;
            }

            internal PerformanceProfiler Owner { get; }

            internal void RecordSqlite(double milliseconds)
            {
                lock (_gate)
                {
                    if (!_complete && milliseconds >= 0 && double.IsFinite(milliseconds))
                    {
                        _sqliteStatements++;
                        _sqliteMilliseconds += milliseconds;
                    }
                }
            }

            internal void RecordPostgres(double milliseconds)
            {
                lock (_gate)
                {
                    if (!_complete && milliseconds >= 0 && double.IsFinite(milliseconds))
                    {
                        _postgresOperations++;
                        _postgresMilliseconds += milliseconds;
                    }
                }
            }

            public void Complete(bool failed = false, bool cancelled = false)
            {
                RequestSample sample;
                lock (_gate)
                {
                    if (_complete)
                    {
                        return;
                    }

                    _complete = true;
                    sample = new RequestSample
                    {
                        Operation = _operation,
                        Method = _method,
                        DurationMilliseconds = Owner._time.GetElapsedTime(_started).TotalMilliseconds,
                        SqliteStatements = _sqliteStatements,
                        SqliteMilliseconds = _sqliteMilliseconds,
                        PostgresOperations = _postgresOperations,
                        PostgresMilliseconds = _postgresMilliseconds,
                        Failed = failed,
                        Cancelled = cancelled
                    };
                }

                Owner.Record(sample);
            }

            public void Dispose()
            {
                Complete();
                if (ReferenceEquals(Current.Value, this))
                {
                    Current.Value = _parent;
                }
            }
        }
    }
}
