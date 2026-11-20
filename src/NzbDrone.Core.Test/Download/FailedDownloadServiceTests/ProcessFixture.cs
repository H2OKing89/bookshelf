using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.FailedDownloadServiceTests
{
    [TestFixture]
    public class ProcessFixture : CoreTest<FailedDownloadService>
    {
        private TrackedDownload _trackedDownload;
        private List<EntityHistory> _grabHistory;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.Title = "Drone.DroneTheBook.FLAC")
                                                    .Build();

            _grabHistory = Builder<EntityHistory>.CreateListOfSize(2).BuildList();

            var remoteBook = new RemoteBook
            {
                Author = new Author(),
                Books = new List<Book> { new Book { Id = 1 } }
            };

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadState.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteBook = remoteBook)
                    .Build();

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Find(_trackedDownload.DownloadItem.DownloadId, EntityHistoryEventType.Grabbed))
                  .Returns(_grabHistory);
        }

        private void GivenNoGrabbedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.Find(_trackedDownload.DownloadItem.DownloadId, EntityHistoryEventType.Grabbed))
                .Returns(new List<EntityHistory>());
        }

        [Test]
        public void should_not_fail_if_matching_history_is_not_found()
        {
            GivenNoGrabbedHistory();

            Subject.Check(_trackedDownload);

            AssertDownloadNotFailed();
        }

        [Test]
        public void should_warn_if_matching_history_is_not_found()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Failed;
            GivenNoGrabbedHistory();

            Subject.Check(_trackedDownload);

            _trackedDownload.StatusMessages.Should().NotBeEmpty();
        }

        [Test]
        public void should_not_warn_if_matching_history_is_not_found_and_not_failed()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Failed;
            GivenNoGrabbedHistory();

            Subject.Check(_trackedDownload);

            _trackedDownload.StatusMessages.Should().NotBeEmpty();
        }

        [TestCase(TrackedDownloadState.ImportBlocked, false)]
        [TestCase(TrackedDownloadState.ImportBlocked, true)]
        [TestCase(TrackedDownloadState.ImportFailed, false)]
        [TestCase(TrackedDownloadState.ImportPending, false)]
        public void an_import_block_should_not_hide_a_later_client_failure(TrackedDownloadState state, bool encrypted)
        {
            _trackedDownload.State = state;
            _trackedDownload.DownloadItem.Status = encrypted ? DownloadItemStatus.Completed : DownloadItemStatus.Failed;
            _trackedDownload.DownloadItem.IsEncrypted = encrypted;

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailedPending);
        }

        [TestCase(TrackedDownloadState.Imported)]
        [TestCase(TrackedDownloadState.Ignored)]
        public void completed_or_ignored_imports_keep_their_terminal_state(TrackedDownloadState state)
        {
            _trackedDownload.State = state;
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Failed;

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(state);
        }

        private void AssertDownloadNotFailed()
        {
            Mocker.GetMock<IEventAggregator>()
               .Verify(v => v.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());

            _trackedDownload.State.Should().NotBe(TrackedDownloadState.DownloadFailed);
        }

        private void AssertDownloadFailed()
        {
            Mocker.GetMock<IEventAggregator>()
            .Verify(v => v.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Once());

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);
        }
    }
}
