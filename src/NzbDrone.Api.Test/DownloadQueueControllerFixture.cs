using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Queue;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Queue;
using Readarr.Http;
using Readarr.Http.REST;
using NzbDroneClientException = NzbDrone.Core.Exceptions.NzbDroneClientException;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class DownloadQueueControllerFixture : TestBase<DownloadQueueController>
    {
        private List<QueueDownload> _rows;
        private Mock<IDownloadClient> _client;

        [SetUp]
        public void SetUp()
        {
            _rows = new List<QueueDownload>();
            _client = new Mock<IDownloadClient>();
            Mocker.GetMock<ICustomFormatService>().Setup(formats => formats.All()).Returns(new List<CustomFormat>());
            Mocker.GetMock<IDownloadQueueService>().Setup(service => service.GetDownloads()).Returns(_rows);
            Mocker.GetMock<IProvideDownloadClient>().Setup(clients => clients.Get(It.IsAny<int>())).Returns(_client.Object);
        }

        [Test]
        public void valid_removal_acts_once_on_each_client_with_identical_download_id()
        {
            var first = Add(21);
            var second = Add(22);
            Subject.Remove(Request());
            _client.Verify(client => client.RemoveItem(first.DownloadItem, true), Times.Once());
            _client.Verify(client => client.RemoveItem(second.DownloadItem, true), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(service => service.StopTracking("shared", 21), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(service => service.StopTracking("shared", 22), Times.Once());
        }

        [Test]
        public void changed_revision_and_duplicate_targets_are_rejected_before_any_action()
        {
            Add(21);
            Add(22);
            var request = Request();
            request.Targets[1].Revision = "stale";
            Assert.Throws<NzbDroneClientException>(() => Subject.Remove(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            request.Targets[1] = request.Targets[0];
            Assert.Throws<BadRequestException>(() => Subject.Remove(request));
            _client.VerifyNoOtherCalls();
        }

        [Test]
        public void changed_client_snapshot_is_rejected_even_when_published_revision_is_unchanged()
        {
            Add(21);
            var second = Add(22);
            second.DownloadItem.Category = "new category";
            Assert.Throws<NzbDroneClientException>(() => Subject.Remove(Request())).StatusCode.Should().Be(HttpStatusCode.Conflict);
            _client.VerifyNoOtherCalls();
        }

        [Test]
        public void pending_removal_uses_only_the_selected_persisted_release()
        {
            _rows.Add(new QueueDownload
            {
                Id = "pending:9",
                Revision = "pending revision",
                Kind = QueueDownloadKind.Pending,
                Item = new Queue { PendingReleaseId = 9 }
            });
            var request = Request();
            request.RemoveFromClient = false;
            Subject.Remove(request);
            Mocker.GetMock<IPendingReleaseService>().Verify(service => service.RemovePendingQueueRelease(9), Times.Once());
            Mocker.GetMock<IPendingReleaseService>().Verify(service => service.RemovePendingQueueItems(It.IsAny<int>()), Times.Never());
            _client.VerifyNoOtherCalls();
        }

        [Test]
        public void status_and_filter_counts_are_download_level_and_include_unresolved_when_requested()
        {
            Add(21);
            Add(22);
            _rows[0].Author = new Author { Id = 10 };
            var status = Subject.GetStatus();
            status.TotalCount.Should().Be(2);
            status.Count.Should().Be(1);
            status.UnknownCount.Should().Be(1);
            var page = Subject.GetQueue(new PagingRequestResource { Page = 999, PageSize = 1 }, true, "downloading", DownloadProtocol.Torrent);
            page.TotalRecords.Should().Be(2);
            page.Page.Should().Be(2);
            page.Records.Should().ContainSingle().Which.Id.Should().Be("download:22:shared");
        }

        private DownloadQueueRemoveRequest Request() => new DownloadQueueRemoveRequest
        {
            RemoveFromClient = true,
            Targets = _rows.Select(row => new DownloadQueueTarget { Id = row.Id, Revision = row.Revision }).ToList()
        };

        private TrackedDownload Add(int clientId)
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = clientId,
                IsClientAvailable = true,
                DownloadItem = new DownloadClientItem { DownloadId = "shared", Status = DownloadItemStatus.Downloading, Title = "Owned" }
            };
            _rows.Add(new QueueDownload
            {
                Id = $"download:{clientId}:shared",
                Revision = "revision",
                Kind = QueueDownloadKind.Download,
                Recordings = new List<QueueRecording>(),
                CanRemoveFromClient = true,
                Item = new Queue
                {
                    DownloadId = "shared",
                    DownloadClientId = clientId,
                    DownloadClientAvailable = true,
                    Status = "Downloading",
                    Title = "Owned",
                    OutputPath = tracked.DownloadItem.OutputPath.ToString(),
                    Protocol = DownloadProtocol.Torrent,
                    TrackedDownloadState = tracked.State,
                    TrackedDownloadStatus = tracked.Status
                }
            });
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("shared", clientId)).Returns(tracked);
            return tracked;
        }
    }
}
