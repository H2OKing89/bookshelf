using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ImportRetryServiceFixture
    {
        private RetryTestClock _clock;
        private ImportRetryService _subject;
        private TrackedDownload _tracked;

        [SetUp]
        public void Setup()
        {
            _clock = new RetryTestClock();
            _subject = new ImportRetryService(_clock);
            _tracked = new TrackedDownload
            {
                State = TrackedDownloadState.ImportPending,
                DownloadItem = new DownloadClientItem { Status = DownloadItemStatus.Completed, Title = "Owned recording" }
            };
        }

        [Test]
        public void the_pre_http_container_can_use_the_system_clock_without_a_registered_provider()
        {
            var subject = new ImportRetryService();
            var earliest = DateTime.UtcNow.AddSeconds(30);

            subject.Block(_tracked);

            _tracked.ImportRetryAfterUtc.Should().BeOnOrAfter(earliest);
            _tracked.ImportRetryAfterUtc.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(30));
            _tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        [Test]
        public void repeated_import_failures_back_off_without_unbounded_delay()
        {
            foreach (var expected in new[] { 30, 60, 120, 240, 300, 300, 300 })
            {
                _tracked.State = TrackedDownloadState.Importing;
                _subject.Block(_tracked);

                _tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
                _tracked.ImportRetryAfterUtc.Should().Be(_clock.GetUtcNow().UtcDateTime.AddSeconds(expected));
                _subject.CanRetry(_tracked).Should().BeFalse();
                _clock.Advance(expected);
                _subject.CanRetry(_tracked).Should().BeTrue();
            }

            _tracked.ImportRetryCount.Should().Be(5);
        }

        [Test]
        public void observing_an_existing_block_must_not_push_its_deadline_out()
        {
            _subject.Block(_tracked);
            var deadline = _tracked.ImportRetryAfterUtc;
            _clock.Advance(10);

            _subject.Block(_tracked);

            _tracked.ImportRetryCount.Should().Be(1);
            _tracked.ImportRetryAfterUtc.Should().Be(deadline);
        }

        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        [TestCase(DownloadItemStatus.Warning)]
        [TestCase(DownloadItemStatus.Failed)]
        public void an_unfinished_client_item_cannot_be_retried(DownloadItemStatus status)
        {
            _tracked.State = TrackedDownloadState.ImportBlocked;
            _tracked.DownloadItem.Status = status;

            _subject.CanRetry(_tracked).Should().BeFalse();
        }

        [TestCase(TrackedDownloadState.Downloading)]
        [TestCase(TrackedDownloadState.Imported)]
        [TestCase(TrackedDownloadState.Ignored)]
        [TestCase(TrackedDownloadState.DownloadFailed)]
        public void a_nonblocked_state_cannot_be_retried(TrackedDownloadState state)
        {
            _tracked.State = state;

            _subject.CanRetry(_tracked).Should().BeFalse();
        }

        [Test]
        public void removed_client_items_cannot_be_retried()
        {
            _tracked.State = TrackedDownloadState.ImportBlocked;
            _tracked.DownloadItem.Removed = true;

            _subject.CanRetry(_tracked).Should().BeFalse();
        }

        [TestCase(TrackedDownloadState.ImportBlocked)]
        [TestCase(TrackedDownloadState.ImportFailed)]
        public void restored_blocks_or_legacy_failures_can_be_rechecked(TrackedDownloadState state)
        {
            _tracked.State = state;

            _subject.CanRetry(_tracked).Should().BeTrue();
        }

        [Test]
        public void a_relevant_change_resets_backoff_without_hiding_the_warning()
        {
            _subject.Block(_tracked);
            _tracked.Warn("Path needs review");

            _subject.Reset(_tracked);

            _tracked.ImportRetryCount.Should().Be(0);
            _tracked.ImportRetryAfterUtc.Should().BeNull();
            _tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
            _tracked.StatusMessages.Should().ContainSingle();
            _subject.CanRetry(_tracked).Should().BeTrue();
        }

        [Test]
        public void the_new_state_does_not_renumber_existing_contract_values()
        {
            ((int)TrackedDownloadState.Downloading).Should().Be(0);
            ((int)TrackedDownloadState.ImportFailed).Should().Be(5);
            ((int)TrackedDownloadState.Imported).Should().Be(6);
            ((int)TrackedDownloadState.Ignored).Should().Be(7);
            ((int)TrackedDownloadState.ImportBlocked).Should().Be(8);
        }
    }

    internal sealed class RetryTestClock : TimeProvider
    {
        private DateTimeOffset _now = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
}
