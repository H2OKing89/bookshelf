using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Queue;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Queue;
using Readarr.Http;
using Readarr.Http.REST;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class QueueFilterFixture : TestBase<QueueController>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ICustomFormatService>().Setup(formats => formats.All()).Returns(new List<CustomFormat>());
            Mocker.GetMock<IQueueService>().Setup(queue => queue.GetQueue()).Returns(new List<Queue>
            {
                Item(1, "A", "Downloading", DownloadProtocol.Torrent),
                Item(2, "B", "Completed", DownloadProtocol.Usenet),
                Item(3, "C", "Downloading", DownloadProtocol.Usenet),
                Item(4, "D", "Downloading", DownloadProtocol.Torrent),
                Item(5, "E", "Downloading", DownloadProtocol.Torrent, hasAuthor: false),
                Item(6, "F", "Completed", DownloadProtocol.Torrent),
                Item(7, "G", "Downloading", DownloadProtocol.Torrent),
            });
            Mocker.GetMock<IPendingReleaseService>().Setup(pending => pending.GetPendingQueue()).Returns(new List<Queue>
            {
                Item(8, "H", "Delay", DownloadProtocol.Usenet),
                Item(9, "I", "Delay", DownloadProtocol.Torrent),
            });
        }

        [Test]
        public void filters_should_be_combined_before_counting_and_paging()
        {
            var page = Subject.GetQueue(Page(2, 2), status: "downloading", protocol: DownloadProtocol.Torrent);

            page.TotalRecords.Should().Be(3);
            page.Records.Should().ContainSingle().Which.Id.Should().Be(7);

            page = Subject.GetQueue(Page(2, 2), includeUnknownAuthorItems: true, status: "Downloading", protocol: DownloadProtocol.Torrent);

            page.TotalRecords.Should().Be(4);
            page.Records.Should().SatisfyRespectively(item => item.Id.Should().Be(5), item => item.Id.Should().Be(7));
        }

        [Test]
        public void filters_should_apply_to_pending_releases()
        {
            var page = Subject.GetQueue(Page(1, 10), status: "delay", protocol: DownloadProtocol.Usenet);

            page.TotalRecords.Should().Be(1);
            page.Records.Should().ContainSingle().Which.Id.Should().Be(8);
        }

        [Test]
        public void page_past_a_partial_last_page_should_return_that_page()
        {
            var page = Subject.GetQueue(Page(int.MaxValue, 2), status: "downloading", protocol: DownloadProtocol.Torrent);

            page.Page.Should().Be(2);
            page.Records.Should().ContainSingle().Which.Id.Should().Be(7);
        }

        [Test]
        public void no_match_should_have_zero_records_on_the_first_page()
        {
            var page = Subject.GetQueue(Page(3, 2), status: "failed");

            page.Page.Should().Be(1);
            page.TotalRecords.Should().Be(0);
            page.Records.Should().BeEmpty();
        }

        [Test]
        public void invalid_filters_and_paging_should_be_rejected()
        {
            Assert.Throws<BadRequestException>(() => Subject.GetQueue(Page(1, 10), status: "unlisted"));
            Assert.Throws<BadRequestException>(() => Subject.GetQueue(Page(1, 10), protocol: (DownloadProtocol)42));
            Assert.Throws<BadRequestException>(() => Subject.GetQueue(Page(1, 0)));
        }

        private static PagingRequestResource Page(int page, int size)
        {
            return new PagingRequestResource { Page = page, PageSize = size, SortKey = "title", SortDirection = SortDirection.Ascending };
        }

        private static Queue Item(int id, string title, string status, DownloadProtocol protocol, bool hasAuthor = true)
        {
            return new Queue { Id = id, Title = title, Status = status, Protocol = protocol, Author = hasAuthor ? new Author { Id = 1 } : null };
        }
    }
}
