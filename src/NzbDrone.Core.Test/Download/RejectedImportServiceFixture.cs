using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RejectedImportServiceFixture : CoreTest<RejectedImportService>
    {
        private TrackedDownload _tracked;
        private DownloadHistory _grab;
        private IndexerDefinition _indexer;
        private List<DownloadHistory> _downloadHistory;
        private List<EntityHistory> _history;

        [SetUp]
        public void Setup()
        {
            _tracked = new TrackedDownload
            {
                DownloadClient = 7, State = TrackedDownloadState.ImportBlocked,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned", Title = "Owned release", Status = DownloadItemStatus.Completed,
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7, Protocol = DownloadProtocol.Torrent }
                }
            };
            _grab = new DownloadHistory
            {
                Id = 1, DownloadId = "owned", DownloadClientId = 7, AuthorId = 22, SourceTitle = "Owned release",
                Date = DateTime.UtcNow.AddMinutes(-1), IndexerId = 12, Protocol = DownloadProtocol.Torrent,
                EventType = DownloadHistoryEventType.DownloadGrabbed,
                Data = new Dictionary<string, string> { ["bookIds"] = "[101,102]", ["releaseSource"] = "Rss" },
                Release = new TorrentInfo { IndexerId = 12, Title = "Owned release", Guid = "owned-guid", Indexer = "Owned indexer", DownloadProtocol = DownloadProtocol.Torrent, PublishDate = DateTime.UtcNow.AddDays(-1), Size = 30 }
            };
            _indexer = new IndexerDefinition { Id = 12, Name = "Owned indexer", EnableRss = true };
            _downloadHistory = new List<DownloadHistory> { _grab };
            _history = new[] { 101, 102 }.Select(book => new EntityHistory
            {
                DownloadId = "owned", BookId = book, AuthorId = 22, SourceTitle = "Owned release", Date = _grab.Date.AddSeconds(1),
                EventType = EntityHistoryEventType.Grabbed, Quality = new QualityModel(Quality.MP3),
                Data = new Dictionary<string, string> { ["Guid"] = "owned-guid", ["DownloadClientId"] = "7", ["TorrentInfoHash"] = "owned-hash" }
            }).ToList();
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.IsSafeRejectionSource(_tracked)).Returns(true);
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.FindByDownloadId("owned")).Returns(_downloadHistory);
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned", 7)).Returns(_history);
            Mocker.GetMock<IIndexerFactory>().Setup(factory => factory.Find(12)).Returns(_indexer);
            Mocker.GetMock<IRejectedImportRepository>().Setup(repository => repository.TryRecord(_grab, It.IsAny<DownloadFailedEvent>(), It.IsAny<RejectedImportAction>())).Returns(RejectedImportRecordResult.Applied);
        }

        private static Rejection ForCode(string code) => new Rejection("Owned reason", code: code);

        private bool Process(string code = RejectionCode.ExecutableFile) => Subject.TryProcess(_tracked, new List<ImportResult>(), ForCode(code));

        [TestCase(RejectionCode.ExecutableFile)]
        [TestCase(RejectionCode.DangerousFile)]
        [TestCase(RejectionCode.UserRejectedExtension)]
        public void default_should_warn_not_publish_failure(string code)
        {
            Process(code).Should().BeFalse();
            AssertNoAction();
            _tracked.StatusMessages.SelectMany(row => row.Rejections).Should().Contain(rejection => rejection.Code == RejectionCode.IndexerRejectionReview);
        }

        [TestCase(RejectedImportAction.Blocklist, true)]
        [TestCase(RejectedImportAction.BlocklistAndRetry, false)]
        public void explicit_policy_should_bind_current_grab_and_emit_only_after_commit(RejectedImportAction action, bool skipRetry)
        {
            _indexer.ExecutableFileAction = action;
            var events = new List<string>();
            Mocker.GetMock<IRejectedImportRepository>().Setup(repository => repository.TryRecord(_grab, It.IsAny<DownloadFailedEvent>(), action))
                .Callback(() => events.Add("commit")).Returns(RejectedImportRecordResult.Applied);
            Mocker.GetMock<IEventAggregator>().Setup(aggregator => aggregator.PublishEvent(It.IsAny<DownloadFailedEvent>()))
                .Callback<DownloadFailedEvent>(failure =>
                {
                    events.Add("event");
                    failure.AutomaticRejectionRecorded.Should().BeTrue();
                    failure.SkipRedownload.Should().Be(skipRetry);
                    failure.BookIds.Should().Equal(101, 102);
                    failure.TrackedDownload.Should().BeSameAs(_tracked);
                    failure.ImportRejectionCode.Should().Be(RejectionCode.ExecutableFile);
                });

            Process().Should().BeTrue();
            events.Should().Equal("commit", "event");
            _tracked.State.Should().Be(TrackedDownloadState.DownloadFailed);
            Process().Should().BeFalse();
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Once());
        }

        [TestCase(RejectionCode.UnknownAuthor)]
        [TestCase(RejectionCode.ArchiveNotExtracted)]
        [TestCase(RejectionCode.UnsupportedFileExtension)]
        [TestCase(RejectionCode.RecordingIdentityMismatch)]
        public void ordinary_and_ambiguous_rejections_should_never_fail_automatically(string code)
        {
            _indexer.ExecutableFileAction = RejectedImportAction.BlocklistAndRetry;
            Process(code).Should().BeFalse();
            AssertNoAction();
        }

        [Test]
        public void other_category_opt_in_should_not_affect_this_rejection()
        {
            _indexer.DangerousFileAction = RejectedImportAction.BlocklistAndRetry;
            Process().Should().BeFalse();
            AssertNoAction();
        }

        [TestCase("unknown")]
        [TestCase("disabled")]
        [TestCase("missing-grab")]
        [TestCase("other-client")]
        [TestCase("conflicting-recordings")]
        [TestCase("library-alias")]
        public void unknown_or_conflicting_context_should_remain_review(string change)
        {
            _indexer.ExecutableFileAction = RejectedImportAction.BlocklistAndRetry;
            switch (change)
            {
                case "unknown": Mocker.GetMock<IIndexerFactory>().Setup(factory => factory.Find(12)).Returns((IndexerDefinition)null); break;
                case "disabled": _indexer.EnableRss = false; break;
                case "missing-grab": _downloadHistory.Clear(); break;
                case "other-client": _grab.DownloadClientId = 8; break;
                case "conflicting-recordings": _history[0].BookId = 999; break;
                case "library-alias": Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.IsSafeRejectionSource(_tracked)).Returns(false); break;
            }

            Process().Should().BeFalse();
            AssertNoAction();
            _tracked.StatusMessages.SelectMany(row => row.Rejections).Should().Contain(rejection => rejection.Code == RejectionCode.IndexerRejectionReview);
        }

        [TestCase(DownloadHistoryEventType.FileImported)]
        [TestCase(DownloadHistoryEventType.DownloadImported)]
        [TestCase(DownloadHistoryEventType.DownloadSourceRetained)]
        public void previous_import_or_retained_sources_should_prevent_failure(DownloadHistoryEventType eventType)
        {
            _indexer.ExecutableFileAction = RejectedImportAction.BlocklistAndRetry;
            _downloadHistory.Add(new DownloadHistory { Id = 2, DownloadId = "owned", DownloadClientId = 7, Date = _grab.Date.AddSeconds(1), EventType = eventType });
            Process().Should().BeFalse();
            AssertNoAction();
        }

        [Test]
        public void partial_batch_results_should_prevent_failure()
        {
            _indexer.ExecutableFileAction = RejectedImportAction.BlocklistAndRetry;
            var imported = new ImportResult(new ImportDecision<LocalBook>(new LocalBook()));
            Subject.TryProcess(_tracked, new List<ImportResult> { imported }, ForCode(RejectionCode.ExecutableFile)).Should().BeFalse();
            AssertNoAction();
        }

        [Test]
        public void persisted_terminal_marker_should_not_blocklist_or_publish_again_on_restart()
        {
            _indexer.ExecutableFileAction = RejectedImportAction.BlocklistAndRetry;
            _downloadHistory.Add(new DownloadHistory { Id = 2, DownloadId = "owned", DownloadClientId = 7, Date = _grab.Date.AddSeconds(1), EventType = DownloadHistoryEventType.DownloadFailed });
            Process().Should().BeTrue();
            _tracked.State.Should().Be(TrackedDownloadState.DownloadFailed);
            Mocker.GetMock<IRejectedImportRepository>().Verify(repository => repository.TryRecord(It.IsAny<DownloadHistory>(), It.IsAny<DownloadFailedEvent>(), It.IsAny<RejectedImportAction>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }

        [Test]
        public void persistence_failure_should_remain_review_and_never_enqueue_retry()
        {
            _indexer.ExecutableFileAction = RejectedImportAction.BlocklistAndRetry;
            Mocker.GetMock<IRejectedImportRepository>().Setup(repository => repository.TryRecord(It.IsAny<DownloadHistory>(), It.IsAny<DownloadFailedEvent>(), It.IsAny<RejectedImportAction>())).Throws(new IOException("Owned fault"));
            Process().Should().BeFalse();
            _tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }

        [Test]
        public void retry_after_an_explicit_review_policy_change_should_use_live_settings()
        {
            Process().Should().BeFalse();
            _indexer.ExecutableFileAction = RejectedImportAction.Blocklist;
            Process().Should().BeTrue();
        }

        private void AssertNoAction()
        {
            _tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
            Mocker.GetMock<IRejectedImportRepository>().Verify(repository => repository.TryRecord(It.IsAny<DownloadHistory>(), It.IsAny<DownloadFailedEvent>(), It.IsAny<RejectedImportAction>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }
    }
}
