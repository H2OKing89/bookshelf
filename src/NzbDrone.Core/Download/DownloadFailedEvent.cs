using System.Collections.Generic;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Download
{
    public class DownloadFailedEvent : IEvent
    {
        public DownloadFailedEvent()
        {
            Data = new Dictionary<string, string>();
        }

        public int AuthorId { get; set; }
        public List<int> BookIds { get; set; }
        public QualityModel Quality { get; set; }
        public string SourceTitle { get; set; }
        public string DownloadClient { get; set; }
        public string DownloadId { get; set; }
        public string Message { get; set; }
        public Dictionary<string, string> Data { get; set; }
        public TrackedDownload TrackedDownload { get; set; }
        public bool SkipRedownload { get; set; }

        // Set only after the automatic import rejection's blocklist and failure
        // marker have committed. This path must never remove client data.
        public bool AutomaticRejectionRecorded { get; set; }
        public string ImportRejectionCode { get; set; }
        public string ImportRejectionAction { get; set; }
        public ReleaseSourceType ReleaseSource { get; set; }
    }
}
