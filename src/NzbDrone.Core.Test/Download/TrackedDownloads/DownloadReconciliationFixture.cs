using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class DownloadReconciliationFixture : CoreTest<DownloadMonitoringService>
    {
        private TrackedDownloadService _tracker;
        private Mock<IDownloadClient> _client;
        private DownloadClientDefinition _definition;
        private TrackedDownload _cached;
        private List<TrackedDownload> _visible;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IHistoryService>().Setup(history => history.FindByDownloadId(It.IsAny<string>(), It.IsAny<int>())).Returns(new List<EntityHistory>());
            Mocker.SetConstant<IImportRetryService>(new ImportRetryService(TimeProvider.System));
            _tracker = Mocker.Resolve<TrackedDownloadService>();
            Mocker.SetConstant<ITrackedDownloadService>(_tracker);
            _definition = new DownloadClientDefinition { Id = 1, Name = "Owned client" };
            _client = new Mock<IDownloadClient>();
            _client.SetupGet(client => client.Definition).Returns(_definition);
            _client.Setup(client => client.GetItems()).Returns(Array.Empty<DownloadClientItem>());
            Mocker.GetMock<IDownloadClientFactory>().Setup(factory => factory.DownloadHandlingEnabled()).Returns(new List<IDownloadClient> { _client.Object });
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableCompletedDownloadHandling).Returns(true);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<TrackedDownloadRefreshedEvent>()))
                .Callback<TrackedDownloadRefreshedEvent>(message => _visible = message.TrackedDownloads);
            _cached = _tracker.TrackDownload(_definition, Item("old-item"));
        }

        private static DownloadClientItem Item(string downloadId)
        {
            return new DownloadClientItem
            {
                DownloadId = downloadId,
                Title = "Owned Author - Owned Book",
                Status = DownloadItemStatus.Downloading,
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1 }
            };
        }

        [Test]
        public void successful_empty_poll_removes_a_vanished_job()
        {
            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.GetTrackedDownloads().Should().BeEmpty();
            _visible.Should().BeEmpty();
            Mocker.GetMock<IDownloadClientStatusService>().Verify(status => status.RecordSuccess(1), Times.Once());
        }

        [Test]
        public void offline_client_preserves_visible_jobs_but_disables_processing()
        {
            _client.Setup(client => client.GetItems()).Throws(new InvalidOperationException("Owned offline fixture"));

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.GetTrackedDownloads().Should().Equal(_cached);
            _visible.Should().Equal(_cached);
            _cached.IsTrackable.Should().BeFalse();
            _cached.IsClientAvailable.Should().BeFalse();
            Mocker.GetMock<IDownloadClientStatusService>().Verify(status => status.RecordSuccess(1), Times.Never());
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Check(It.IsAny<TrackedDownload>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void lazy_enumeration_failure_cannot_be_mistaken_for_an_empty_or_partial_client()
        {
            _client.Setup(client => client.GetItems()).Returns(PartialResponse());

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.GetTrackedDownloads().Should().Equal(_cached);
            _visible.Should().Equal(_cached);
            _cached.IsTrackable.Should().BeFalse();
            Mocker.GetMock<IDownloadClientStatusService>().Verify(status => status.RecordFailure(1), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        private static IEnumerable<DownloadClientItem> PartialResponse()
        {
            yield return Item("new-item");
            throw new InvalidOperationException("Owned interrupted response");
        }

        [Test]
        public void per_item_failure_preserves_missing_jobs_and_marks_the_entire_client_stale()
        {
            _client.Setup(client => client.GetItems()).Returns(new[] { Item("first"), Item("broken") });
            Mocker.GetMock<ICompletedDownloadService>()
                .Setup(service => service.Check(It.Is<TrackedDownload>(download => download.DownloadItem.DownloadId == "broken")))
                .Throws(new InvalidOperationException("Owned processing fixture"));

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.Find("old-item", 1).Should().BeSameAs(_cached);
            _tracker.GetTrackedDownloads().Should().HaveCount(3).And.OnlyContain(download => !download.IsTrackable);
            _tracker.GetTrackedDownloads().Should().OnlyContain(download => !download.IsClientAvailable);
            _visible.Should().HaveCount(3);
            Mocker.GetMock<IDownloadClientStatusService>().Verify(status => status.RecordSuccess(1), Times.Never());
            Mocker.GetMock<IDownloadClientStatusService>().Verify(status => status.RecordFailure(1), Times.Once());
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void invalid_or_duplicate_response_cannot_evict_previous_jobs(bool duplicate)
        {
            var invalid = duplicate ? Item("new-item") : Item(null);
            _client.Setup(client => client.GetItems()).Returns(new[] { Item("new-item"), invalid });

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.GetTrackedDownloads().Should().Equal(_cached);
            _visible.Should().Equal(_cached);
            _cached.IsTrackable.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void missing_or_foreign_client_identity_cannot_authorize_reconciliation(int clientId)
        {
            var invalid = Item("new-item");
            invalid.DownloadClientInfo.Id = clientId;
            _client.Setup(client => client.GetItems()).Returns(new[] { invalid });

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.GetTrackedDownloads().Should().Equal(_cached);
            _cached.IsClientAvailable.Should().BeFalse();
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Check(It.IsAny<TrackedDownload>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_subsequent_success_reenables_processing_and_reconciles_missing_jobs()
        {
            _client.Setup(client => client.GetItems()).Throws(new InvalidOperationException("Owned outage"));
            Subject.Execute(new RefreshMonitoredDownloadsCommand());
            _client.Setup(client => client.GetItems()).Returns(new[] { Item("returned") });

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.Find("old-item", 1).Should().BeNull();
            _tracker.GetTrackedDownloads().Should().ContainSingle().Which.IsTrackable.Should().BeTrue();
            _tracker.GetTrackedDownloads().Should().ContainSingle().Which.IsClientAvailable.Should().BeTrue();
            _visible.Should().ContainSingle().Which.DownloadItem.DownloadId.Should().Be("returned");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void successful_poll_does_not_evict_another_clients_matching_download_id()
        {
            var other = _tracker.TrackDownload(new DownloadClientDefinition { Id = 2 }, new DownloadClientItem
            {
                DownloadId = "old-item", Title = "Owned Author - Owned Book", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 2 }
            });

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _tracker.GetTrackedDownloads().Should().Equal(other);
            _visible.Should().Equal(other);
            other.IsTrackable.Should().BeFalse();
        }

        [Test]
        public void successful_poll_marks_imported_jobs_fresh_without_adding_them_to_the_queue()
        {
            _cached.State = TrackedDownloadState.Imported;
            _client.Setup(client => client.GetItems()).Returns(new[] { Item("old-item") });

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _cached.IsClientAvailable.Should().BeTrue();
            _cached.IsTrackable.Should().BeFalse();
            _visible.Should().BeEmpty();
        }

        [Test]
        public void offline_poll_clears_imported_job_freshness_even_when_old_cleanup_flags_allow_removal()
        {
            _cached.State = TrackedDownloadState.Imported;
            _cached.IsClientAvailable = true;
            _cached.DownloadItem.Status = DownloadItemStatus.Completed;
            _cached.DownloadItem.CanBeRemoved = true;
            _client.Setup(client => client.GetItems()).Throws(new InvalidOperationException("Owned offline fixture"));

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            _cached.IsClientAvailable.Should().BeFalse();
            _tracker.GetTrackedDownloads().Should().Equal(_cached);
            _visible.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void client_updates_and_deletions_request_one_debounced_refresh()
        {
            Subject.Handle(new ProviderUpdatedEvent<IDownloadClient>(_definition));
            Subject.Handle(new ProviderUpdatedEvent<IDownloadClient>(_definition));
            Subject.Handle(new ProviderDeletedEvent<IDownloadClient>(_definition.Id));

            SpinWait.SpinUntil(
                () => Mocker.GetMock<IManageCommandQueue>().Invocations.Any(invocation =>
                    invocation.Arguments.FirstOrDefault() is RefreshMonitoredDownloadsCommand),
                TimeSpan.FromSeconds(10)).Should().BeTrue();

            Mocker.GetMock<IManageCommandQueue>().Verify(queue => queue.Push(It.IsAny<RefreshMonitoredDownloadsCommand>(), CommandPriority.High, CommandTrigger.Unspecified), Times.Once());
        }
    }
}
