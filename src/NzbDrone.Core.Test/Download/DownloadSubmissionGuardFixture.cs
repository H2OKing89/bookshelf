using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadSubmissionGuardFixture : CoreTest<DownloadSubmissionGuard>
    {
        private List<DownloadHistory> _rows;
        private RemoteBook _book;
        private Mock<IDownloadClient> _client;

        [SetUp]
        public void SetUp()
        {
            _rows = new List<DownloadHistory>();
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.GetSubmissionAttempts()).Returns(() => _rows.ToList());
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>()))
                .Callback<DownloadHistory>(row =>
                {
                    row.Id = _rows.Count + 1;
                    _rows.Add(row);
                }).Returns<DownloadHistory>(row => row);
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Update(It.IsAny<DownloadHistory>())).Returns<DownloadHistory>(row => row);
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Delete(It.IsAny<DownloadHistory>()))
                .Callback<DownloadHistory>(row => _rows.Remove(row));
            _book = new RemoteBook
            {
                Author = new Author { Id = 1 },
                Books = new List<Book> { new Book { Id = 12 }, new Book { Id = 99 } },
                Release = new ReleaseInfo { Title = "A Book" }
            };
            _client = new Mock<IDownloadClient>();
            _client.SetupGet(client => client.Definition).Returns(new DownloadClientDefinition { Id = 8, Name = "Owned test client" });
        }

        [Test]
        public void should_persist_neutral_marker_before_allowing_submission()
        {
            var id = Subject.Begin(_book, _client.Object);

            _rows.Should().ContainSingle();
            _rows[0].EventType.Should().Be(DownloadHistoryEventType.DownloadSubmissionPending);
            _rows[0].DownloadClientId.Should().Be(8);
            _rows[0].Data["AttemptId"].Should().Be(id);
            Subject.GetPending().Single().BookIds.Should().Equal(12, 99);
            Subject.GetPending().Single().CanAcknowledge.Should().BeFalse();
            Subject.Acknowledge(id, 8).Should().BeFalse();
        }

        [Test]
        public void active_marker_should_block_an_overlapping_submission()
        {
            Subject.Begin(_book, _client.Object);
            _book.Books = new List<Book> { new Book { Id = 99 }, new Book { Id = 100 } };

            Assert.Throws<DownloadClientSubmissionUncertainException>(() => Subject.Begin(_book, _client.Object));
            _rows.Should().ContainSingle();
        }

        [Test]
        public void unknown_outcome_should_survive_service_restart_and_later_batch()
        {
            var id = Subject.Begin(_book, _client.Object);
            Subject.MarkUncertain(id, new string('A', 40));
            var restarted = new DownloadSubmissionGuard(Mocker.GetMock<IDownloadHistoryRepository>().Object);

            Assert.Throws<DownloadClientSubmissionUncertainException>(() => restarted.EnsureCanSubmit(_book));
            restarted.GetPending().Single().ExpectedDownloadId.Should().Be(new string('A', 40));
            restarted.GetPending().Single().CanAcknowledge.Should().BeTrue();
        }

        [Test]
        public void crash_after_begin_should_require_review_on_restart()
        {
            Subject.Begin(_book, _client.Object);
            var restarted = new DownloadSubmissionGuard(Mocker.GetMock<IDownloadHistoryRepository>().Object);

            Assert.Throws<DownloadClientSubmissionUncertainException>(() => restarted.EnsureCanSubmit(_book));
            restarted.GetPending().Single().CanAcknowledge.Should().BeTrue();
        }

        [Test]
        public void acknowledgement_should_require_matching_attempt_and_client_and_preserve_audit()
        {
            var id = Subject.Begin(_book, _client.Object);
            Subject.MarkUncertain(id, null);

            Subject.Acknowledge("another-attempt", 8).Should().BeFalse();
            Subject.Acknowledge(id, 9).Should().BeFalse();
            Subject.Acknowledge(id, 8).Should().BeTrue();
            Subject.GetPending().Should().BeEmpty();
            _rows.Should().ContainSingle();
            _rows[0].Data.Should().ContainKey("AcknowledgedUtc");
            Assert.DoesNotThrow(() => Subject.EnsureCanSubmit(_book));
            Subject.Acknowledge(id, 8).Should().BeFalse();
        }

        [Test]
        public void confirmed_success_or_proven_release_failure_should_clear_marker()
        {
            var id = Subject.Begin(_book, _client.Object);
            Subject.Complete(id);

            Subject.GetPending().Should().BeEmpty();
            Assert.DoesNotThrow(() => Subject.EnsureCanSubmit(_book));
        }

        [Test]
        public void another_book_should_remain_eligible()
        {
            Subject.Begin(_book, _client.Object);
            _book.Books = new List<Book> { new Book { Id = 100 } };

            Assert.DoesNotThrow(() => Subject.EnsureCanSubmit(_book));
        }

        [TestCase(null)]
        [TestCase("not-json")]
        [TestCase("[]")]
        public void damaged_book_ids_should_block_the_author_until_review(string bookIds)
        {
            Subject.Begin(_book, _client.Object);
            _rows[0].Data["BookIds"] = bookIds;
            _book.Books = new List<Book> { new Book { Id = 100 } };

            Assert.Throws<DownloadClientSubmissionUncertainException>(() => Subject.EnsureCanSubmit(_book));
        }

        [Test]
        public void failed_marker_persistence_should_prevent_submission_authorization()
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>())).Throws(new InvalidOperationException("write failed"));

            Assert.Throws<InvalidOperationException>(() => Subject.Begin(_book, _client.Object));
        }

        [Test]
        public void serialized_markers_should_preserve_holds_and_client_scoped_acknowledgement()
        {
            var id = Subject.Begin(_book, _client.Object);
            Subject.MarkUncertain(id, new string('A', 40));
            _rows[0] = STJson.Deserialize<DownloadHistory>(STJson.ToJson(_rows[0]));
            var restarted = new DownloadSubmissionGuard(Mocker.GetMock<IDownloadHistoryRepository>().Object);

            restarted.GetPending().Single().AttemptId.Should().Be(id);
            restarted.GetPending().Single().BookIds.Should().Equal(12, 99);
            Assert.Throws<DownloadClientSubmissionUncertainException>(() => restarted.EnsureCanSubmit(_book));
            restarted.Acknowledge(id, 8).Should().BeTrue();
            _rows[0] = STJson.Deserialize<DownloadHistory>(STJson.ToJson(_rows[0]));
            restarted.GetPending().Should().BeEmpty();
        }

        [Test]
        public void damaged_data_should_retain_a_reviewable_stable_attempt_identity()
        {
            var id = Subject.Begin(_book, _client.Object);
            _rows[0].Data = null;
            var restarted = new DownloadSubmissionGuard(Mocker.GetMock<IDownloadHistoryRepository>().Object);

            restarted.GetPending().Single().AttemptId.Should().Be(id);
            Assert.Throws<DownloadClientSubmissionUncertainException>(() => restarted.EnsureCanSubmit(_book));
            restarted.Acknowledge(id, 8).Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void unconfirmed_persistence_should_never_authorize_submission(bool returnsNull)
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>()))
                .Returns(returnsNull ? null : new DownloadHistory());

            Assert.Throws<InvalidOperationException>(() => Subject.Begin(_book, _client.Object));
        }

        [Test]
        public void missing_book_identity_should_not_authorize_submission()
        {
            _book.Books = new List<Book> { new Book { Id = 0 } };

            Assert.Throws<ArgumentException>(() => Subject.Begin(_book, _client.Object));
            Mocker.GetMock<IDownloadHistoryRepository>().Verify(repository => repository.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }

        [TestCase("broken")]
        [TestCase("2020-01-01T00:00:00.0000000Z")]
        [TestCase("2030-01-01T00:00:00.0000000")]
        public void malformed_or_invalid_acknowledgement_should_keep_the_hold(string acknowledged)
        {
            Subject.Begin(_book, _client.Object);
            _rows[0].Data["AcknowledgedUtc"] = acknowledged;

            var restarted = new DownloadSubmissionGuard(Mocker.GetMock<IDownloadHistoryRepository>().Object);
            Assert.Throws<DownloadClientSubmissionUncertainException>(() => restarted.EnsureCanSubmit(_book));
        }

        [Test]
        public void conflicting_cased_acknowledgements_should_keep_the_hold()
        {
            Subject.Begin(_book, _client.Object);
            _rows[0].Data["AcknowledgedUtc"] = DateTime.UtcNow.ToString("O");
            _rows[0].Data["acknowledgedUtc"] = string.Empty;

            var restarted = new DownloadSubmissionGuard(Mocker.GetMock<IDownloadHistoryRepository>().Object);
            Assert.Throws<DownloadClientSubmissionUncertainException>(() => restarted.EnsureCanSubmit(_book));
        }
    }
}
