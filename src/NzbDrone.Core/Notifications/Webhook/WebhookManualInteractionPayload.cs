using System.Collections.Generic;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookManualInteractionPayload : WebhookPayload
    {
        public int PayloadVersion { get; set; } = 1;
        public WebhookAuthor Author { get; set; }
        public List<ManualInteractionBook> Books { get; set; }
        public string Message { get; set; }
        public int DownloadClientId { get; set; }
        public string DownloadClient { get; set; }
        public string DownloadClientType { get; set; }
        public string DownloadId { get; set; }
        public string SourceTitle { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string DownloadStatus { get; set; }
        public string ImportStatus { get; set; }
        public List<TrackedDownloadStatusMessage> DownloadStatusMessages { get; set; }
    }
}
