using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using QueueRow = NzbDrone.Core.Queue.Queue;

namespace NzbDrone.Core.Test.QueueTests
{
    [TestFixture]
    public class DownloadQueueServiceFixture : CoreTest<DownloadQueueService>
    {
        private readonly Author _author = new Author { Id = 10 };
        private List<Book> _books;
        private List<QueueRow> _queue;
        private List<QueueRow> _pending;

        [SetUp]
        public void SetUp()
        {
            _books = new List<Book>
            {
                new Book { Id = 101, ForeignBookId = "recording-us" },
                new Book { Id = 102, ForeignBookId = "recording-uk" },
                new Book { Id = 103, ForeignBookId = "recording-other" }
            };
            _queue = new List<QueueRow>();
            _pending = new List<QueueRow>();
            Mocker.GetMock<IQueueService>().Setup(service => service.GetQueue()).Returns(_queue);
            Mocker.GetMock<IPendingReleaseService>().Setup(service => service.GetPendingQueue()).Returns(_pending);
            Mocker.GetMock<IBookRepository>().Setup(service => service.FindByIds(It.IsAny<IEnumerable<int>>()))
                .Returns((IEnumerable<int> ids) => _books.Where(book => ids.Contains(book.Id)).ToList());
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindAuthors(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Author> { _author });
            Mocker.GetMock<IHistoryService>().Setup(service => service.Find(It.IsAny<string>(), EntityHistoryEventType.Grabbed, It.IsAny<int>()))
                .Returns(new List<EntityHistory>());
        }

        [Test]
        public void groups_download_rows_by_client_and_id_without_combining_recordings()
        {
            var a = Row(21, _books[0], _books[1]);
            var b = Row(22, _books[2]);
            _queue.AddRange(new[] { a, Row(21, _books[0], _books[1]), b });
            RecordGrab(21, "[101,102]");
            RecordGrab(22, "[103]");
            var rows = Subject.GetDownloads();
            rows.Should().HaveCount(2);
            var first = rows.Single(row => row.Id == "download:21:same-id");
            first.Recordings.Select(recording => recording.RecordingId).Should().Equal("recording-us", "recording-uk");
            first.CoverageSource.Should().Be(QueueCoverageSource.RecordedGrab);
            first.Item.Size.Should().Be(500);
            rows.Single(row => row.Id == "download:22:same-id").Recordings.Should().ContainSingle().Which.BookId.Should().Be(103);
        }

        [Test]
        public void recorded_coverage_wins_over_incorrect_mapping_and_missing_books_remain_explicit()
        {
            _queue.Add(Row(21, _books[2]));
            RecordGrab(21, "[101,102,999]");
            var row = Subject.GetDownloads().Single();
            row.Recordings.Select(recording => recording.BookId).Should().Equal(101, 102, 999);
            row.Recordings.Last().RecordingId.Should().BeNull();
            row.CanIgnore.Should().BeFalse();
            row.CanBlocklist.Should().BeFalse();
            row.CanRemoveFromClient.Should().BeTrue();
        }

        [Test]
        public void fallback_mapping_is_not_recorded_coverage_and_unknown_remains_visible()
        {
            _queue.Add(Row(21, _books[0]));
            _queue.Add(Row(22));
            RecordGrab(21, "not-json");
            var rows = Subject.GetDownloads();
            rows.First().CoverageSource.Should().Be(QueueCoverageSource.MappedCandidates);
            rows.Last().CoverageSource.Should().Be(QueueCoverageSource.Unknown);
            rows.Last().Recordings.Should().BeEmpty();
            rows.Last().CanRemoveFromClient.Should().BeTrue();
        }

        [Test]
        public void pending_coverage_and_actions_are_scoped_to_the_persisted_release()
        {
            var first = Row(0, _books[0], _books[1]);
            first.PendingReleaseId = 11;
            first.PendingRecordedBookIds = new List<int> { 101, 102 };
            var second = Row(0, _books[2]);
            second.PendingReleaseId = 12;
            _pending.AddRange(new[] { first, second });
            var rows = Subject.GetDownloads();
            rows.Select(row => row.Id).Should().Equal("pending:11", "pending:12");
            rows.First().CoverageSource.Should().Be(QueueCoverageSource.PendingRelease);
            rows.First().CanGrab.Should().BeTrue();
        }

        [Test]
        public void revision_changes_with_recorded_coverage_but_not_progress()
        {
            _queue.Add(Row(21, _books[0]));
            RecordGrab(21, "[101]");
            var revision = Subject.GetDownloads().Single().Revision;
            _queue[0].Sizeleft = 1;
            Subject.GetDownloads().Single().Revision.Should().Be(revision);
            RecordGrab(21, "[102]");
            Subject.GetDownloads().Single().Revision.Should().NotBe(revision);
        }

        [Test]
        public void conflicting_identity_disables_actions_without_merging_book_ids()
        {
            _queue.Add(Row(21, _books[0]));
            _queue.Add(Row(21, _books[1]));
            var row = Subject.GetDownloads().Single();
            row.Kind.Should().Be(QueueDownloadKind.Unaddressable);
            row.Recordings.Should().BeEmpty();
            row.CanRemove.Should().BeFalse();
            row.CanGrab.Should().BeFalse();
        }

        private void RecordGrab(int clientId, string ids)
        {
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestGrab("same-id", clientId))
                .Returns(new DownloadHistory { Id = 500, AuthorId = _author.Id, SourceTitle = "Owned", Data = new Dictionary<string, string> { { "BookIds", ids } } });
        }

        private QueueRow Row(int clientId, params Book[] books)
        {
            return new QueueRow
            {
                Id = 123,
                DownloadClientId = clientId,
                DownloadId = "same-id",
                DownloadClientAvailable = true,
                Author = books.Length > 0 ? _author : null,
                Title = "Owned",
                Size = 500,
                Sizeleft = 250,
                Status = "Downloading",
                RemoteBook = new RemoteBook { Author = books.Length > 0 ? _author : null, Books = books.ToList() }
            };
        }
    }
}
