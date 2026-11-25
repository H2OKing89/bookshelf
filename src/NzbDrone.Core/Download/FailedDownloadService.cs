using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IFailedDownloadService
    {
        void MarkAsFailed(int historyId, bool skipRedownload = false);
        void MarkAsFailed(string downloadId, bool skipRedownload = false);
        void MarkAsFailed(string downloadId, int downloadClientId, bool skipRedownload = false);
        void BlockQueueDownload(List<EntityHistory> grabbedItems, TrackedDownload trackedDownload, bool skipRedownload);
        void Check(TrackedDownload trackedDownload);
        void ProcessFailed(TrackedDownload trackedDownload);
    }

    public class FailedDownloadService : IFailedDownloadService
    {
        private readonly IHistoryService _historyService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDownloadHistoryRepository _downloadHistory;

        public FailedDownloadService(IHistoryService historyService,
                                     ITrackedDownloadService trackedDownloadService,
                                     IEventAggregator eventAggregator,
                                     IDownloadHistoryRepository downloadHistory)
        {
            _historyService = historyService;
            _trackedDownloadService = trackedDownloadService;
            _eventAggregator = eventAggregator;
            _downloadHistory = downloadHistory;
        }

        public void MarkAsFailed(int historyId, bool skipRedownload = false)
        {
            var history = _historyService.Get(historyId);

            var downloadId = history.DownloadId;
            if (downloadId.IsNullOrWhiteSpace())
            {
                PublishDownloadFailedEvent(new List<EntityHistory> { history }, "Manually marked as failed", skipRedownload: skipRedownload);
            }
            else
            {
                var clientId = GetClientId(history);
                if (clientId.HasValue)
                {
                    MarkAsFailed(downloadId, clientId.Value, skipRedownload);
                }
                else
                {
                    MarkAsFailed(downloadId, skipRedownload);
                }
            }
        }

        public void MarkAsFailed(string downloadId, bool skipRedownload = false)
        {
            // Resolve a legacy request before reading or modifying any client's history.
            var trackedDownload = _trackedDownloadService.Find(downloadId);
            if (trackedDownload != null)
            {
                MarkAsFailed(downloadId, trackedDownload.DownloadClient, skipRedownload);
                return;
            }

            var history = _historyService.Find(downloadId, EntityHistoryEventType.Grabbed);
            var clients = history.Select(GetClientId).Where(id => id.HasValue).Select(id => id.Value)
                .Concat((_downloadHistory.FindByDownloadId(downloadId) ?? new List<DownloadHistory>())
                    .Where(item => item.DownloadClientId > 0 && item.DownloadId == downloadId).Select(item => item.DownloadClientId))
                .Distinct().ToList();
            if (clients.Count > 1)
            {
                throw new AmbiguousTrackedDownloadException(downloadId);
            }

            if (clients.Count == 1)
            {
                MarkAsFailed(downloadId, clients[0], skipRedownload);
            }
            else if (history.Any())
            {
                PublishDownloadFailedEvent(history, "Manually marked as failed", skipRedownload: skipRedownload);
            }
        }

        public void MarkAsFailed(string downloadId, int downloadClientId, bool skipRedownload = false)
        {
            if (downloadClientId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(downloadClientId));
            }

            var history = _historyService.Find(downloadId, EntityHistoryEventType.Grabbed, downloadClientId);
            if (history.Any())
            {
                var trackedDownload = _trackedDownloadService.Find(downloadId, downloadClientId);
                PublishDownloadFailedEvent(history, "Manually marked as failed", trackedDownload, skipRedownload);
            }
        }

        public void BlockQueueDownload(List<EntityHistory> grabbedItems, TrackedDownload trackedDownload, bool skipRedownload)
        {
            PublishDownloadFailedEvent(grabbedItems, "Manually marked as failed", trackedDownload, skipRedownload, suppressAutomaticRemoval: true);
        }

        private static int? GetClientId(EntityHistory history)
        {
            var value = history.Data?.FirstOrDefault(pair => string.Equals(pair.Key, "downloadClientId", StringComparison.OrdinalIgnoreCase)).Value;
            return int.TryParse(value, out var clientId) && clientId > 0 ? clientId : null;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            // Import blocks must not hide a subsequent client-side failure.
            if (trackedDownload.State != TrackedDownloadState.Downloading &&
                trackedDownload.State != TrackedDownloadState.ImportPending &&
                trackedDownload.State != TrackedDownloadState.ImportBlocked &&
                trackedDownload.State != TrackedDownloadState.ImportFailed)
            {
                return;
            }

            if (trackedDownload.DownloadItem.IsEncrypted ||
                trackedDownload.DownloadItem.Status == DownloadItemStatus.Failed)
            {
                var grabbedItems = _historyService
                                   .Find(trackedDownload.DownloadItem.DownloadId, EntityHistoryEventType.Grabbed, trackedDownload.DownloadClient)
                                   .ToList();

                if (grabbedItems.Empty())
                {
                    trackedDownload.WarnCode(RejectionCode.DownloadNotTracked, "Download wasn't grabbed by Readarr, skipping");
                    return;
                }

                trackedDownload.State = TrackedDownloadState.DownloadFailedPending;
            }
        }

        public void ProcessFailed(TrackedDownload trackedDownload)
        {
            if (trackedDownload.State != TrackedDownloadState.DownloadFailedPending)
            {
                return;
            }

            var grabbedItems = _historyService
                               .Find(trackedDownload.DownloadItem.DownloadId, EntityHistoryEventType.Grabbed, trackedDownload.DownloadClient)
                               .ToList();

            if (grabbedItems.Empty())
            {
                return;
            }

            var failure = "Failed download detected";

            if (trackedDownload.DownloadItem.IsEncrypted)
            {
                failure = "Encrypted download detected";
            }
            else if (trackedDownload.DownloadItem.Status == DownloadItemStatus.Failed && trackedDownload.DownloadItem.Message.IsNotNullOrWhiteSpace())
            {
                failure = trackedDownload.DownloadItem.Message;
            }

            trackedDownload.State = TrackedDownloadState.DownloadFailed;
            PublishDownloadFailedEvent(grabbedItems, failure, trackedDownload);
        }

        private void PublishDownloadFailedEvent(List<EntityHistory> historyItems, string message, TrackedDownload trackedDownload = null, bool skipRedownload = false, bool suppressAutomaticRemoval = false)
        {
            var historyItem = historyItems.Last();
            Enum.TryParse(historyItem.Data.GetValueOrDefault(EntityHistory.RELEASE_SOURCE, ReleaseSourceType.Unknown.ToString()), out ReleaseSourceType releaseSource);

            var downloadFailedEvent = new DownloadFailedEvent
            {
                AuthorId = historyItem.AuthorId,
                BookIds = historyItems.Select(h => h.BookId).ToList(),
                Quality = historyItem.Quality,
                SourceTitle = historyItem.SourceTitle,
                DownloadClient = historyItem.Data.GetValueOrDefault(EntityHistory.DOWNLOAD_CLIENT),
                DownloadId = historyItem.DownloadId,
                Message = message,
                Data = historyItem.Data,
                TrackedDownload = trackedDownload,
                SkipRedownload = skipRedownload,
                SuppressAutomaticRemoval = suppressAutomaticRemoval,
                ReleaseSource = releaseSource
            };

            _eventAggregator.PublishEvent(downloadFailedEvent);
        }
    }
}
