using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Queue;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Queue;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class ClientScopedQueueFixture : TestBase<QueueController>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ICustomFormatService>().Setup(formats => formats.All()).Returns(new List<CustomFormat>());
        }

        private TrackedDownload Add(int queueId, int clientId)
        {
            var tracked = new TrackedDownload { DownloadClient = clientId, DownloadItem = new DownloadClientItem { DownloadId = "shared" } };
            Mocker.GetMock<IQueueService>().Setup(queue => queue.Find(queueId))
                .Returns(new NzbDrone.Core.Queue.Queue { Id = queueId, DownloadId = "shared", DownloadClientId = clientId });
            Mocker.GetMock<ITrackedDownloadService>().Setup(tracker => tracker.Find("shared", clientId)).Returns(tracked);
            Mocker.GetMock<IIgnoredDownloadService>().Setup(ignored => ignored.IgnoreDownload(tracked)).Returns(true);
            return tracked;
        }

        [Test]
        public void bulk_should_preserve_two_clients_with_same_id_and_deduplicate_only_same_job()
        {
            Add(1, 12);
            Add(2, 13);
            Subject.RemoveMany(new QueueBulkResource { Ids = new List<int> { 1, 2, 1 } }, removeFromClient: false);

            Mocker.GetMock<ITrackedDownloadService>().Verify(tracker => tracker.StopTracking("shared", 12), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(tracker => tracker.StopTracking("shared", 13), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(tracker => tracker.Find("shared"), Times.Never());
        }

        [Test]
        public void failed_ignore_should_keep_job_tracked()
        {
            var tracked = Add(1, 12);
            Mocker.GetMock<IIgnoredDownloadService>().Setup(ignored => ignored.IgnoreDownload(tracked)).Returns(false);

            Subject.RemoveAction(1, removeFromClient: false);

            Mocker.GetMock<ITrackedDownloadService>().Verify(tracker => tracker.StopTracking(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void queue_resource_should_expose_client_scope_and_freshness()
        {
            var resource = new NzbDrone.Core.Queue.Queue
            {
                DownloadId = "shared", DownloadClientId = 12, DownloadClientAvailable = false, Status = "Completed"
            }.ToResource(false, false);

            resource.DownloadClientId.Should().Be(12);
            resource.DownloadClientAvailable.Should().BeFalse();
        }

        [Test]
        public void pending_release_without_a_client_should_not_be_reported_as_unavailable()
        {
            var resource = new NzbDrone.Core.Queue.Queue { Status = "Delay" }.ToResource(false, false);

            resource.DownloadClientId.Should().BeNull();
            resource.DownloadClientAvailable.Should().BeNull();
        }
    }
}
