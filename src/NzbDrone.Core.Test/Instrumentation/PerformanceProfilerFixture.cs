using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Instrumentation
{
    [TestFixture]
    public class PerformanceProfilerFixture : DbTest
    {
        private PerformanceProfiler Create(bool enabled, TimeProvider time = null)
        {
            var config = new Mock<IConfigFileProvider>();
            config.Setup(provider => provider.PerformanceProfilingEnabled).Returns(enabled);
            return new PerformanceProfiler(config.Object, time ?? TimeProvider.System);
        }

        private void Query(string marker)
        {
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.ExecuteScalar<int>("SELECT LENGTH(@marker)", new { marker }).Should().Be(marker.Length);
        }

        [Test]
        public void disabled_profiler_does_not_sample()
        {
            var clock = new OwnedClock();
            using var profiler = Create(false, clock);
            profiler.Begin("Library.Get", "GET").Should().BeNull();
            profiler.BeginJob(new RefreshAuthorCommand(1), CommandTrigger.Scheduled).Should().BeNull();
            Query("Owned synthetic unprofiled marker");
            clock.TimestampReads.Should().Be(0);
            clock.TimerCreations.Should().Be(0);
            profiler.Snapshot().Enabled.Should().BeFalse();
            profiler.Snapshot().Samples.Should().Be(0);
            profiler.Snapshot().JobSamples.Should().Be(0);
        }

        [Test]
        public void genuine_database_command_timings_are_scoped_without_retaining_query_or_parameters()
        {
            using var profiler = Create(true);
            Query("Not in a request scope");
            var databaseType = Mocker.Resolve<IMainDatabase>().DatabaseType;
            using (profiler.Begin("Library.Get", "GET"))
            {
                Query("Owned synthetic secret marker");
                Query("Second owned marker");
            }

            Query("After the scope");
            var result = profiler.Snapshot();
            result.Samples.Should().Be(1);
            var timing = result.Operations.Single();
            timing.Operation.Should().Be("Library.Get");
            timing.Requests.Should().Be(1);
            timing.MeanMilliseconds.Should().BeGreaterOrEqualTo(0);
            if (databaseType == DatabaseType.SQLite)
            {
                timing.SqliteStatements.Should().BeGreaterOrEqualTo(2);
                timing.SqliteMilliseconds.Should().BeGreaterOrEqualTo(0);
                timing.PostgresOperations.Should().Be(0);
            }
            else
            {
                timing.PostgresOperations.Should().BeGreaterOrEqualTo(2);
                timing.PostgresMilliseconds.Should().BeGreaterOrEqualTo(0);
                timing.SqliteStatements.Should().Be(0);
            }

            var serialized = JsonSerializer.Serialize(result);
            serialized.Should().NotContain("Owned synthetic secret marker").And.NotContain("Second owned marker").And.NotContain("SELECT").And.NotContain("@marker");
        }

        [Test]
        public async Task concurrent_request_scopes_remain_independent_and_completed_scopes_ignore_late_work()
        {
            using var profiler = Create(true);
            var databaseType = Mocker.Resolve<IMainDatabase>().DatabaseType;
            await Task.WhenAll(
                Task.Run(() =>
                {
                    using (profiler.Begin("One.Get", "GET"))
                    {
                        Query("First request");
                    }
                }),
                Task.Run(() =>
                {
                    using var capture = profiler.Begin("Two.Get", "GET");
                    Query("Second request");
                    capture.Complete(cancelled: true);
                    var completed = profiler.Snapshot().Operations.Single(group => group.Operation == "Two.Get");
                    Query("Late work must not update the completed request");
                    var after = profiler.Snapshot().Operations.Single(group => group.Operation == "Two.Get");
                    after.SqliteStatements.Should().Be(completed.SqliteStatements);
                    after.PostgresOperations.Should().Be(completed.PostgresOperations);
                }));

            var groups = profiler.Snapshot().Operations.ToDictionary(g => g.Operation);
            groups.Keys.Should().BeEquivalentTo("One.Get", "Two.Get");
            groups["One.Get"].Requests.Should().Be(1);
            groups["Two.Get"].Requests.Should().Be(1);
            groups["Two.Get"].Cancelled.Should().Be(1);
            var firstCount = databaseType == DatabaseType.SQLite ? groups["One.Get"].SqliteStatements : groups["One.Get"].PostgresOperations;
            var secondCount = databaseType == DatabaseType.SQLite ? groups["Two.Get"].SqliteStatements : groups["Two.Get"].PostgresOperations;
            firstCount.Should().BeGreaterThan(0);
            secondCount.Should().BeGreaterThan(0);
        }

        [Test]
        public async Task independent_job_and_request_scopes_use_provider_timings_without_nested_job_or_argument_samples()
        {
            using var profiler = Create(true);
            var databaseType = Mocker.Resolve<IMainDatabase>().DatabaseType;
            await Task.WhenAll(
                Task.Run(() =>
                {
                    using var capture = profiler.BeginJob(new RefreshAuthorCommand(891234), CommandTrigger.Scheduled);
                    profiler.BeginJob(new RescanFoldersCommand(), CommandTrigger.Unspecified).Should().BeNull();
                    Query("Owned secret job marker");
                    Query("Owned second query inside job");
                    capture.Complete(cancelled: true);
                    Query("Late work is not added to the completed job");
                }),
                Task.Run(() =>
                {
                    using var capture = profiler.Begin("Library.Get", "GET");
                    profiler.BeginJob(new ManualImportCommand(), CommandTrigger.Manual).Should().BeNull();
                    Query("Owned request marker");
                }));

            var snapshot = profiler.Snapshot();
            snapshot.Samples.Should().Be(1);
            snapshot.JobSamples.Should().Be(1);
            var job = snapshot.Jobs.Single();
            job.Operation.Should().Be("RefreshAuthor");
            job.Category.Should().Be(PerformanceJobCategory.AuthorRefresh);
            job.Trigger.Should().Be(CommandTrigger.Scheduled);
            job.Cancelled.Should().Be(1);
            job.Failed.Should().Be(0);
            job.Executions.Should().Be(1);
            job.MeanMilliseconds.Should().BeGreaterOrEqualTo(0);
            if (databaseType == DatabaseType.SQLite)
            {
                job.SqliteStatements.Should().BeGreaterOrEqualTo(2);
                job.PostgresOperations.Should().Be(0);
            }
            else
            {
                job.PostgresOperations.Should().BeGreaterOrEqualTo(2);
                job.SqliteStatements.Should().Be(0);
            }

            var serialized = JsonSerializer.Serialize(snapshot);
            serialized.Should().NotContain("891234").And.NotContain("Owned secret job marker").And.NotContain("SELECT").And.NotContain("@marker");
        }

        [Test]
        public void job_allowlist_splits_triggers_and_shares_capacity_and_expiry_with_requests()
        {
            var clock = new OwnedClock();
            using var profiler = Create(true, clock);
            profiler.BeginJob(new UnprofiledCommand(), CommandTrigger.Scheduled).Should().BeNull();
            using (profiler.Begin("Evicted.Get", "GET"))
            {
            }

            for (var index = 0; index < PerformanceProfiler.Capacity; index++)
            {
                using var job = profiler.BeginJob(new RefreshMediaInfoCommand { BookId = 891234 }, index == 0 ? CommandTrigger.Manual : CommandTrigger.Scheduled);
                job.Complete(failed: index == 0);
            }

            var snapshot = profiler.Snapshot();
            snapshot.Samples.Should().Be(0);
            snapshot.JobSamples.Should().Be(PerformanceProfiler.Capacity);
            snapshot.Jobs.Should().HaveCount(2);
            snapshot.Jobs.Single(job => job.Trigger == CommandTrigger.Manual).Failed.Should().Be(1);
            snapshot.Jobs.Single(job => job.Trigger == CommandTrigger.Scheduled).Executions.Should().Be(PerformanceProfiler.Capacity - 1);
            clock.Advance(TimeSpan.FromMinutes(PerformanceProfiler.RetentionMinutes));
            profiler.Snapshot().JobSamples.Should().Be(0);
        }

        [Test]
        public void bounded_samples_expire_and_dispose_clears_them()
        {
            var clock = new OwnedClock();
            using var profiler = Create(true, clock);
            using (profiler.Begin("Evicted.Get", "GET"))
            {
            }

            for (var index = 0; index < PerformanceProfiler.Capacity + 10; index++)
            {
                using var request = profiler.Begin("Retained.Get", "GET");
                request.Complete(failed: index == PerformanceProfiler.Capacity + 9);
            }

            var snapshot = profiler.Snapshot();
            snapshot.Samples.Should().Be(PerformanceProfiler.Capacity);
            snapshot.Operations.Should().ContainSingle().Which.Operation.Should().Be("Retained.Get");
            snapshot.Operations.Single().Failed.Should().Be(1);
            clock.Advance(TimeSpan.FromMinutes(PerformanceProfiler.RetentionMinutes));
            profiler.Snapshot().Samples.Should().Be(0);
            using (profiler.Begin("New.Get", "GET"))
            {
            }

            profiler.Snapshot().Samples.Should().Be(1);
            profiler.Dispose();
            profiler.Snapshot().Enabled.Should().BeFalse();
            profiler.Snapshot().Samples.Should().Be(0);
        }

        [Test]
        public void retention_uses_order_of_completion_and_monotonic_time_when_wall_clock_jumps()
        {
            var clock = new OwnedClock();
            using var profiler = Create(true, clock);
            using var longerRequest = profiler.Begin("Longer.Get", "GET");
            clock.Advance(TimeSpan.FromMinutes(2));
            using (profiler.Begin("Shorter.Get", "GET"))
            {
            }

            clock.Advance(TimeSpan.FromMinutes(1));
            clock.Now = clock.Now.AddDays(-1);
            longerRequest.Complete();
            profiler.Snapshot().Samples.Should().Be(2);

            // The short request finishes first even though it starts second.
            // A wall clock correction must not leave its sample behind later
            // completions or prematurely expire the longer request.
            clock.Advance(TimeSpan.FromMinutes(9));
            profiler.Snapshot().Operations.Should().ContainSingle().Which.Operation.Should().Be("Longer.Get");
            clock.Now = clock.Now.AddDays(2);
            clock.Advance(TimeSpan.FromMinutes(1));
            profiler.Snapshot().Samples.Should().Be(0);
        }

        private sealed class UnprofiledCommand : Command
        {
        }

        private sealed class OwnedClock : TimeProvider
        {
            private long _timestamp;

            public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public int TimestampReads { get; private set; }
            public int TimerCreations { get; private set; }

            public override DateTimeOffset GetUtcNow() => Now;

            public override long GetTimestamp()
            {
                TimestampReads++;
                return _timestamp;
            }

            public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            {
                TimerCreations++;
                return base.CreateTimer(callback, state, dueTime, period);
            }

            public void Advance(TimeSpan duration)
            {
                Now += duration;
                _timestamp += duration.Ticks;
            }
        }
    }
}
