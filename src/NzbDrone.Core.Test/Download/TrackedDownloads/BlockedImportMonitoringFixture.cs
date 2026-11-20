using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class BlockedImportMonitoringFixture : CoreTest<DownloadMonitoringService>
    {
        [TestCase(TrackedDownloadState.ImportBlocked)]
        [TestCase(TrackedDownloadState.ImportFailed)]
        [TestCase(TrackedDownloadState.ImportPending)]
        public void monitoring_detects_client_failure_after_an_import_has_been_blocked(TrackedDownloadState state)
        {
            var item = new DownloadClientItem
            {
                DownloadId = "owned-download", Title = "Owned recording", Status = DownloadItemStatus.Failed,
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 }
            };
            var tracked = new TrackedDownload { State = state, DownloadItem = item, DownloadClient = 7 };
            var client = new Mock<IDownloadClient>();
            var definition = new DownloadClientDefinition { Id = 7 };
            client.SetupGet(provider => provider.Definition).Returns(definition);
            client.Setup(provider => provider.GetItems()).Returns(new[] { item });
            Mocker.GetMock<IDownloadClientFactory>().Setup(factory => factory.DownloadHandlingEnabled()).Returns(new List<IDownloadClient> { client.Object });
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.TrackDownload(definition, item)).Returns(tracked);
            Mocker.GetMock<IHistoryService>().Setup(history => history.Find("owned-download", EntityHistoryEventType.Grabbed, 7))
                .Returns(new List<EntityHistory> { new EntityHistory { DownloadId = "owned-download" } });
            Mocker.SetConstant<IFailedDownloadService>(new FailedDownloadService(
                Mocker.GetMock<IHistoryService>().Object,
                Mocker.GetMock<ITrackedDownloadService>().Object,
                Mocker.GetMock<IEventAggregator>().Object,
                Mocker.GetMock<IDownloadHistoryRepository>().Object));
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableCompletedDownloadHandling).Returns(true);

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            tracked.State.Should().Be(TrackedDownloadState.DownloadFailedPending);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }
    }
}
