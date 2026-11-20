using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public class DownloadProcessingService : IExecute<ProcessMonitoredDownloadsCommand>
    {
        private readonly IConfigService _configService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IDownloadImportCompletionService _completionService;
        private readonly Logger _logger;

        public DownloadProcessingService(IConfigService configService,
                                         ICompletedDownloadService completedDownloadService,
                                         IFailedDownloadService failedDownloadService,
                                         ITrackedDownloadService trackedDownloadService,
                                         IEventAggregator eventAggregator,
                                         IDownloadHistoryService downloadHistoryService,
                                         IDownloadImportCompletionService completionService,
                                         Logger logger)
        {
            _configService = configService;
            _completedDownloadService = completedDownloadService;
            _failedDownloadService = failedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _eventAggregator = eventAggregator;
            _downloadHistoryService = downloadHistoryService;
            _completionService = completionService;
            _logger = logger;
        }

        private void RemoveCompletedDownloads()
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads()
                                                          .Where(t => !t.DownloadItem.Removed && t.DownloadItem.CanBeRemoved && t.State == TrackedDownloadState.Imported)
                                                          .ToList();

            foreach (var trackedDownload in trackedDownloads)
            {
                if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
                {
                    trackedDownload.State = TrackedDownloadState.Downloading;
                    trackedDownload.WarnCode(RejectionCode.ClientNotCompleted, "The download client has not confirmed completion. Cleanup remains pending.");
                    continue;
                }

                DownloadImportReceipt receipt;
                try
                {
                    receipt = _completionService.ReadReceipt(trackedDownload,
                        _downloadHistoryService.GetLatestDownloadHistoryItem(trackedDownload.DownloadItem.DownloadId));
                }
                catch (Exception error)
                {
                    _logger.Debug(error, "Could not revalidate imported download before cleanup");
                    receipt = null;
                }

                if (receipt == null)
                {
                    trackedDownload.State = TrackedDownloadState.ImportPending;
                    trackedDownload.WarnCode(RejectionCode.CompletionUnverified, "Previous download completion could not be verified. Rescan or review its recording parts before cleanup.");
                    continue;
                }

                trackedDownload.ImportReceipt = receipt;
                _eventAggregator.PublishEvent(new DownloadCanBeRemovedEvent(trackedDownload));
            }
        }

        public void Execute(ProcessMonitoredDownloadsCommand message)
        {
            var enableCompletedDownloadHandling = _configService.EnableCompletedDownloadHandling;
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads()
                                                          .Where(t => t.IsTrackable)
                                                          .ToList();

            foreach (var trackedDownload in trackedDownloads)
            {
                try
                {
                    if (trackedDownload.State == TrackedDownloadState.DownloadFailedPending)
                    {
                        _failedDownloadService.ProcessFailed(trackedDownload);
                    }
                    else if (enableCompletedDownloadHandling && trackedDownload.State == TrackedDownloadState.ImportPending)
                    {
                        _completedDownloadService.Import(trackedDownload);
                    }
                }
                catch (Exception e)
                {
                    _logger.Debug(e, "Failed to process download: {0}", trackedDownload.DownloadItem.Title);
                }
            }

            // Imported downloads are no longer trackable so process them after processing trackable downloads
            RemoveCompletedDownloads();

            _eventAggregator.PublishEvent(new DownloadsProcessedEvent());
        }
    }
}
