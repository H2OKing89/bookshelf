using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.CompletedDownloadServiceTests
{
    [TestFixture]
    public class ImportFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _tracked;
        private List<ImportResult> _results;
        private DownloadImportReceipt _receipt;

        [SetUp]
        public void Setup()
        {
            var output = Path.Combine(TempFolder, "download");
            var item = new DownloadClientItem
            {
                DownloadId = "owned-download", Status = DownloadItemStatus.Completed,
                OutputPath = new OsPath(output), Title = "Owned recording", CanMoveFiles = true
            };
            _tracked = new TrackedDownload
            {
                DownloadClient = 3, DownloadItem = item, ImportItem = item,
                RemoteBook = new RemoteBook { Author = new Author { Id = 7 }, Books = new List<Book> { new Book { Id = 11 } } }
            };
            _results = new List<ImportResult> { Result() };
            _receipt = new DownloadImportReceipt
            {
                DownloadId = item.DownloadId, DownloadClientId = 3, AuthorId = 7,
                OutputPath = output, BookIds = new List<int> { 11 }
            };
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns(item);
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(service => service.ProcessPath(output, ImportMode.Auto, It.IsAny<Author>(), item))
                .Returns(() => _results);
            Mocker.GetMock<IDownloadImportSourceService>()
                .Setup(service => service.Retire(_tracked, It.IsAny<DownloadImportReceipt>())).Returns(true);
        }

        private ImportResult Result(string error = null, bool rejected = false)
        {
            var local = new LocalBook
            {
                Path = Path.Combine(TempFolder, "download", "recording.m4b"),
                Book = new Book { Id = 11 }, Author = new Author { Id = 7 },
                Edition = new Edition { Id = 17, BookId = 11 }, Part = 1, PartCount = 1
            };
            var decision = rejected ? new ImportDecision<LocalBook>(local, new Rejection("Rejected recording")) : new ImportDecision<LocalBook>(local);
            return error == null ? new ImportResult(decision) : new ImportResult(decision, error);
        }

        private void GivenReceipt()
        {
            Mocker.GetMock<IDownloadImportCompletionService>()
                .Setup(service => service.VerifyAndRecord(_tracked, It.IsAny<List<ImportResult>>())).Returns(_receipt);
        }

        private void AssertNoCompletion()
        {
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
            _tracked.State.Should().NotBe(TrackedDownloadState.Imported);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void rejected_or_skipped_files_should_remain_failed_without_cleanup(bool rejected)
        {
            _results = new List<ImportResult> { Result("Not imported", rejected) };

            Subject.Import(_tracked);

            _tracked.State.Should().Be(TrackedDownloadState.ImportFailed);
            AssertNoCompletion();
            Mocker.GetMock<IDownloadImportSourceService>().Verify(service => service.Retire(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookImportIncompleteEvent>()), Times.Once());
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void successful_result_rows_alone_should_never_authorize_download_cleanup(int rows)
        {
            _results.Clear();
            for (var index = 0; index < rows; index++)
            {
                _results.Add(Result());
            }

            Subject.Import(_tracked);

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            AssertNoCompletion();
            Mocker.GetMock<IDownloadImportSourceService>().Verify(service => service.Retire(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()), Times.Never());
        }

        [Test]
        public void empty_scan_without_proof_should_stay_pending()
        {
            _results.Clear();

            Subject.Import(_tracked);

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            AssertNoCompletion();
        }

        [Test]
        public void verified_previous_commits_can_complete_when_the_importer_returns_no_new_files()
        {
            _results.Clear();
            GivenReceipt();

            Subject.Import(_tracked);

            _tracked.State.Should().Be(TrackedDownloadState.Imported);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
        }

        [Test]
        public void completion_should_persist_evidence_before_retiring_source_and_publishing()
        {
            var persisted = false;
            var retired = false;
            Mocker.GetMock<IDownloadImportCompletionService>()
                .Setup(service => service.VerifyAndRecord(_tracked, _results))
                .Callback(() => persisted = true).Returns(_receipt);
            Mocker.GetMock<IDownloadImportSourceService>()
                .Setup(service => service.Retire(_tracked, _receipt))
                .Callback(() =>
                {
                    persisted.Should().BeTrue();
                    retired = true;
                }).Returns(true);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()))
                .Callback<DownloadCompletedEvent>(message =>
                {
                    persisted.Should().BeTrue();
                    retired.Should().BeTrue();
                    message.AuthorId.Should().Be(7);
                    message.TrackedDownload.ImportReceipt.Should().BeSameAs(_receipt);
                    message.TrackedDownload.State.Should().Be(TrackedDownloadState.Imported);
                });

            Subject.Import(_tracked);

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
        }

        [Test]
        public void receipt_write_failure_should_preserve_source_and_restore_a_retryable_state()
        {
            Mocker.GetMock<IDownloadImportCompletionService>()
                .Setup(service => service.VerifyAndRecord(_tracked, _results)).Throws(new InvalidOperationException("Owned write failure"));

            Assert.Throws<InvalidOperationException>(() => Subject.Import(_tracked));

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            AssertNoCompletion();
            Mocker.GetMock<IDownloadImportSourceService>().Verify(service => service.Retire(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()), Times.Never());
        }

        [Test]
        public void importer_exception_should_not_leave_the_download_stuck_importing()
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(service => service.ProcessPath(It.IsAny<string>(), ImportMode.Auto, It.IsAny<Author>(), _tracked.DownloadItem))
                .Throws(new IOException("Owned import failure"));

            Assert.Throws<IOException>(() => Subject.Import(_tracked));

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            AssertNoCompletion();
            Mocker.GetMock<IDownloadImportCompletionService>().Verify(service => service.VerifyAndRecord(It.IsAny<TrackedDownload>(), It.IsAny<List<ImportResult>>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void unsafe_or_failed_source_retirement_should_not_publish_completion(bool throws)
        {
            GivenReceipt();
            var setup = Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Retire(_tracked, _receipt));
            if (throws)
            {
                setup.Throws(new IOException("Owned source failure"));
                Assert.Throws<IOException>(() => Subject.Import(_tracked));
            }
            else
            {
                setup.Returns(false);
                Subject.Import(_tracked);
            }

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            AssertNoCompletion();
        }

        [Test]
        public void completion_with_bound_history_scope_should_not_require_a_remote_author_mapping()
        {
            _tracked.RemoteBook = null;
            GivenReceipt();

            Subject.Import(_tracked);

            _tracked.State.Should().Be(TrackedDownloadState.Imported);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.Is<DownloadCompletedEvent>(message => message.AuthorId == 7)), Times.Once());
        }

        [Test]
        public void repeating_a_verified_transition_should_not_publish_another_completion()
        {
            GivenReceipt();

            Subject.VerifyImport(_tracked, _results).Should().BeTrue();
            Subject.VerifyImport(_tracked, _results).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
        }

        [Test]
        public void verified_completion_should_clear_the_previous_review_warning()
        {
            _tracked.Warn("Previous attempt needs review");
            GivenReceipt();

            Subject.VerifyImport(_tracked, _results).Should().BeTrue();

            _tracked.Status.Should().Be(TrackedDownloadStatus.Ok);
            _tracked.StatusMessages.Should().BeEmpty();
        }

        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        [TestCase(DownloadItemStatus.Warning)]
        [TestCase(DownloadItemStatus.Failed)]
        public void an_unfinished_client_item_cannot_use_old_completion_to_retire_sources(DownloadItemStatus status)
        {
            GivenReceipt();
            _tracked.DownloadItem.Status = status;

            Subject.VerifyImport(_tracked, _results).Should().BeFalse();
            Subject.Import(_tracked);

            _tracked.State.Should().Be(TrackedDownloadState.Downloading);
            AssertNoCompletion();
            Mocker.GetMock<IDownloadImportSourceService>().Verify(service => service.Retire(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()), Times.Never());
            Mocker.GetMock<IDownloadedBooksImportService>().Verify(service => service.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }
    }
}
