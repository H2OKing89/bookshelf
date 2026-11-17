using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.JobTests
{
    [TestFixture]
    public class CancelledScheduledTaskFixture : CoreTest<TaskManager>
    {
        [TestCase(CommandStatus.Cancelled, false)]
        [TestCase(CommandStatus.Completed, true)]
        [TestCase(CommandStatus.Failed, true)]
        public void only_cancelled_scheduled_refresh_should_remain_pending(CommandStatus status, bool advanceSchedule)
        {
            var previousExecution = DateTime.UtcNow.AddDays(-3);
            var scheduled = new ScheduledTask
            {
                Id = 1,
                TypeName = typeof(RefreshAuthorCommand).FullName,
                LastExecution = previousExecution
            };
            Mocker.GetMock<IScheduledTaskRepository>().Setup(repository => repository.All()).Returns(new List<ScheduledTask> { scheduled });
            Subject.Handle(new ApplicationStartedEvent());

            Subject.Handle(new CommandExecutedEvent(new CommandModel
            {
                Body = new RefreshAuthorCommand(),
                Status = status,
                StartedAt = DateTime.UtcNow
            }));

            Mocker.GetMock<IScheduledTaskRepository>().Verify(
                repository => repository.SetLastExecutionTime(scheduled.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()),
                advanceSchedule ? Times.Once() : Times.Never());
            if (advanceSchedule)
            {
                Subject.GetPending().Should().NotContain(scheduled);
                scheduled.LastExecution.Should().BeAfter(previousExecution);
            }
            else
            {
                Subject.GetPending().Should().Contain(scheduled);
                scheduled.LastExecution.Should().Be(previousExecution);
            }
        }
    }
}
