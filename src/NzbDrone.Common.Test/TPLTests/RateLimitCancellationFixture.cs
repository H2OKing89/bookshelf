using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.TPL;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.TPLTests
{
    [TestFixture]
    public class RateLimitCancellationFixture : TestBase<RateLimitService>
    {
        [TestCase(null)]
        [TestCase("sub")]
        public void pre_cancelled_wait_should_not_reserve_a_rate_limit_slot(string subKey)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var store = Store();

            Assert.CatchAsync<OperationCanceledException>(async () => await Subject.WaitAndPulseAsync("unused", subKey, TimeSpan.FromMinutes(1), cancellation.Token));

            store.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("sub")]
        public async Task pending_rate_limit_delay_should_stop_when_its_caller_cancels(string subKey)
        {
            using var cancellation = new CancellationTokenSource();
            Store()["slow"] = DateTime.UtcNow.AddMinutes(2);
            var operation = Subject.WaitAndPulseAsync("slow", subKey, TimeSpan.FromMinutes(1), cancellation.Token);
            operation.IsCompleted.Should().BeFalse();

            cancellation.Cancel();

            var error = Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            error.CancellationToken.Should().Be(cancellation.Token);
            await Subject.WaitAndPulseAsync("unrelated", TimeSpan.FromMinutes(1)).WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Test]
        public void synchronous_rate_limit_wait_should_remain_cancellable_while_waiting()
        {
            using var cancellation = new CancellationTokenSource();
            var seed = DateTime.UtcNow.AddMinutes(1);
            var store = Store();
            store["slow"] = seed;
            var operation = Task.Run(() => Subject.WaitAndPulse("slow", TimeSpan.FromMinutes(1), cancellation.Token));
            SpinWait.SpinUntil(() => store["slow"] > seed, TimeSpan.FromSeconds(5)).Should().BeTrue();

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        private ConcurrentDictionary<string, DateTime> Store()
        {
            return Mocker.Resolve<ICacheManager>()
                .GetCache<ConcurrentDictionary<string, DateTime>>(typeof(RateLimitService), "rateLimitStore")
                .Get("rateLimitStore", () => new ConcurrentDictionary<string, DateTime>());
        }
    }
}
