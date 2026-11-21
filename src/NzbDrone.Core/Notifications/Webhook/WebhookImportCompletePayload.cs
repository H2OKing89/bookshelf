using System;
using System.Collections.Generic;
using NzbDrone.Core.MediaFiles.BookImport;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookImportCompletePayload : WebhookPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public string EventId { get; set; }
        public DateTime CommittedAtUtc { get; set; }
        public int AuthorId { get; set; }
        public string AuthorName { get; set; }
        public int DownloadClientId { get; set; }
        public string DownloadClient { get; set; }
        public string DownloadClientType { get; set; }
        public string DownloadId { get; set; }
        public string Protocol { get; set; }
        public int? IndexerId { get; set; }
        public string Indexer { get; set; }
        public string ReleaseSource { get; set; }
        public int? GrabCustomFormatScore { get; set; }
        public List<WebhookCommittedRecording> Recordings { get; set; }
    }

    public class WebhookCommittedRecording
    {
        public int BookId { get; set; }
        public string BookTitle { get; set; }
        public int EditionId { get; set; }
        public string EditionTitle { get; set; }
        public string RecordingId { get; set; }
        public string Asin { get; set; }
        public string AudibleRegion { get; set; }
        public bool IsEbook { get; set; }
        public List<WebhookCommittedPart> Parts { get; set; } = new List<WebhookCommittedPart>();
    }

    public class WebhookCommittedPart
    {
        public int FileId { get; set; }
        public int Part { get; set; }
        public int PartCount { get; set; }
        public string SourceFileName { get; set; }
        public string ImportedFileName { get; set; }
        public long Size { get; set; }
        public string Quality { get; set; }
        public WebhookCommittedMedia Media { get; set; }
        public CommittedImportScores CustomFormatScores { get; set; }
    }

    public class WebhookCommittedMedia
    {
        public string Source { get; set; }
        public string SourceVersion { get; set; }
        public string AnalysisStatus { get; set; }
        public string ContainerFormat { get; set; }
        public string AudioCodec { get; set; }
        public int? AudioChannels { get; set; }
        public string AudioBitrateMode { get; set; }
        public int? AudioSampleRate { get; set; }
        public double? DurationSeconds { get; set; }
    }
}
