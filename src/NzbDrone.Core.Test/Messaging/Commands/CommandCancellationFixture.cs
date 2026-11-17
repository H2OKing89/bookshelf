using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandCancellationFixture : CoreTest<CommandExecutor>
    {
        private readonly List<ManualResetEventSlim> _events = new List<ManualResetEventSlim>();
        private readonly List<CommandModel> _commands = new List<CommandModel>();
        private CommandQueueManager _queue;
        private Mock<IExecute<RefreshAuthorCommand>> _handler;

        [SetUp]
        public void Setup()
        {
            var nextId = 0;
            Mocker.GetMock<ICommandRepository>().Setup(repository => repository.Insert(It.IsAny<CommandModel>()))
                .Returns((CommandModel model) =>
                {
                    model.Id = Interlocked.Increment(ref nextId);
                    return model;
                });
            _queue = new CommandQueueManager(
                Mocker.GetMock<ICommandRepository>().Object,
                Mocker.GetMock<IServiceFactory>().Object,
                new KnownTypes(new List<Type>()),
                TestLogger);
            Mocker.SetConstant<IManageCommandQueue>(_queue);
            _handler = new Mock<IExecute<RefreshAuthorCommand>>();
            Mocker.GetMock<IServiceFactory>().Setup(factory => factory.Build(typeof(IExecute<RefreshAuthorCommand>)))
                .Returns(_handler.Object);
        }

        [TearDown]
        public void Teardown()
        {
            Subject.Handle(new ApplicationShutdownRequested());
            foreach (var signal in _events)
            {
                signal.Set();
            }

            SpinWait.SpinUntil(() => _commands.TrueForAll(command => command.Status != CommandStatus.Started), 5000)
                .Should().BeTrue();
            foreach (var signal in _events)
            {
                signal.Dispose();
            }

            _events.Clear();
            _commands.Clear();
        }

        private ManualResetEventSlim Signal()
        {
            var signal = new ManualResetEventSlim();
            _events.Add(signal);
            return signal;
        }

        private CommandModel Push(int id)
        {
            var model = _queue.Push(new RefreshAuthorCommand(id));
            _commands.Add(model);
            return model;
        }

        [Test]
        public void cancelling_a_running_refresh_should_cancel_only_that_command()
        {
            var firstEntered = Signal();
            var secondEntered = Signal();
            var finishSecond = Signal();
            CancellationToken siblingToken = default;
            _handler.Setup(handler => handler.Execute(It.IsAny<RefreshAuthorCommand>()))
                .Callback((RefreshAuthorCommand command) =>
                {
                    var token = CommandExecutionContext.CancellationToken;
                    if (command.AuthorId == 1)
                    {
                        firstEntered.Set();
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                        token.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        siblingToken = token;
                        secondEntered.Set();
                        finishSecond.Wait(TimeSpan.FromSeconds(5));
                        token.ThrowIfCancellationRequested();
                    }
                });
            var first = Push(1);
            var second = Push(2);
            Subject.Handle(new ApplicationStartedEvent());
            firstEntered.Wait(5000).Should().BeTrue();
            secondEntered.Wait(5000).Should().BeTrue();

            _queue.Cancel(first.Id);

            SpinWait.SpinUntil(() => first.Status == CommandStatus.Cancelled, 5000).Should().BeTrue();
            siblingToken.IsCancellationRequested.Should().BeFalse();
            finishSecond.Set();
            SpinWait.SpinUntil(() => second.Status == CommandStatus.Completed, 5000).Should().BeTrue();
            first.Exception.Should().BeNull();
        }

        [Test]
        public void shutdown_should_cancel_running_metadata_work_without_marking_it_failed()
        {
            var entered = Signal();
            _handler.Setup(handler => handler.Execute(It.IsAny<RefreshAuthorCommand>()))
                .Callback(() =>
                {
                    var token = CommandExecutionContext.CancellationToken;
                    entered.Set();
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    token.ThrowIfCancellationRequested();
                });
            var command = Push(1);
            Subject.Handle(new ApplicationStartedEvent());
            entered.Wait(5000).Should().BeTrue();

            Subject.Handle(new ApplicationShutdownRequested());

            SpinWait.SpinUntil(() => command.Status == CommandStatus.Cancelled, 5000).Should().BeTrue();
            command.Exception.Should().BeNull();
        }

        [Test]
        public void a_completed_write_unit_should_remain_successful_after_a_late_cancellation_request()
        {
            var entered = Signal();
            _handler.Setup(handler => handler.Execute(It.IsAny<RefreshAuthorCommand>()))
                .Callback(() =>
                {
                    entered.Set();
                    CommandExecutionContext.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)).Should().BeTrue();
                });
            var command = Push(1);
            Subject.Handle(new ApplicationStartedEvent());
            entered.Wait(5000).Should().BeTrue();

            _queue.Cancel(command.Id);

            SpinWait.SpinUntil(() => command.Status == CommandStatus.Completed, 5000).Should().BeTrue();
            command.Result.Should().Be(CommandResult.Successful);
            command.Exception.Should().BeNull();
        }

        [Test]
        public void unrelated_operation_cancelled_exception_should_still_fail_the_command()
        {
            _handler.Setup(handler => handler.Execute(It.IsAny<RefreshAuthorCommand>()))
                .Throws(new OperationCanceledException("Synthetic unrelated cancellation"));
            var command = Push(1);
            Subject.Handle(new ApplicationStartedEvent());

            SpinWait.SpinUntil(() => command.Status == CommandStatus.Failed, 5000).Should().BeTrue();

            command.Exception.Should().Contain(nameof(OperationCanceledException));
            ExceptionVerification.WaitForErrors(1, 5000);
        }

        [Test]
        public void queued_cancellation_should_be_persisted_so_restart_cannot_requeue_it()
        {
            var command = Push(1);

            _queue.Cancel(command.Id);

            command.Status.Should().Be(CommandStatus.Cancelled);
            command.StartedAt.Should().BeNull();
            command.EndedAt.Should().NotBeNull();
            Mocker.GetMock<ICommandRepository>().Verify(repository => repository.End(command), Times.Once());
        }

        [Test]
        public void executing_non_cancellable_commands_should_keep_the_conflict_boundary()
        {
            var command = _queue.Push(new CommandA());
            using var enumerator = _queue.Queue(CancellationToken.None).GetEnumerator();
            enumerator.MoveNext().Should().BeTrue();

            Assert.Throws<NzbDrone.Core.Exceptions.NzbDroneClientException>(() => _queue.Cancel(command.Id));

            command.Status.Should().Be(CommandStatus.Started);
            command.CancellationState.Finish();
        }
    }
}
