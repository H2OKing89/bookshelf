using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface ICompletedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void Import(TrackedDownload trackedDownload);
        bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults);
    }

    public class CompletedDownloadService : ICompletedDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IHistoryService _historyService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IDownloadedBooksImportService _downloadedTracksImportService;
        private readonly IDownloadImportCompletionService _completionService;
        private readonly IDownloadImportSourceService _sourceService;
        private readonly Logger _logger;

        public CompletedDownloadService(IEventAggregator eventAggregator,
                                        IHistoryService historyService,
                                        IProvideImportItemService provideImportItemService,
                                        IDownloadedBooksImportService downloadedTracksImportService,
                                        IDownloadImportCompletionService completionService,
                                        IDownloadImportSourceService sourceService,
                                        Logger logger)
        {
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedTracksImportService = downloadedTracksImportService;
            _completionService = completionService;
            _sourceService = sourceService;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            SetImportItem(trackedDownload);

            // Only process tracked downloads that are still downloading
            if (trackedDownload.State != TrackedDownloadState.Downloading)
            {
                return;
            }

            var historyItem = _historyService.MostRecentForDownloadId(trackedDownload.DownloadItem.DownloadId);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.WarnCode(RejectionCode.DownloadNotTracked, "Download wasn't grabbed by Readarr and not in a category, Skipping.");
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            SetImportItem(trackedDownload);

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                trackedDownload.State = TrackedDownloadState.Downloading;
                trackedDownload.WarnCode(RejectionCode.ClientNotCompleted, "Waiting for the download client to finish before completion verification.");
                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            List<ImportResult> importResults;
            try
            {
                importResults = _downloadedTracksImportService.ProcessPath(outputPath, ImportMode.Auto, trackedDownload.RemoteBook?.Author, trackedDownload.DownloadItem);
                if (VerifyImport(trackedDownload, importResults))
                {
                    return;
                }
            }
            catch
            {
                trackedDownload.State = TrackedDownloadState.ImportPending;
                trackedDownload.WarnCode(RejectionCode.ImportVerificationFailed, "Import verification did not finish. Source files and download cleanup remain pending.");
                throw;
            }

            if (importResults.Empty())
            {
                trackedDownload.WarnCode(RejectionCode.NoEligibleFiles, "No files found are eligible for import in {0}", outputPath);
                trackedDownload.State = TrackedDownloadState.ImportPending;
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.Any(c => c.Result != ImportResultType.Imported))
            {
                trackedDownload.State = TrackedDownloadState.ImportFailed;
                var statusMessages = importResults
                    .Where(v => v.Result != ImportResultType.Imported && v.ImportDecision.Item != null)
                    .Select(v => new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.Item.Path), v.Errors, v.Rejections))
                    .ToArray();

                trackedDownload.Warn(statusMessages);
                _eventAggregator.PublishEvent(new BookImportIncompleteEvent(trackedDownload));
                return;
            }
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                trackedDownload.State = TrackedDownloadState.Downloading;
                trackedDownload.WarnCode(RejectionCode.ClientNotCompleted, "Waiting for the download client to finish before completion verification.");
                return false;
            }

            try
            {
                var wasImported = trackedDownload.State == TrackedDownloadState.Imported;
                var receipt = _completionService.VerifyAndRecord(trackedDownload, importResults ?? new List<ImportResult>());
                if (receipt == null)
                {
                    trackedDownload.State = TrackedDownloadState.ImportPending;
                    trackedDownload.WarnCode(RejectionCode.CompletionUnverified, "Not all intended books and recording parts have verified committed files. Source files and download cleanup remain pending.");
                    return false;
                }

                trackedDownload.ImportReceipt = receipt;
                if (!_sourceService.Retire(trackedDownload, receipt))
                {
                    trackedDownload.State = TrackedDownloadState.ImportPending;
                    trackedDownload.WarnCode(RejectionCode.SourceCleanupPending, "The download source changed or could not be safely retired. Download cleanup remains pending.");
                    return false;
                }

                trackedDownload.State = TrackedDownloadState.Imported;
                trackedDownload.ClearWarnings();
                if (!wasImported)
                {
                    _logger.Debug("Verified all intended books and recording parts for {0}", trackedDownload.DownloadItem.Title);
                    _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, receipt.AuthorId));
                }

                return true;
            }
            catch
            {
                trackedDownload.State = TrackedDownloadState.ImportPending;
                trackedDownload.WarnCode(RejectionCode.ImportVerificationFailed, "Import verification did not finish. Source files and download cleanup remain pending.");
                throw;
            }
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private bool ValidatePath(TrackedDownload trackedDownload)
        {
            var downloadItemOutputPath = trackedDownload.ImportItem.OutputPath;

            if (downloadItemOutputPath.IsEmpty)
            {
                trackedDownload.WarnCode(RejectionCode.InvalidImportPath, "Download doesn't contain intermediate path, Skipping.");
                return false;
            }

            if ((OsInfo.IsWindows && !downloadItemOutputPath.IsWindowsPath) ||
                (OsInfo.IsNotWindows && !downloadItemOutputPath.IsUnixPath))
            {
                trackedDownload.WarnCode(RejectionCode.InvalidImportPath, "[{0}] is not a valid local path. You may need a Remote Path Mapping.", downloadItemOutputPath);
                return false;
            }

            return true;
        }
    }
}
