using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Instrumentation.Commands;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Test.Instrumentation
{
    [TestFixture]
    public class OptionalLogStorageFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void clear_logs_and_housekeeping_should_use_the_effective_startup_state(bool enabled)
        {
            var database = new Mock<ILogDatabase>();
            database.SetupGet(item => item.IsEnabled).Returns(enabled);
            var repository = new Mock<ILogRepository>();
            var service = new LogService(repository.Object, database.Object);
            var housekeeping = new TrimLogDatabase(repository.Object, database.Object);

            service.Execute(new ClearLogCommand());
            housekeeping.Clean();

            repository.Verify(item => item.Purge(true), enabled ? Times.Once() : Times.Never());
            repository.Verify(item => item.Trim(), enabled ? Times.Once() : Times.Never());
            database.Verify(item => item.OpenConnection(), Times.Never());
        }

        [Test]
        public void disabled_update_history_should_never_read_write_or_purge_the_log_database()
        {
            var repository = new Mock<IUpdateHistoryRepository>(MockBehavior.Strict);
            var database = new Mock<ILogDatabase>();
            database.SetupGet(item => item.IsEnabled).Returns(false);
            var events = new Mock<IEventAggregator>();
            var service = new UpdateHistoryService(repository.Object, events.Object, LogManager.CreateNullLogger(), database.Object);

            service.PreviouslyInstalled().Should().BeNull();
            service.InstalledSince(DateTime.UtcNow.AddDays(-1)).Should().BeEmpty();
            service.Handle(new ApplicationStartedEvent());
            service.HandleAsync(new ApplicationStartedEvent());

            repository.VerifyNoOtherCalls();
            events.VerifyNoOtherCalls();
            database.Verify(item => item.OpenConnection(), Times.Never());
        }

        [Test]
        public void enabled_update_history_should_keep_existing_read_results()
        {
            var previous = new UpdateHistory { Version = new Version(1, 2, 3) };
            var since = DateTime.UtcNow.AddDays(-1);
            var installed = new List<UpdateHistory> { previous };
            var repository = new Mock<IUpdateHistoryRepository>();
            repository.Setup(item => item.PreviouslyInstalled()).Returns(previous);
            repository.Setup(item => item.InstalledSince(since)).Returns(installed);
            var database = new Mock<ILogDatabase>();
            database.SetupGet(item => item.IsEnabled).Returns(true);
            var service = new UpdateHistoryService(repository.Object, Mock.Of<IEventAggregator>(), LogManager.CreateNullLogger(), database.Object);

            service.PreviouslyInstalled().Should().Be(previous.Version);
            service.InstalledSince(since).Should().BeSameAs(installed);
        }
    }
}
