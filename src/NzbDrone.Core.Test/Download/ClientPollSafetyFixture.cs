using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ClientPollSafetyFixture : CoreTest<DownloadProcessingService>
    {
        [TestCase(false)]
        [TestCase(true)]
        public void imported_jobs_require_a_current_successful_client_poll_before_cleanup(bool available)
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 7, State = TrackedDownloadState.Imported, IsClientAvailable = available,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-import", Status = DownloadItemStatus.Completed, CanBeRemoved = true }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.GetTrackedDownloads()).Returns(new List<TrackedDownload> { tracked });
            Mocker.GetMock<IDownloadImportCompletionService>().Setup(service => service.ReadReceipt(tracked, It.IsAny<DownloadHistory>())).Returns(new DownloadImportReceipt());

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), available ? Times.Once() : Times.Never());
            Mocker.GetMock<IDownloadImportCompletionService>().Verify(service => service.ReadReceipt(tracked, It.IsAny<DownloadHistory>()), available ? Times.Once() : Times.Never());
        }

        [TestCase(TrackedDownloadState.ImportPending)]
        [TestCase(TrackedDownloadState.DownloadFailedPending)]
        public void jobs_from_a_partial_poll_cannot_import_or_enter_failure_cleanup(TrackedDownloadState state)
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 7, State = state, IsTrackable = true, IsClientAvailable = false,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-stale", Status = DownloadItemStatus.Completed, CanBeRemoved = true }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.GetTrackedDownloads()).Returns(new List<TrackedDownload> { tracked });
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableCompletedDownloadHandling).Returns(true);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Import(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IFailedDownloadService>().Verify(service => service.ProcessFailed(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), Times.Never());
        }
    }
}
