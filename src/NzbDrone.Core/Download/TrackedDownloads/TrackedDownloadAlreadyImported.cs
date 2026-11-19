using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadAlreadyImported
    {
        bool IsImported(TrackedDownload trackedDownload, List<EntityHistory> historyItems);
    }

    public class TrackedDownloadAlreadyImported : ITrackedDownloadAlreadyImported
    {
        private readonly Logger _logger;
        private readonly IDownloadImportCompletionService _completionService;
        private readonly IDownloadHistoryService _downloadHistory;

        public TrackedDownloadAlreadyImported(IDownloadImportCompletionService completionService, IDownloadHistoryService downloadHistory, Logger logger)
        {
            _completionService = completionService;
            _downloadHistory = downloadHistory;
            _logger = logger;
        }

        public bool IsImported(TrackedDownload trackedDownload, List<EntityHistory> historyItems)
        {
            _logger.Trace("Checking if all books for '{0}' have been imported", trackedDownload.DownloadItem.Title);

            var history = _downloadHistory.GetLatestDownloadHistoryItem(trackedDownload.DownloadItem.DownloadId);
            var allBooksImportedInHistory = _completionService.ReadReceipt(trackedDownload, history) != null;

            _logger.Trace("All books for '{0}' have been imported: {1}", trackedDownload.DownloadItem.Title, allBooksImportedInHistory);

            return allBooksImportedInHistory;
        }
    }
}
