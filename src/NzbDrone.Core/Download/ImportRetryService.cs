using System;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Download
{
    public interface IImportRetryService
    {
        void Block(TrackedDownload trackedDownload);
        bool CanRetry(TrackedDownload trackedDownload);
        void Reset(TrackedDownload trackedDownload);
    }

    public class ImportRetryService : IImportRetryService
    {
        private readonly TimeProvider _timeProvider;

        public ImportRetryService(TimeProvider timeProvider = null)
        {
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public void Block(TrackedDownload trackedDownload)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (trackedDownload.State == TrackedDownloadState.ImportBlocked && trackedDownload.ImportRetryAfterUtc > now)
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportBlocked;
            trackedDownload.ImportRetryCount = Math.Min(trackedDownload.ImportRetryCount + 1, 5);
            var delaySeconds = Math.Min(30 * Math.Pow(2, trackedDownload.ImportRetryCount - 1), 300);
            trackedDownload.ImportRetryAfterUtc = now.AddSeconds(delaySeconds);
        }

        public bool CanRetry(TrackedDownload trackedDownload)
        {
            return trackedDownload?.DownloadItem != null && !trackedDownload.DownloadItem.Removed &&
                trackedDownload.DownloadItem.Status == DownloadItemStatus.Completed &&
                (trackedDownload.State == TrackedDownloadState.ImportBlocked || trackedDownload.State == TrackedDownloadState.ImportFailed) &&
                (!trackedDownload.ImportRetryAfterUtc.HasValue || trackedDownload.ImportRetryAfterUtc <= _timeProvider.GetUtcNow().UtcDateTime);
        }

        public void Reset(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportRetryCount = 0;
            trackedDownload.ImportRetryAfterUtc = null;
        }
    }
}
