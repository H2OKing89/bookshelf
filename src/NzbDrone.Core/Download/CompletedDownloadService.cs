using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.History;
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
        private readonly IDownloadHistoryService _downloadHistory;
        private readonly IDownloadImportDiagnostics _diagnostics;
        private readonly IImportRetryService _retry;
        private readonly Logger _logger;

        public CompletedDownloadService(IEventAggregator eventAggregator,
                                        IHistoryService historyService,
                                        IProvideImportItemService provideImportItemService,
                                        IDownloadedBooksImportService downloadedTracksImportService,
                                        IDownloadImportCompletionService completionService,
                                        IDownloadImportSourceService sourceService,
                                        IDownloadHistoryService downloadHistory,
                                        IDownloadImportDiagnostics diagnostics,
                                        IImportRetryService retry,
                                        Logger logger)
        {
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedTracksImportService = downloadedTracksImportService;
            _completionService = completionService;
            _sourceService = sourceService;
            _downloadHistory = downloadHistory;
            _diagnostics = diagnostics;
            _retry = retry;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed || trackedDownload.State != TrackedDownloadState.Downloading)
            {
                return;
            }

            var historyItem = _historyService.MostRecentForDownloadId(trackedDownload.DownloadItem.DownloadId);
            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.WarnCode(RejectionCode.DownloadNotTracked, "Download wasn't grabbed by Readarr and not in a category, Skipping.");
                return;
            }

            try
            {
                SetImportItem(trackedDownload);
            }
            catch
            {
                trackedDownload.WarnCode(RejectionCode.ImportPathUnavailable, "The local import path could not be determined. Check the download client and remote path mapping.");
                _retry.Block(trackedDownload);
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                _retry.Block(trackedDownload);
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            var previousWarnings = STJson.ToJson(trackedDownload.StatusMessages);
            try
            {
                SetImportItem(trackedDownload);
                if (!ValidatePath(trackedDownload))
                {
                    AddDiagnostics(trackedDownload);
                    _retry.Block(trackedDownload);
                    PublishBlocked(trackedDownload, previousWarnings);
                    return;
                }

                if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
                {
                    trackedDownload.State = TrackedDownloadState.Downloading;
                    trackedDownload.WarnCode(RejectionCode.ClientNotCompleted, "Waiting for the download client to finish before completion verification.");
                    return;
                }

                trackedDownload.State = TrackedDownloadState.Importing;
                var saved = _downloadHistory.GetLatestImportCompletion(trackedDownload.DownloadItem.DownloadId, trackedDownload.DownloadClient);
                if (_completionService.ReadReceipt(trackedDownload, saved) != null)
                {
                    // A committed import needs completion/retirement retry only.
                    // Never transfer or tag its source again to retry cleanup.
                    if (!VerifyImportInternal(trackedDownload, new List<ImportResult>()))
                    {
                        PublishBlocked(trackedDownload, previousWarnings);
                    }

                    return;
                }

                var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
                var importResults = _downloadedTracksImportService.ProcessPath(outputPath, ImportMode.Auto, trackedDownload.RemoteBook?.Author, trackedDownload.DownloadItem) ?? new List<ImportResult>();
                if (VerifyImportInternal(trackedDownload, importResults))
                {
                    return;
                }

                if (importResults.Empty())
                {
                    trackedDownload.WarnCode(RejectionCode.NoEligibleFiles, "No files found are eligible for import in {0}", outputPath);
                    AddDiagnostics(trackedDownload);
                }
                else if (importResults.Any(result => result.Result != ImportResultType.Imported))
                {
                    var messages = importResults.Where(result => result.Result != ImportResultType.Imported && result.ImportDecision.Item != null)
                        .Select(result => new TrackedDownloadStatusMessage(Path.GetFileName(result.ImportDecision.Item.Path), result.Errors, result.Rejections)).ToArray();
                    if (messages.Length > 0)
                    {
                        trackedDownload.Warn(messages);
                    }
                }

                _retry.Block(trackedDownload);
                PublishBlocked(trackedDownload, previousWarnings);
            }
            catch
            {
                trackedDownload.WarnCode(RejectionCode.ImportVerificationFailed, "Import verification did not finish. Source files and download cleanup remain pending.");
                AddDiagnostics(trackedDownload);
                _retry.Block(trackedDownload);
                PublishBlocked(trackedDownload, previousWarnings);
                throw;
            }
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            var previousWarnings = STJson.ToJson(trackedDownload.StatusMessages);
            try
            {
                var complete = VerifyImportInternal(trackedDownload, importResults);
                if (!complete && trackedDownload.State == TrackedDownloadState.ImportBlocked)
                {
                    PublishBlocked(trackedDownload, previousWarnings);
                }

                return complete;
            }
            catch
            {
                trackedDownload.WarnCode(RejectionCode.ImportVerificationFailed, "Import verification did not finish. Source files and download cleanup remain pending.");
                _retry.Block(trackedDownload);
                PublishBlocked(trackedDownload, previousWarnings);
                throw;
            }
        }

        private bool VerifyImportInternal(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                trackedDownload.State = TrackedDownloadState.Downloading;
                trackedDownload.WarnCode(RejectionCode.ClientNotCompleted, "Waiting for the download client to finish before completion verification.");
                return false;
            }

            var wasImported = trackedDownload.State == TrackedDownloadState.Imported;
            var receipt = _completionService.VerifyAndRecord(trackedDownload, importResults ?? new List<ImportResult>());
            if (receipt == null)
            {
                trackedDownload.WarnCode(RejectionCode.CompletionUnverified, "Not all intended books and recording parts have verified committed files. Source files and download cleanup remain pending.");
                _retry.Block(trackedDownload);
                return false;
            }

            trackedDownload.ImportReceipt = receipt;
            if (!_sourceService.Retire(trackedDownload, receipt))
            {
                trackedDownload.WarnCode(RejectionCode.SourceCleanupPending, "The download source changed or could not be safely retired. Download cleanup remains pending.");
                _retry.Block(trackedDownload);
                return false;
            }

            trackedDownload.State = TrackedDownloadState.Imported;
            _retry.Reset(trackedDownload);
            trackedDownload.ClearWarnings();
            if (!wasImported && !trackedDownload.ImportCompletionAnnounced)
            {
                _logger.Debug("Verified all intended books and recording parts for {0}", trackedDownload.DownloadItem.Title);
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, receipt.AuthorId));
                trackedDownload.ImportCompletionAnnounced = true;
            }

            return true;
        }

        private void AddDiagnostics(TrackedDownload trackedDownload)
        {
            try
            {
                var warnings = _diagnostics.GetWarnings(trackedDownload);
                if (warnings?.Count > 0)
                {
                    trackedDownload.Warn(trackedDownload.StatusMessages.Concat(warnings).ToArray());
                }
            }
            catch (Exception error)
            {
                _logger.Debug("Import diagnostics were unavailable ({0}); retaining the original warning", error.GetType().Name);
            }
        }

        private void PublishBlocked(TrackedDownload trackedDownload, string previousWarnings)
        {
            if (previousWarnings != STJson.ToJson(trackedDownload.StatusMessages))
            {
                _eventAggregator.PublishEvent(new BookImportIncompleteEvent(trackedDownload));
            }
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private bool ValidatePath(TrackedDownload trackedDownload)
        {
            var downloadItemOutputPath = trackedDownload.ImportItem?.OutputPath ?? default;

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
