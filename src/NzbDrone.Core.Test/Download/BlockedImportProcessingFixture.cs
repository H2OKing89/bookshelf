using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class BlockedImportProcessingFixture : CoreTest<DownloadProcessingService>
    {
        private RetryTestClock _clock;
        private ImportRetryService _retry;
        private TrackedDownload _tracked;

        [SetUp]
        public void Setup()
        {
            _clock = new RetryTestClock();
            _retry = new ImportRetryService(_clock);
            Mocker.SetConstant<IImportRetryService>(_retry);
            _tracked = new TrackedDownload
            {
                IsTrackable = true,
                DownloadItem = new DownloadClientItem { Status = DownloadItemStatus.Completed, Title = "Owned recording" }
            };
            _retry.Block(_tracked);
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.GetTrackedDownloads()).Returns(new List<TrackedDownload> { _tracked });
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableCompletedDownloadHandling).Returns(true);
        }

        [Test]
        public void a_block_is_not_reprocessed_during_every_monitoring_poll()
        {
            for (var poll = 0; poll < 10; poll++)
            {
                Subject.Execute(new ProcessMonitoredDownloadsCommand());
            }

            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Import(It.IsAny<TrackedDownload>()), Times.Never());
        }

        [Test]
        public void a_still_blocked_retry_is_rescheduled_and_does_not_repeat_transfer_attempts_immediately()
        {
            _clock.Advance(30);
            Mocker.GetMock<ICompletedDownloadService>().Setup(service => service.Import(_tracked)).Callback(() => _retry.Block(_tracked));

            Subject.Execute(new ProcessMonitoredDownloadsCommand());
            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Import(_tracked), Times.Once());
            _tracked.ImportRetryAfterUtc.Should().Be(_clock.GetUtcNow().UtcDateTime.AddSeconds(60));
        }

        [Test]
        public void fixed_imports_can_complete_once_the_retry_is_due()
        {
            _clock.Advance(30);
            Mocker.GetMock<ICompletedDownloadService>().Setup(service => service.Import(_tracked)).Callback(() =>
            {
                _tracked.State = TrackedDownloadState.Imported;
                _retry.Reset(_tracked);
            });

            Subject.Execute(new ProcessMonitoredDownloadsCommand());
            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            _tracked.State.Should().Be(TrackedDownloadState.Imported);
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Import(_tracked), Times.Once());
        }

        [Test]
        public void disabled_completed_download_handling_does_not_retry_blocks()
        {
            _clock.Advance(300);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableCompletedDownloadHandling).Returns(false);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Import(It.IsAny<TrackedDownload>()), Times.Never());
        }

        [Test]
        public void an_untrackable_item_does_not_retry()
        {
            _clock.Advance(300);
            _tracked.IsTrackable = false;

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.Import(It.IsAny<TrackedDownload>()), Times.Never());
        }

        [Test]
        public void a_retry_exception_remains_blocked_and_scheduled()
        {
            _clock.Advance(30);
            Mocker.GetMock<ICompletedDownloadService>().Setup(service => service.Import(_tracked)).Throws(new InvalidOperationException("Owned fixture failure"));

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            _tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
            _tracked.ImportRetryAfterUtc.Should().Be(_clock.GetUtcNow().UtcDateTime.AddSeconds(60));
            _tracked.StatusMessages.Should().ContainSingle();
        }
    }
}
