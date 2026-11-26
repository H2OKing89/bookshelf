using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http.HappyEyeballs;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HappyEyeballsFixture
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
        private static readonly IPAddress V4First = IPAddress.Parse("192.0.2.1");
        private static readonly IPAddress V4Second = IPAddress.Parse("192.0.2.2");
        private static readonly IPAddress V6First = IPAddress.Parse("2001:db8::1");
        private static readonly IPAddress V6Second = IPAddress.Parse("2001:db8::2");
        private static readonly IPAddress V6Third = IPAddress.Parse("2001:db8::3");

        [Test]
        public async Task no_addresses_should_be_rejected()
        {
            var connector = Connector((address, token) => throw new InvalidOperationException());

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await connector.Connect(Array.Empty<IPAddress>(), CancellationToken.None));
        }

        [Test]
        public async Task precancellation_should_not_start_a_connection_or_timer()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var connector = Connector((address, token) => throw new InvalidOperationException());

            var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await connector.Connect(new[] { V4First }, cancellation.Token));

            error.CancellationToken.Should().Be(cancellation.Token);
        }

        [TestCase("v4")]
        [TestCase("v6")]
        [TestCase("mixed")]
        [TestCase("uneven")]
        public async Task failures_should_try_every_address_in_stable_family_order(string family)
        {
            var input = family switch
            {
                "v4" => new[] { V4First, V4Second },
                "v6" => new[] { V6First, V6Second },
                "mixed" => new[] { V4First, V4Second, V6First, V6Second },
                _ => new[] { V6First, V6Second, V6Third, V4First }
            };
            var expected = family switch
            {
                "v4" => new[] { V4First, V4Second },
                "v6" => new[] { V6First, V6Second },
                "mixed" => new[] { V6First, V4First, V6Second, V4Second },
                _ => new[] { V6First, V4First, V6Second, V6Third }
            };
            var attempted = new List<IPAddress>();
            var connector = Connector((address, token) =>
            {
                attempted.Add(address);
                return Task.FromException<Connection>(new InvalidOperationException(address.ToString()));
            });

            var error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await connector.Connect(input, CancellationToken.None));

            attempted.Should().Equal(expected);
            error.InnerExceptions.Should().HaveCount(input.Length);
        }

        [Test]
        public async Task immediate_success_should_transfer_only_one_connection_without_a_timer()
        {
            var winner = new Connection();
            var calls = 0;
            var connector = Connector((address, token) =>
            {
                calls++;
                return Task.FromResult(winner);
            });

            var result = await connector.Connect(new[] { V4First, V6First }, CancellationToken.None);

            result.Should().BeSameAs(winner);
            winner.DisposeCount.Should().Be(0);
            calls.Should().Be(1);
            result.Dispose();
        }

        [Test]
        public async Task a_synchronous_connection_failure_should_allow_the_other_family_immediately()
        {
            var winner = new Connection();
            var attempts = new List<IPAddress>();
            var connector = Connector((address, token) =>
            {
                attempts.Add(address);
                return address.Equals(V6First) ? throw new InvalidOperationException("IPv6 unavailable") : Task.FromResult(winner);
            });

            var result = await connector.Connect(new[] { V4First, V6First }, CancellationToken.None);

            attempts.Should().Equal(V6First, V4First);
            result.Should().BeSameAs(winner);
            result.Dispose();
        }

        [Test]
        public async Task a_stalled_first_family_should_start_the_other_after_the_stagger_and_dispose_late_success()
        {
            var loser = new Connection();
            var winner = new Connection();
            var delayEntered = Signal();
            var releaseDelay = Signal();
            var attempts = new List<IPAddress>();
            var connector = new HappyEyeballs<Connection>(
                (address, token) =>
                {
                    attempts.Add(address);
                    return address.Equals(V6First) ? AfterCancellation(token, loser) : Task.FromResult(winner);
                },
                token =>
                {
                    delayEntered.TrySetResult(true);
                    return releaseDelay.Task.WaitAsync(token);
                });

            var pending = connector.Connect(new[] { V4First, V6First }, CancellationToken.None).AsTask();
            await delayEntered.Task.WaitAsync(Deadline);
            attempts.Should().Equal(V6First);

            releaseDelay.TrySetResult(true);
            var result = await pending.WaitAsync(Deadline);

            attempts.Should().Equal(V6First, V4First);
            result.Should().BeSameAs(winner);
            loser.DisposeCount.Should().Be(1);
            winner.DisposeCount.Should().Be(0);
            result.Dispose();
        }

        [Test]
        public async Task simultaneous_successes_should_transfer_one_and_dispose_the_other()
        {
            var first = new Connection();
            var second = new Connection();
            var firstPending = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connector = new HappyEyeballs<Connection>(
                (address, token) =>
                {
                    if (address.Equals(V6First))
                    {
                        return firstPending.Task;
                    }

                    firstPending.TrySetResult(first);
                    return Task.FromResult(second);
                },
                token => Task.CompletedTask);

            var result = await connector.Connect(new[] { V6First, V4First }, CancellationToken.None).AsTask().WaitAsync(Deadline);

            new[] { first, second }.Should().Contain(result);
            (first.DisposeCount + second.DisposeCount).Should().Be(1);
            result.DisposeCount.Should().Be(0);
            result.Dispose();
        }

        [Test]
        public async Task cancellation_should_dispose_a_connection_that_succeeds_after_it()
        {
            using var cancellation = new CancellationTokenSource();
            var entered = Signal();
            var connection = new Connection();
            var connector = Connector((address, token) =>
            {
                entered.TrySetResult(true);
                return AfterCancellation(token, connection);
            });
            var pending = connector.Connect(new[] { V6First }, cancellation.Token).AsTask();
            await entered.Task.WaitAsync(Deadline);

            cancellation.Cancel();

            var error = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            error.CancellationToken.Should().Be(cancellation.Token);
            connection.DisposeCount.Should().Be(1);
        }

        [Test]
        public async Task cancellation_during_loser_cleanup_should_dispose_the_selected_winner_too()
        {
            using var cancellation = new CancellationTokenSource();
            var cleanupEntered = Signal();
            var releaseCleanup = Signal();
            var loser = new Connection();
            var winner = new Connection();
            var connector = new HappyEyeballs<Connection>((address, token) =>
                address.Equals(V6First) ? LateConnection(token, loser, cleanupEntered, releaseCleanup) : Task.FromResult(winner),
                token => Task.CompletedTask);
            var pending = connector.Connect(new[] { V6First, V4First }, cancellation.Token).AsTask();
            await cleanupEntered.Task.WaitAsync(Deadline);

            cancellation.Cancel();
            releaseCleanup.TrySetResult(true);

            var error = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            error.CancellationToken.Should().Be(cancellation.Token);
            loser.DisposeCount.Should().Be(1);
            winner.DisposeCount.Should().Be(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task a_failed_stagger_should_cancel_and_dispose_the_outstanding_attempt(bool synchronous)
        {
            var connection = new Connection();
            var connector = new HappyEyeballs<Connection>((address, token) => AfterCancellation(token, connection),
                token => synchronous ? throw new InvalidOperationException("stagger failed") : Task.FromException(new InvalidOperationException("stagger failed")));

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connector.Connect(new[] { V6First, V4First }, CancellationToken.None).AsTask().WaitAsync(Deadline));

            connection.DisposeCount.Should().Be(1);
        }

        [TestCase("throw")]
        [TestCase("fault")]
        [TestCase("cancel")]
        public async Task caller_cancellation_should_win_a_simultaneous_attempt_failure(string outcome)
        {
            using var cancellation = new CancellationTokenSource();
            var connector = Connector((address, token) =>
            {
                cancellation.Cancel();
                return outcome switch
                {
                    "throw" => throw new InvalidOperationException("connection failed during cancellation"),
                    "fault" => Task.FromException<Connection>(new InvalidOperationException("connection failed during cancellation")),
                    _ => Task.FromCanceled<Connection>(token)
                };
            });

            var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await connector.Connect(new[] { V6First }, cancellation.Token));

            error.CancellationToken.Should().Be(cancellation.Token);
        }

        [Test]
        public async Task independent_attempt_cancellation_should_remain_an_aggregate_failure()
        {
            var connector = Connector((address, token) => Task.FromCanceled<Connection>(new CancellationToken(true)));

            var error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await connector.Connect(new[] { V6First }, CancellationToken.None));

            error.InnerExceptions.Should().ContainSingle().Which.Should().BeOfType<TaskCanceledException>();
        }

        [Test]
        public async Task a_failed_family_should_not_disable_it_for_the_next_connection()
        {
            var attempts = new List<IPAddress>();
            var firstConnection = true;
            var connector = Connector((address, token) =>
            {
                attempts.Add(address);
                return firstConnection && address.Equals(V6First)
                    ? Task.FromException<Connection>(new InvalidOperationException("first host IPv6 failed"))
                    : Task.FromResult(new Connection());
            });
            using var first = await connector.Connect(new[] { V6First, V4First }, CancellationToken.None);
            firstConnection = false;

            using var second = await connector.Connect(new[] { V6First, V4First }, CancellationToken.None);

            attempts.Should().Equal(V6First, V4First, V6First);
        }

        private static HappyEyeballs<Connection> Connector(Func<IPAddress, CancellationToken, Task<Connection>> connect)
        {
            return new HappyEyeballs<Connection>(connect, token => throw new InvalidOperationException("A completed attempt must not wait for a stagger."));
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task<Connection> AfterCancellation(CancellationToken token, Connection connection)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }

            return connection;
        }

        private static async Task<Connection> LateConnection(CancellationToken token,
                                                             Connection connection,
                                                             TaskCompletionSource<bool> entered,
                                                             TaskCompletionSource<bool> release)
        {
            await AfterCancellation(token, connection);
            entered.TrySetResult(true);
            await release.Task;
            return connection;
        }

        private sealed class Connection : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
