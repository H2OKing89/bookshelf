using System.Linq;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownload
    {
        public int DownloadClient { get; set; }
        public DownloadClientItem DownloadItem { get; set; }
        public DownloadClientItem ImportItem { get; set; }
        public TrackedDownloadState State { get; set; }
        public TrackedDownloadStatus Status { get; private set; }
        public RemoteBook RemoteBook { get; set; }
        public TrackedDownloadStatusMessage[] StatusMessages { get; private set; }
        public DownloadProtocol Protocol { get; set; }
        public string Indexer { get; set; }
        public bool IsTrackable { get; set; }
        public bool KeepSourceFiles { get; set; }
        public DownloadImportReceipt ImportReceipt { get; set; }

        public TrackedDownload()
        {
            StatusMessages = new TrackedDownloadStatusMessage[] { };
        }

        public void Warn(string message, params object[] args)
        {
            var statusMessage = string.Format(message, args);
            Warn(new TrackedDownloadStatusMessage(DownloadItem.Title, statusMessage));
        }

        public void WarnCode(string code, string message, params object[] args)
        {
            var rejection = new Rejection(string.Format(message, args), RejectionType.Temporary, code);
            Warn(new TrackedDownloadStatusMessage(DownloadItem.Title, new[] { rejection.Reason }.ToList(), new[] { rejection }));
        }

        public void Warn(params TrackedDownloadStatusMessage[] statusMessages)
        {
            Status = TrackedDownloadStatus.Warning;
            StatusMessages = statusMessages;
        }

        public void ClearWarnings()
        {
            Status = TrackedDownloadStatus.Ok;
            StatusMessages = new TrackedDownloadStatusMessage[] { };
        }
    }

    public enum TrackedDownloadState
    {
        Downloading,
        DownloadFailed,
        DownloadFailedPending,
        ImportPending,
        Importing,
        ImportFailed,
        Imported,
        Ignored
    }

    public enum TrackedDownloadStatus
    {
        Ok,
        Warning,
        Error
    }
}
