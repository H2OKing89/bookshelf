using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class ClientScopedTrackingFixture : CoreTest<TrackedDownloadService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IHistoryService>().Setup(history => history.FindByDownloadId(It.IsAny<string>(), It.IsAny<int>())).Returns(new List<EntityHistory>());
            Mocker.SetConstant<IImportRetryService>(new ImportRetryService(TimeProvider.System));
        }

        private TrackedDownload Track(int clientId, string downloadId = "same-hash")
        {
            return Subject.TrackDownload(new DownloadClientDefinition { Id = clientId }, new DownloadClientItem
            {
                DownloadId = downloadId,
                Title = "Owned Author - Owned Book",
                Category = "books",
                OutputPath = new OsPath(System.IO.Path.Combine(TempFolder, $"client-{clientId}")),
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = clientId }
            });
        }

        [Test]
        public void identical_download_ids_on_two_clients_have_independent_state_and_scoped_lookup()
        {
            var first = Track(1);
            first.State = TrackedDownloadState.Ignored;
            first.KeepSourceFiles = true;
            var second = Track(2);

            Subject.GetTrackedDownloads().Should().HaveCount(2);
            Subject.Find("same-hash", 1).Should().BeSameAs(first);
            Subject.Find("same-hash", 2).Should().BeSameAs(second);
            second.State.Should().Be(TrackedDownloadState.Downloading);
            second.KeepSourceFiles.Should().BeFalse();
            Action legacyLookup = () => Subject.Find("same-hash");
            legacyLookup.Should().Throw<AmbiguousTrackedDownloadException>();
        }

        [Test]
        public void successful_empty_poll_removes_only_that_clients_items()
        {
            Track(1);
            var second = Track(2);

            Subject.StopTrackingMissing(1, new List<string>());

            Subject.GetTrackedDownloads().Should().Equal(second);
            Subject.Find("same-hash").Should().BeSameAs(second);
        }

        [Test]
        public void present_download_and_same_id_on_another_client_survive_reconciliation()
        {
            var first = Track(1);
            Track(1, "vanished");
            var second = Track(2, "vanished");

            Subject.StopTrackingMissing(1, new List<string> { "same-hash" });

            Subject.GetTrackedDownloads().Should().BeEquivalentTo(new[] { first, second });
        }

        [Test]
        public void ambiguous_legacy_batch_is_rejected_before_any_cache_mutation()
        {
            Track(1, "unique");
            Track(1);
            Track(2);

            Action stop = () => Subject.StopTracking(new List<string> { "unique", "same-hash" });

            stop.Should().Throw<AmbiguousTrackedDownloadException>();
            Subject.GetTrackedDownloads().Should().HaveCount(3);
        }

        [Test]
        public void freshness_is_scoped_even_when_download_ids_match()
        {
            var first = Track(1);
            var second = Track(2);

            Subject.UpdateTrackable(new List<TrackedDownload> { first });

            first.IsTrackable.Should().BeTrue();
            second.IsTrackable.Should().BeFalse();
            Subject.GetTrackedDownloads().Should().HaveCount(2);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void changed_category_or_path_rechecks_blocked_context_without_reusing_mapping(bool category)
        {
            var first = Track(1);
            first.ImportItem = first.DownloadItem.Clone();
            first.ImportReceipt = new DownloadImportReceipt();
            first.HasNotifiedManualInteractionRequired = true;
            Mocker.Resolve<IImportRetryService>().Block(first);
            var updated = first.DownloadItem.Clone();
            if (category)
            {
                updated.Category = "new-books";
            }
            else
            {
                updated.OutputPath = new OsPath(System.IO.Path.Combine(TempFolder, "changed-source"));
            }

            var refreshed = Subject.TrackDownload(new DownloadClientDefinition { Id = 1 }, updated);

            refreshed.Should().BeSameAs(first);
            refreshed.State.Should().Be(TrackedDownloadState.Downloading);
            refreshed.ImportItem.Should().BeNull();
            refreshed.ImportReceipt.Should().BeNull();
            refreshed.ImportRetryAfterUtc.Should().BeNull();
            refreshed.HasNotifiedManualInteractionRequired.Should().BeTrue();
        }

        [Test]
        public void downloading_refresh_preserves_intervention_and_copy_intent()
        {
            var first = Track(1);
            first.HasNotifiedManualInteractionRequired = true;
            first.KeepSourceFiles = true;

            var refreshed = Track(1);

            refreshed.HasNotifiedManualInteractionRequired.Should().BeTrue();
            refreshed.KeepSourceFiles.Should().BeTrue();
            Subject.GetTrackedDownloads().Should().HaveCount(1);
        }

        [Test]
        public void deleting_a_client_retires_only_its_cache_entries()
        {
            Track(1);
            var retained = Track(2);

            Subject.Handle(new ProviderDeletedEvent<IDownloadClient>(1));

            Subject.GetTrackedDownloads().Should().Equal(retained);
        }

        [TestCase(TrackedDownloadState.Imported)]
        [TestCase(TrackedDownloadState.Ignored)]
        [TestCase(TrackedDownloadState.ImportBlocked)]
        public void a_new_grab_replaces_only_its_clients_previous_state(TrackedDownloadState previous)
        {
            var first = Track(1);
            first.State = previous;
            first.HasNotifiedManualInteractionRequired = true;
            var other = Track(2);

            Subject.Handle(new BookGrabbedEvent(null) { DownloadId = "same-hash", DownloadClientId = 1 });
            var refreshed = Track(1);

            refreshed.State.Should().Be(TrackedDownloadState.Downloading);
            refreshed.HasNotifiedManualInteractionRequired.Should().BeFalse();
            Subject.Find("same-hash", 2).Should().BeSameAs(other);
        }

        [Test]
        public void state_reconstruction_uses_only_the_polled_clients_history()
        {
            Mocker.GetMock<IDownloadHistoryService>().Setup(history => history.GetLatestDownloadHistoryItem("same-hash", 1))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadIgnored });

            var first = Track(1);
            var second = Track(2);

            first.State.Should().Be(TrackedDownloadState.Ignored);
            second.State.Should().Be(TrackedDownloadState.Downloading);
            Mocker.GetMock<IDownloadHistoryService>().Verify(history => history.GetLatestDownloadHistoryItem(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IHistoryService>().Verify(history => history.FindByDownloadId(It.IsAny<string>()), Times.Never());
        }
    }
}
