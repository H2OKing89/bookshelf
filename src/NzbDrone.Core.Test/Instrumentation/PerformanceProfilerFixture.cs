using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;
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
            using var profiler = Create(false);
            profiler.Begin("Library.Get", "GET").Should().BeNull();
            Query("Owned synthetic unprofiled marker");
            profiler.Snapshot().Enabled.Should().BeFalse();
            profiler.Snapshot().Samples.Should().Be(0);
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
            await Task.WhenAll(Task.Run(() =>
            {
                using (profiler.Begin("One.Get", "GET"))
                {
                    Query("First request");
                }
            }), Task.Run(() =>
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
            clock.Now = clock.Now.AddMinutes(PerformanceProfiler.RetentionMinutes);
            profiler.Snapshot().Samples.Should().Be(0);
            using (profiler.Begin("New.Get", "GET"))
            {
            }

            profiler.Snapshot().Samples.Should().Be(1);
            profiler.Dispose();
            profiler.Snapshot().Enabled.Should().BeFalse();
            profiler.Snapshot().Samples.Should().Be(0);
        }

        private sealed class OwnedClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
