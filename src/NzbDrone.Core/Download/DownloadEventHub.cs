using System;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public class DownloadEventHub : IHandle<DownloadFailedEvent>,
                                    IHandle<DownloadCompletedEvent>,
                                    IHandle<DownloadCanBeRemovedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IDownloadImportCompletionService _completionService;
        private readonly IDownloadImportSourceService _sourceService;
        private readonly Logger _logger;

        public DownloadEventHub(IConfigService configService,
            IProvideDownloadClient downloadClientProvider,
            IDownloadHistoryService downloadHistoryService,
            IDownloadImportCompletionService completionService,
            IDownloadImportSourceService sourceService,
            Logger logger)
        {
            _configService = configService;
            _downloadClientProvider = downloadClientProvider;
            _downloadHistoryService = downloadHistoryService;
            _completionService = completionService;
            _sourceService = sourceService;
            _logger = logger;
        }

        public void Handle(DownloadFailedEvent message)
        {
            var trackedDownload = message.TrackedDownload;

            if (trackedDownload == null ||
                message.TrackedDownload.DownloadItem.Removed ||
                !trackedDownload.DownloadItem.CanBeRemoved)
            {
                return;
            }

            var downloadClient = _downloadClientProvider.Get(message.TrackedDownload.DownloadClient);
            var definition = downloadClient.Definition as DownloadClientDefinition;

            if (!definition.RemoveFailedDownloads)
            {
                return;
            }

            RemoveFromDownloadClient(trackedDownload, downloadClient);
        }

        public void Handle(DownloadCompletedEvent message)
        {
            var trackedDownload = message.TrackedDownload;
            if (trackedDownload?.DownloadItem?.Removed == true || !HasVerifiedImport(trackedDownload))
            {
                return;
            }

            var downloadClient = _downloadClientProvider.Get(trackedDownload.DownloadClient);
            var definition = downloadClient.Definition as DownloadClientDefinition;

            MarkItemAsImported(trackedDownload, downloadClient);

            if (trackedDownload.DownloadItem.Removed ||
                !trackedDownload.DownloadItem.CanBeRemoved ||
                trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            if (!definition.RemoveCompletedDownloads)
            {
                return;
            }

            RemoveFromDownloadClient(message.TrackedDownload, downloadClient, trackedDownload.DownloadItem.CanMoveFiles && !trackedDownload.KeepSourceFiles && !trackedDownload.ImportReceipt.KeepSourceFiles);
        }

        public void Handle(DownloadCanBeRemovedEvent message)
        {
            var trackedDownload = message.TrackedDownload;
            if (trackedDownload?.DownloadItem?.Removed == true || !HasVerifiedImport(trackedDownload))
            {
                return;
            }

            var downloadClient = _downloadClientProvider.Get(trackedDownload.DownloadClient);
            var definition = downloadClient.Definition as DownloadClientDefinition;

            if (trackedDownload.DownloadItem.Removed ||
                !trackedDownload.DownloadItem.CanBeRemoved ||
                !definition.RemoveCompletedDownloads)
            {
                return;
            }

            RemoveFromDownloadClient(message.TrackedDownload, downloadClient, trackedDownload.DownloadItem.CanMoveFiles && !trackedDownload.KeepSourceFiles && !trackedDownload.ImportReceipt.KeepSourceFiles);
        }

        private bool HasVerifiedImport(TrackedDownload trackedDownload)
        {
            if (trackedDownload?.DownloadItem == null)
            {
                return false;
            }

            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                trackedDownload.State = TrackedDownloadState.Downloading;
                trackedDownload.WarnCode(RejectionCode.ClientNotCompleted, "The download client has not reported completion. Completion cannot authorize cleanup.");
                return false;
            }

            DownloadImportReceipt receipt;
            try
            {
                receipt = _completionService.ReadReceipt(trackedDownload,
                    _downloadHistoryService.GetLatestDownloadHistoryItem(trackedDownload.DownloadItem.DownloadId));
            }
            catch (Exception error)
            {
                _logger.Debug(error, "Could not verify completed download before client cleanup");
                receipt = null;
            }

            if (receipt == null)
            {
                trackedDownload.State = TrackedDownloadState.ImportPending;
                trackedDownload.WarnCode(RejectionCode.CompletionUnverified, "Download cleanup requires verified committed books and recording parts.");
                return false;
            }

            trackedDownload.ImportReceipt = receipt;
            try
            {
                if (_sourceService.Retire(trackedDownload, receipt))
                {
                    return true;
                }
            }
            catch (Exception error)
            {
                _logger.Debug(error, "Could not safely retire completed download sources before client cleanup");
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
            trackedDownload.WarnCode(RejectionCode.SourceCleanupPending, "The download source changed or could not be safely retired. Download cleanup remains pending.");
            return false;
        }

        private void RemoveFromDownloadClient(TrackedDownload trackedDownload, IDownloadClient downloadClient, bool deleteData = true)
        {
            try
            {
                _logger.Debug("[{0}] Removing download from {1} history", trackedDownload.DownloadItem.Title, trackedDownload.DownloadItem.DownloadClientInfo.Name);
                downloadClient.RemoveItem(trackedDownload.DownloadItem, deleteData);
                trackedDownload.DownloadItem.Removed = true;
            }
            catch (NotSupportedException)
            {
                _logger.Warn("Removing item not supported by your download client ({0}).", downloadClient.Definition.Name);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't remove item {0} from client {1}", trackedDownload.DownloadItem.Title, downloadClient.Name);
            }
        }

        private void MarkItemAsImported(TrackedDownload trackedDownload, IDownloadClient downloadClient)
        {
            try
            {
                _logger.Debug("[{0}] Marking download as imported from {1}", trackedDownload.DownloadItem.Title, trackedDownload.DownloadItem.DownloadClientInfo.Name);
                downloadClient.MarkItemAsImported(trackedDownload.DownloadItem);
            }
            catch (NotSupportedException e)
            {
                _logger.Debug(e.Message);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't mark item {0} as imported from client {1}", trackedDownload.DownloadItem.Title, downloadClient.Name);
            }
        }
    }
}
