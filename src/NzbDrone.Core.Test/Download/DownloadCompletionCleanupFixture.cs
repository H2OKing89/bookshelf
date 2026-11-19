using System;
using System.Collections.Generic;
using FluentAssertions;
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
    public class DownloadCompletionCleanupFixture : CoreTest<DownloadEventHub>
    {
        private TrackedDownload _tracked;
        private DownloadImportReceipt _receipt;
        private Mock<IDownloadClient> _client;
        private DownloadClientDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _tracked = new TrackedDownload
            {
                DownloadClient = 3, State = TrackedDownloadState.Imported,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-download", Title = "Owned recording", Status = DownloadItemStatus.Completed,
                    CanBeRemoved = true, CanMoveFiles = true, DownloadClientInfo = new DownloadClientItemClientInfo { Id = 3, Name = "Owned client" }
                }
            };
            _receipt = new DownloadImportReceipt { DownloadId = "owned-download", DownloadClientId = 3, AuthorId = 7 };
            _definition = new DownloadClientDefinition { Id = 3, Name = "Owned client", RemoveCompletedDownloads = true, RemoveFailedDownloads = true };
            _client = new Mock<IDownloadClient>();
            _client.SetupGet(client => client.Definition).Returns(_definition);
            Mocker.GetMock<IProvideDownloadClient>().Setup(provider => provider.Get(3)).Returns(_client.Object);
            Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Retire(_tracked, _receipt)).Returns(true);
        }

        private void GivenReceipt()
        {
            Mocker.GetMock<IDownloadImportCompletionService>()
                .Setup(service => service.ReadReceipt(_tracked, It.IsAny<DownloadHistory>())).Returns(_receipt);
        }

        private void Notify(bool removalOnly)
        {
            if (removalOnly)
            {
                Subject.Handle(new DownloadCanBeRemovedEvent(_tracked));
            }
            else
            {
                Subject.Handle(new DownloadCompletedEvent(_tracked, 7));
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void cached_imported_state_without_valid_receipt_never_marks_or_removes_client_data(bool removalOnly, bool throws)
        {
            if (throws)
            {
                Mocker.GetMock<IDownloadImportCompletionService>()
                    .Setup(service => service.ReadReceipt(_tracked, It.IsAny<DownloadHistory>())).Throws(new InvalidOperationException("Owned evidence failure"));
            }

            Notify(removalOnly);

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            _client.Verify(client => client.MarkItemAsImported(It.IsAny<DownloadClientItem>()), Times.Never());
            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IProvideDownloadClient>().Verify(provider => provider.Get(It.IsAny<int>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void verified_completion_can_remove_the_client_item(bool removalOnly)
        {
            GivenReceipt();

            Notify(removalOnly);

            _client.Verify(client => client.RemoveItem(_tracked.DownloadItem, true), Times.Once());
            _tracked.DownloadItem.Removed.Should().BeTrue();
            _tracked.ImportReceipt.Should().BeSameAs(_receipt);
            Mocker.GetMock<IDownloadImportSourceService>().Verify(service => service.Retire(_tracked, _receipt), Times.Once());
            _client.Verify(client => client.MarkItemAsImported(_tracked.DownloadItem), removalOnly ? Times.Never() : Times.Once());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void persisted_receipt_after_restart_cannot_authorize_cleanup_when_source_retirement_fails(bool removalOnly, bool throws)
        {
            GivenReceipt();
            _tracked.ImportReceipt = _receipt;
            var retirement = Mocker.GetMock<IDownloadImportSourceService>().Setup(service => service.Retire(_tracked, _receipt));
            if (throws)
            {
                retirement.Throws(new InvalidOperationException("Owned source retirement failure"));
            }
            else
            {
                retirement.Returns(false);
            }

            Notify(removalOnly);

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            _client.Verify(client => client.MarkItemAsImported(It.IsAny<DownloadClientItem>()), Times.Never());
            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IProvideDownloadClient>().Verify(provider => provider.Get(It.IsAny<int>()), Times.Never());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void copy_mode_keeps_client_data_when_removing_a_completed_task(bool removalOnly, bool storedFlag)
        {
            GivenReceipt();
            _receipt.KeepSourceFiles = storedFlag;
            _tracked.KeepSourceFiles = !storedFlag;

            Notify(removalOnly);

            _client.Verify(client => client.RemoveItem(_tracked.DownloadItem, false), Times.Once());
            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), true), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void a_removable_task_without_file_move_permission_should_keep_its_client_data(bool removalOnly)
        {
            GivenReceipt();
            _tracked.DownloadItem.CanMoveFiles = false;

            Notify(removalOnly);

            _tracked.DownloadItem.Removed.Should().BeTrue();
            _client.Verify(client => client.RemoveItem(_tracked.DownloadItem, false), Times.Once());
            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), true), Times.Never());
            _client.Verify(client => client.MarkItemAsImported(_tracked.DownloadItem), removalOnly ? Times.Never() : Times.Once());
        }

        [Test]
        public void client_settings_still_control_completed_task_removal()
        {
            GivenReceipt();
            _definition.RemoveCompletedDownloads = false;

            Notify(false);

            _client.Verify(client => client.MarkItemAsImported(_tracked.DownloadItem), Times.Once());
            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void completed_event_replay_after_successful_removal_is_a_noop()
        {
            GivenReceipt();

            Notify(false);
            Notify(false);

            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Once());
            _client.Verify(client => client.MarkItemAsImported(It.IsAny<DownloadClientItem>()), Times.Once());
        }

        [TestCase(false, DownloadItemStatus.Downloading)]
        [TestCase(true, DownloadItemStatus.Downloading)]
        [TestCase(false, DownloadItemStatus.Queued)]
        [TestCase(true, DownloadItemStatus.Queued)]
        [TestCase(false, DownloadItemStatus.Paused)]
        [TestCase(true, DownloadItemStatus.Paused)]
        [TestCase(false, DownloadItemStatus.Failed)]
        [TestCase(true, DownloadItemStatus.Failed)]
        [TestCase(false, DownloadItemStatus.Warning)]
        [TestCase(true, DownloadItemStatus.Warning)]
        public void an_incomplete_client_item_cannot_be_marked_or_removed_using_a_previous_receipt(bool removalOnly, DownloadItemStatus status)
        {
            GivenReceipt();
            _tracked.DownloadItem.Status = status;

            Notify(removalOnly);

            _tracked.State.Should().Be(TrackedDownloadState.Downloading);
            _client.Verify(client => client.MarkItemAsImported(It.IsAny<DownloadClientItem>()), Times.Never());
            _client.Verify(client => client.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDownloadImportSourceService>().Verify(service => service.Retire(It.IsAny<TrackedDownload>(), It.IsAny<DownloadImportReceipt>()), Times.Never());
        }

        [Test]
        public void failed_download_removal_retains_its_existing_independent_policy()
        {
            Subject.Handle(new DownloadFailedEvent { TrackedDownload = _tracked });

            _client.Verify(client => client.RemoveItem(_tracked.DownloadItem, true), Times.Once());
            Mocker.GetMock<IDownloadImportCompletionService>().Verify(service => service.ReadReceipt(It.IsAny<TrackedDownload>(), It.IsAny<DownloadHistory>()), Times.Never());
        }
    }

    [TestFixture]
    public class DownloadProcessingCompletionFixture : CoreTest<DownloadProcessingService>
    {
        private TrackedDownload _tracked;
        private DownloadImportReceipt _receipt;

        [SetUp]
        public void Setup()
        {
            _tracked = new TrackedDownload
            {
                IsTrackable = true, State = TrackedDownloadState.Imported,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-download", CanBeRemoved = true, Title = "Owned recording", Status = DownloadItemStatus.Completed }
            };
            _receipt = new DownloadImportReceipt { DownloadId = "owned-download" };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.GetTrackedDownloads()).Returns(new List<TrackedDownload> { _tracked });
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableCompletedDownloadHandling).Returns(true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void periodic_cleanup_must_not_trust_a_cached_imported_state(bool throws)
        {
            if (throws)
            {
                Mocker.GetMock<IDownloadImportCompletionService>()
                    .Setup(service => service.ReadReceipt(_tracked, It.IsAny<DownloadHistory>())).Throws(new InvalidOperationException("Owned history failure"));
            }

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            _tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), Times.Never());
        }

        [Test]
        public void periodic_cleanup_may_request_removal_after_receipt_revalidation_without_republishing_completion()
        {
            Mocker.GetMock<IDownloadImportCompletionService>()
                .Setup(service => service.ReadReceipt(_tracked, It.IsAny<DownloadHistory>())).Returns(_receipt);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            _tracked.ImportReceipt.Should().BeSameAs(_receipt);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
        }

        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        [TestCase(DownloadItemStatus.Warning)]
        [TestCase(DownloadItemStatus.Failed)]
        public void periodic_cleanup_should_wait_for_the_client_to_confirm_completion(DownloadItemStatus status)
        {
            _tracked.DownloadItem.Status = status;
            Mocker.GetMock<IDownloadImportCompletionService>()
                .Setup(service => service.ReadReceipt(_tracked, It.IsAny<DownloadHistory>())).Returns(_receipt);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            _tracked.State.Should().Be(TrackedDownloadState.Downloading);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), Times.Never());
        }
    }
}
