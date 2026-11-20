using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles
{
    public class DownloadedBooksCommandService : IExecute<DownloadedBooksScanCommand>
    {
        private readonly IDownloadedBooksImportService _downloadedTracksImportService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IDiskProvider _diskProvider;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly ICommandResultReporter _commandResultReporter;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly Logger _logger;

        public DownloadedBooksCommandService(IDownloadedBooksImportService downloadedTracksImportService,
                                                ITrackedDownloadService trackedDownloadService,
                                                IDiskProvider diskProvider,
                                                ICompletedDownloadService completedDownloadService,
                                                ICommandResultReporter commandResultReporter,
                                                IDownloadHistoryService downloadHistoryService,
                                                IProvideImportItemService provideImportItemService,
                                                Logger logger)
        {
            _downloadedTracksImportService = downloadedTracksImportService;
            _trackedDownloadService = trackedDownloadService;
            _diskProvider = diskProvider;
            _completedDownloadService = completedDownloadService;
            _commandResultReporter = commandResultReporter;
            _downloadHistoryService = downloadHistoryService;
            _provideImportItemService = provideImportItemService;
            _logger = logger;
        }

        private List<ImportResult> ProcessPath(DownloadedBooksScanCommand message)
        {
            if (message.ClientId.HasValue && (message.ClientId.Value <= 0 || message.DownloadClientId.IsNullOrWhiteSpace()))
            {
                throw new ArgumentException("A positive ClientId requires a download identifier in DownloadClientId.");
            }

            if (!_diskProvider.FolderExists(message.Path) && !_diskProvider.FileExists(message.Path))
            {
                _logger.Warn("Folder/File specified for import scan [{0}] doesn't exist.", message.Path);
                return new List<ImportResult>();
            }

            if (message.DownloadClientId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = message.ClientId.HasValue
                    ? _trackedDownloadService.Find(message.DownloadClientId, message.ClientId.Value)
                    : _trackedDownloadService.Find(message.DownloadClientId);

                if (trackedDownload == null && message.ClientId.HasValue)
                {
                    throw new InvalidOperationException("The selected download is no longer tracked by this client.");
                }

                if (trackedDownload != null)
                {
                    _logger.Debug("External directory scan request for known download {0}. [{1}]", message.DownloadClientId, message.Path);

                    if (message.ImportMode == ImportMode.Copy)
                    {
                        trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
                        if (trackedDownload.ImportItem == null)
                        {
                            throw new InvalidOperationException("The local download import path could not be determined.");
                        }

                        _downloadHistoryService.RecordSourceRetention(trackedDownload);
                        trackedDownload.KeepSourceFiles = true;
                    }

                    var importResults = _downloadedTracksImportService.ProcessPath(message.Path, message.ImportMode, trackedDownload.RemoteBook?.Author, trackedDownload.DownloadItem);

                    _completedDownloadService.VerifyImport(trackedDownload, importResults);

                    return importResults;
                }

                _logger.Warn("External directory scan request for unknown download {0}, attempting normal import. [{1}]", message.DownloadClientId, message.Path);
            }

            return _downloadedTracksImportService.ProcessPath(message.Path, message.ImportMode);
        }

        public void Execute(DownloadedBooksScanCommand message)
        {
            List<ImportResult> importResults;

            if (message.Path.IsNotNullOrWhiteSpace())
            {
                importResults = ProcessPath(message);
            }
            else
            {
                throw new ArgumentException("A path must be provided", "path");
            }

            if (importResults == null || importResults.All(v => v.Result != ImportResultType.Imported))
            {
                // Allow the command to complete successfully, but report as unsuccessful
                _logger.ProgressDebug("Failed to import");
                _commandResultReporter.Report(CommandResult.Unsuccessful);
            }
        }
    }
}
