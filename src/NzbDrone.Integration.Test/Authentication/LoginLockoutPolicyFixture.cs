using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class LoginLockoutPolicyFixture : TestBase
    {
        private const string Address = "192.0.2.1";
        private TestClock _clock;
        private LoginLockoutPolicy _policy;

        [SetUp]
        public void Setup()
        {
            _clock = new TestClock();
            _policy = new LoginLockoutPolicy(_clock);
        }

        [Test]
        public void fifth_failed_attempt_should_lock_the_source_and_skip_later_password_verification()
        {
            var verifications = 0;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                _policy.GetLockoutEndTime(Address).Should().BeNull();
                _policy.Authenticate(Address, () =>
                {
                    verifications++;
                    return null;
                }).IsBlocked.Should().BeFalse();
            }

            var blocked = _policy.Authenticate(Address, () =>
            {
                verifications++;
                return new User();
            });

            blocked.User.Should().BeNull();
            blocked.IsBlocked.Should().BeTrue();
            verifications.Should().Be(5);
            _policy.GetLockoutEndTime(Address).Should().Be(_clock.UtcNow.UtcDateTime.AddMinutes(5));
        }

        [Test]
        public void blocked_attempts_and_status_queries_should_not_extend_the_lockout()
        {
            Fail(Address, 5);
            var end = _policy.GetLockoutEndTime(Address);
            _clock.Advance(TimeSpan.FromMinutes(4));

            _policy.Authenticate(Address, () => new User()).IsBlocked.Should().BeTrue();
            _policy.GetLockoutEndTime(Address).Should().Be(end);
            _clock.Advance(TimeSpan.FromMinutes(1));

            _policy.GetLockoutEndTime(Address).Should().BeNull();
            _policy.Authenticate(Address, () => new User()).User.Should().NotBeNull();
        }

        [Test]
        public void failed_attempt_history_should_expire_after_five_minutes_without_failure()
        {
            Fail(Address, 4);
            _clock.Advance(TimeSpan.FromMinutes(5));

            Fail(Address, 4);

            _policy.GetLockoutEndTime(Address).Should().BeNull();
            Fail(Address, 1);
            _policy.GetLockoutEndTime(Address).Should().NotBeNull();
        }

        [Test]
        public void successful_password_verification_should_clear_the_previous_failure_count()
        {
            Fail(Address, 4);

            _policy.Authenticate(Address, () => new User()).User.Should().NotBeNull();
            Fail(Address, 4);

            _policy.GetLockoutEndTime(Address).Should().BeNull();
            Fail(Address, 1);
            _policy.GetLockoutEndTime(Address).Should().NotBeNull();
        }

        [Test]
        public void a_locked_source_should_not_block_another_source()
        {
            Fail(Address, 5);

            var result = _policy.Authenticate("192.0.2.2", () => new User());

            result.User.Should().NotBeNull();
            result.IsBlocked.Should().BeFalse();
            _policy.GetLockoutEndTime(Address).Should().NotBeNull();
        }

        [Test]
        public void a_verification_exception_should_propagate_without_counting_or_resetting_failures()
        {
            Fail(Address, 4);
            var failure = new InvalidOperationException("synthetic password repository failure");

            Assert.Throws<InvalidOperationException>(() => _policy.Authenticate(Address, () => throw failure)).Should().BeSameAs(failure);

            _policy.GetLockoutEndTime(Address).Should().BeNull();
            Fail(Address, 1);
            _policy.GetLockoutEndTime(Address).Should().NotBeNull();
        }

        [Test]
        public void capacity_overflow_should_fail_closed_without_evicting_an_existing_lockout()
        {
            FillCapacity();
            Fail(Source(0), 4);
            var originalEnd = _policy.GetLockoutEndTime(Source(0));
            var calls = 0;

            _policy.Authenticate(Address, () =>
            {
                calls++;
                return new User();
            }).IsBlocked.Should().BeTrue();
            var overflowEnd = _policy.GetLockoutEndTime(Address);
            _clock.Advance(TimeSpan.FromMinutes(1));
            _policy.Authenticate("192.0.2.2", () => new User()).IsBlocked.Should().BeTrue();

            calls.Should().Be(0);
            overflowEnd.Should().Be(_clock.UtcNow.UtcDateTime.AddMinutes(4));
            _policy.GetLockoutEndTime("192.0.2.2").Should().Be(overflowEnd);
            _policy.GetLockoutEndTime(Source(0)).Should().Be(originalEnd);
        }

        [Test]
        public void tracked_clients_should_still_authenticate_at_capacity_and_success_should_free_space()
        {
            FillCapacity();

            _policy.Authenticate(Source(0), () => new User()).User.Should().NotBeNull();
            _policy.Authenticate(Address, () => new User()).User.Should().NotBeNull();

            _policy.GetLockoutEndTime(Address).Should().BeNull();
        }

        [Test]
        public void expiry_should_reclaim_capacity_and_clear_overflow_feedback()
        {
            FillCapacity();
            _policy.Authenticate(Address, () => new User()).IsBlocked.Should().BeTrue();
            _clock.Advance(TimeSpan.FromMinutes(5));

            _policy.Authenticate(Address, () => new User()).User.Should().NotBeNull();
            _policy.GetLockoutEndTime("192.0.2.2").Should().BeNull();
        }

        [Test]
        public void status_queries_should_not_allocate_tracked_clients()
        {
            for (var index = 0; index <= LoginLockoutPolicy.MaximumTrackedAddresses; index++)
            {
                _policy.GetLockoutEndTime(Source(index)).Should().BeNull();
            }

            _policy.Authenticate(Address, () => new User()).User.Should().NotBeNull();
        }

        [Test]
        public void verification_errors_should_not_leave_empty_entries_consuming_capacity()
        {
            for (var index = 0; index < LoginLockoutPolicy.MaximumTrackedAddresses - 1; index++)
            {
                Fail(Source(index), 1);
            }

            Assert.Throws<InvalidOperationException>(() => _policy.Authenticate(Address, () => throw new InvalidOperationException()));

            _policy.Authenticate("192.0.2.2", () => new User()).User.Should().NotBeNull();
        }

        [Test]
        public async Task simultaneous_bad_passwords_should_perform_at_most_five_verifications()
        {
            var calls = 0;
            using var begin = new ManualResetEventSlim();
            var attempts = Enumerable.Range(0, 16).Select(index => Task.Factory.StartNew(
                () =>
                {
                    begin.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                    return _policy.Authenticate(Address, () =>
                    {
                        Interlocked.Increment(ref calls);
                        return null;
                    });
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)).ToArray();

            begin.Set();
            var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));

            calls.Should().Be(5);
            results.Count(result => result.IsBlocked).Should().Be(11);
        }

        [Test]
        public async Task queued_successful_attempts_should_never_use_two_verification_gates_for_one_source()
        {
            var active = 0;
            var overlapping = 0;
            using var begin = new ManualResetEventSlim();
            var attempts = Enumerable.Range(0, 16).Select(index => Task.Factory.StartNew(
                () =>
                {
                    begin.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                    return _policy.Authenticate(Address, () =>
                    {
                        if (Interlocked.Increment(ref active) != 1)
                        {
                            Interlocked.Increment(ref overlapping);
                        }

                        Thread.Sleep(5);
                        Interlocked.Decrement(ref active);
                        return new User();
                    });
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)).ToArray();

            begin.Set();
            var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));

            results.Should().OnlyContain(result => result.User != null && !result.IsBlocked);
            overlapping.Should().Be(0);
        }

        [Test]
        public async Task a_slow_password_verification_should_not_hold_a_global_lock_over_other_sources()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var slow = Task.Factory.StartNew(
                () => _policy.Authenticate(Address, () =>
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                    return new User();
                }),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                var independent = Task.Run(() => _policy.Authenticate("192.0.2.2", () => new User()));
                var result = await independent.WaitAsync(TimeSpan.FromSeconds(2));
                result.User.Should().NotBeNull();
            }
            finally
            {
                release.Set();
                await slow.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [Test]
        public void application_registration_should_share_one_policy_across_scopes()
        {
            using var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(new List<string> { "Readarr.Http" });
            container.RegisterInstance<ISystemClock>(_clock);
            using var firstScope = container.OpenScope();
            using var secondScope = container.OpenScope();
            var first = firstScope.Resolve<ILoginLockoutPolicy>();
            var second = secondScope.Resolve<ILoginLockoutPolicy>();

            first.Should().BeSameAs(second);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                first.Authenticate(Address, () => null);
            }

            second.Authenticate(Address, () => new User()).IsBlocked.Should().BeTrue();
        }

        private void FillCapacity()
        {
            for (var index = 0; index < LoginLockoutPolicy.MaximumTrackedAddresses; index++)
            {
                Fail(Source(index), 1);
            }
        }

        private void Fail(string address, int attempts)
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                _policy.Authenticate(address, () => null).IsBlocked.Should().BeFalse();
            }
        }

        private static string Source(int index) => $"198.18.{index / 256}.{index % 256}";

        private sealed class TestClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

            public void Advance(TimeSpan amount)
            {
                UtcNow += amount;
            }
        }
    }
}
