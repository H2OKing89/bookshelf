using System;
using System.Linq;
using System.Threading;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandExecutorFixture : TestBase<CommandExecutor>
    {
        private CommandQueue _commandQueue;
        private Mock<IExecute<CommandA>> _executorA;
        private Mock<IExecute<CommandB>> _executorB;

        [SetUp]
        public void Setup()
        {
            _executorA = new Mock<IExecute<CommandA>>();
            _executorB = new Mock<IExecute<CommandB>>();

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.Build(typeof(IExecute<CommandA>)))
                  .Returns(_executorA.Object);

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.Build(typeof(IExecute<CommandB>)))
                  .Returns(_executorB.Object);
        }

        [TearDown]
        public void TearDown()
        {
            Subject.Handle(new ApplicationShutdownRequested());

            Assert.That(Subject.WaitForShutdown(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
        }

        private void GivenCommandQueue()
        {
            _commandQueue = new CommandQueue();

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Queue(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken token) => _commandQueue.GetConsumingEnumerable(token));
        }

        private void QueueAndWaitForExecution(CommandModel commandModel, bool waitPublish = false)
        {
            var waitEventComplete = new ManualResetEventSlim();
            var waitEventPublish = new ManualResetEventSlim();

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), It.IsAny<string>()))
                  .Callback(() => waitEventComplete.Set());

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Fail(It.Is<CommandModel>(c => c == commandModel), It.IsAny<string>(), It.IsAny<Exception>()))
                  .Callback(() => waitEventComplete.Set());

            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent<CommandExecutedEvent>(It.IsAny<CommandExecutedEvent>()))
                  .Callback(() => waitEventPublish.Set());

            _commandQueue.Add(commandModel);

            if (!waitEventComplete.Wait(15000))
            {
                Assert.Fail("Command did not Complete/Fail within 15 sec");
            }

            if (waitPublish && !waitEventPublish.Wait(500))
            {
                Assert.Fail("Command did not Publish within 500 msec");
            }
        }

        [Test]
        public void should_start_executor_threads()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Queue(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Test]
        public void disabled_profiling_never_begins_a_job_on_the_worker()
        {
            GivenCommandQueue();
            var handler = new Mock<IExecute<RefreshAuthorCommand>>();
            Mocker.GetMock<IServiceFactory>().Setup(factory => factory.Build(typeof(IExecute<RefreshAuthorCommand>))).Returns(handler.Object);
            Subject.Handle(new ApplicationStartedEvent());
            QueueAndWaitForExecution(new CommandModel { Body = new RefreshAuthorCommand(1), Trigger = CommandTrigger.Scheduled });
            Mocker.GetMock<IPerformanceProfiler>().Verify(profiler => profiler.BeginJob(It.IsAny<Command>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void worker_records_completed_unsuccessful_and_failed_outcomes(bool unsuccessful, bool exception)
        {
            GivenCommandQueue();
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(value => value.PerformanceProfilingEnabled).Returns(true);
            using var profiler = new PerformanceProfiler(config.Object, TimeProvider.System);
            Mocker.SetConstant<IPerformanceProfiler>(profiler);
            var command = new CommandModel { Body = new RefreshAuthorCommand(1), Trigger = CommandTrigger.Scheduled };
            var handler = new Mock<IExecute<RefreshAuthorCommand>>();
            handler.Setup(value => value.Execute(It.IsAny<RefreshAuthorCommand>())).Callback(() =>
            {
                if (exception)
                {
                    throw new InvalidOperationException("Owned failed job");
                }

                if (unsuccessful)
                {
                    command.Result = CommandResult.Unsuccessful;
                }
            });
            Mocker.GetMock<IServiceFactory>().Setup(factory => factory.Build(typeof(IExecute<RefreshAuthorCommand>))).Returns(handler.Object);
            Subject.Handle(new ApplicationStartedEvent());
            QueueAndWaitForExecution(command);
            Subject.Handle(new ApplicationShutdownRequested());
            Assert.That(Subject.WaitForShutdown(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
            var sample = profiler.Snapshot().Jobs.Single();
            Assert.That(sample.Executions, Is.EqualTo(1));
            Assert.That(sample.Trigger, Is.EqualTo(CommandTrigger.Scheduled));
            Assert.That(sample.Failed, Is.EqualTo(unsuccessful || exception ? 1 : 0));
            Assert.That(sample.Cancelled, Is.Zero);
            if (exception)
            {
                ExceptionVerification.WaitForErrors(1, 500);
            }
        }

        [Test]
        public void worker_records_cooperative_cancellation_once()
        {
            GivenCommandQueue();
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(value => value.PerformanceProfilingEnabled).Returns(true);
            using var profiler = new PerformanceProfiler(config.Object, TimeProvider.System);
            Mocker.SetConstant<IPerformanceProfiler>(profiler);
            using var entered = new ManualResetEventSlim();
            var handler = new Mock<IExecute<RefreshAuthorCommand>>();
            handler.Setup(value => value.Execute(It.IsAny<RefreshAuthorCommand>())).Callback(() =>
            {
                entered.Set();
                var token = CommandExecutionContext.CancellationToken;
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                token.ThrowIfCancellationRequested();
            });
            Mocker.GetMock<IServiceFactory>().Setup(factory => factory.Build(typeof(IExecute<RefreshAuthorCommand>))).Returns(handler.Object);
            Subject.Handle(new ApplicationStartedEvent());
            _commandQueue.Add(new CommandModel { Body = new RefreshAuthorCommand(1), Trigger = CommandTrigger.Manual });
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Subject.Handle(new ApplicationShutdownRequested());
            Assert.That(Subject.WaitForShutdown(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
            var sample = profiler.Snapshot().Jobs.Single();
            Assert.That(sample.Executions, Is.EqualTo(1));
            Assert.That(sample.Trigger, Is.EqualTo(CommandTrigger.Manual));
            Assert.That(sample.Cancelled, Is.EqualTo(1));
            Assert.That(sample.Failed, Is.Zero);
        }

        [Test]
        public void should_wait_for_running_cooperative_command_to_cancel_before_worker_shutdown()
        {
            GivenCommandQueue();
            using var entered = new ManualResetEventSlim();
            _executorA.Setup(executor => executor.Execute(It.IsAny<CommandA>())).Callback(() =>
            {
                entered.Set();
                var token = CommandExecutionContext.CancellationToken;
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                token.ThrowIfCancellationRequested();
            });
            var model = new CommandModel { Body = new CommandA() };
            Subject.Handle(new ApplicationStartedEvent());
            _commandQueue.Add(model);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            Subject.Handle(new ApplicationShutdownRequested());
            Assert.That(Subject.WaitForShutdown(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
            Mocker.GetMock<IManageCommandQueue>().Verify(manager => manager.Cancelled(model), Times.Once());
        }

        [Test]
        public void should_execute_on_executor()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            _executorA.Verify(c => c.Execute(commandA), Times.Once());
        }

        [Test]
        public void should_not_execute_on_incompatible_executor()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            _executorA.Verify(c => c.Execute(commandA), Times.Once());
            _executorB.Verify(c => c.Execute(It.IsAny<CommandB>()), Times.Never());
        }

        [Test]
        public void broken_executor_should_publish_executed_event()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            _executorA.Setup(s => s.Execute(It.IsAny<CommandA>()))
                      .Throws(new NotImplementedException());

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel, true);

            VerifyEventPublished<CommandExecutedEvent>();

            ExceptionVerification.WaitForErrors(1, 500);
        }

        [Test]
        public void should_publish_executed_event_on_success()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel, true);

            VerifyEventPublished<CommandExecutedEvent>();
        }

        [Test]
        public void should_use_completion_message()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), commandA.CompletionMessage), Times.Once());
        }

        [Test]
        public void should_use_last_progress_message_if_completion_message_is_null()
        {
            GivenCommandQueue();
            var commandB = new CommandB();
            var commandModel = new CommandModel
            {
                Body = commandB,
                Message = "Do work"
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), commandModel.Message), Times.Once());
        }
    }

    public class CommandA : Command
    {
        public CommandA(int id = 0)
        {
        }
    }

    public class CommandB : Command
    {
        public override string CompletionMessage => null;
    }
}
