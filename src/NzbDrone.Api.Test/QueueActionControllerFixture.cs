using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Parser.Model;
using Readarr.Api.V1.Queue;
using Readarr.Http.REST;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class QueueActionControllerFixture
    {
        [Test]
        public async Task should_reject_a_stale_bulk_grab_before_sending_any_downloads()
        {
            var pending = new Mock<IPendingReleaseService>();
            var downloads = new Mock<IDownloadService>();
            var controller = new QueueActionController(pending.Object, downloads.Object);
            pending.Setup(service => service.FindPendingQueueItem(13))
                .Returns(new NzbDrone.Core.Queue.Queue { Id = 13, RemoteBook = new RemoteBook() });

            await Assert.ThrowsAsync<NotFoundException>(async () =>
                await controller.Grab(new QueueBulkResource { Ids = new List<int> { 13, 29 } }));

            downloads.VerifyNoOtherCalls();
        }

        [Test]
        public async Task should_keep_bulk_grab_order_and_share_the_space_budget()
        {
            var pending = new Mock<IPendingReleaseService>();
            var downloads = new Mock<IDownloadService>();
            var controller = new QueueActionController(pending.Object, downloads.Object);
            var first = new RemoteBook();
            var second = new RemoteBook();
            var books = new List<RemoteBook>();
            var budgets = new List<DownloadSpaceBudget>();
            pending.Setup(service => service.FindPendingQueueItem(13))
                .Returns(new NzbDrone.Core.Queue.Queue { Id = 13, RemoteBook = first });
            pending.Setup(service => service.FindPendingQueueItem(29))
                .Returns(new NzbDrone.Core.Queue.Queue { Id = 29, RemoteBook = second });
            downloads.Setup(service => service.DownloadReport(It.IsAny<RemoteBook>(), null, It.IsAny<DownloadSpaceBudget>()))
                .Callback<RemoteBook, int?, DownloadSpaceBudget>((book, _, budget) =>
                {
                    books.Add(book);
                    budgets.Add(budget);
                }).Returns(Task.CompletedTask);

            await controller.Grab(new QueueBulkResource { Ids = new List<int> { 13, 29 } });

            books.Should().Equal(first, second);
            budgets.Should().HaveCount(2);
            budgets[0].Should().NotBeNull().And.BeSameAs(budgets[1]);
        }
    }
}
