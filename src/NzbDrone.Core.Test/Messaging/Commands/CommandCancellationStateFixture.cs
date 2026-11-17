using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandCancellationStateFixture
    {
        [Test]
        public void cancellation_before_execution_should_be_observed_when_it_starts()
        {
            var state = new CommandCancellationState();
            state.MarkRequested();
            state.NotifyCancellation();

            state.Start(CancellationToken.None).IsCancellationRequested.Should().BeTrue();

            state.Finish();
            state.NotifyCancellation();
            state.Finish();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task completing_while_cancel_callback_runs_should_not_dispose_or_block_it(bool shutdown)
        {
            var state = new CommandCancellationState();
            using var shutdownSource = new CancellationTokenSource();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var token = state.Start(shutdownSource.Token);
            using var registration = token.Register(() =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            });
            var cancellation = Task.Run(() =>
            {
                if (shutdown)
                {
                    shutdownSource.Cancel();
                }
                else
                {
                    state.MarkRequested();
                    state.NotifyCancellation();
                }
            });
            try
            {
                entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                var finish = Task.Run(state.Finish);
                (await Task.WhenAny(finish, Task.Delay(2000))).Should().BeSameAs(finish);
                await finish;
            }
            finally
            {
                release.Set();
                await cancellation;
                state.Finish();
            }
        }

        [Test]
        public async Task ambient_scope_should_flow_to_async_work_and_restore_the_previous_scope()
        {
            using var outer = new CancellationTokenSource();
            using var inner = new CancellationTokenSource();
            using (CommandExecutionContext.Begin(outer.Token))
            {
                using (CommandExecutionContext.Begin(inner.Token))
                {
                    (await Task.Run(() => CommandExecutionContext.CancellationToken)).Should().Be(inner.Token);
                }

                CommandExecutionContext.CancellationToken.Should().Be(outer.Token);
            }

            CommandExecutionContext.CancellationToken.Should().Be(CancellationToken.None);
        }
    }
}
