using System;
using System.Collections.Generic;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using Readarr.Api.V1.CustomFormats;

namespace Readarr.Api.V1.Queue
{
    public class DownloadQueueAuthor
    {
        public string AuthorName { get; set; }
        public string TitleSlug { get; set; }
    }

    // Intentionally independent of the numeric-id v1 QueueResource.
    public class DownloadQueueResource
    {
        public string Id { get; set; }
        public string Revision { get; set; }
        public QueueDownloadKind Kind { get; set; }
        public QueueCoverageSource CoverageSource { get; set; }
        public List<QueueRecording> Recordings { get; set; }
        public int? AuthorId { get; set; }
        public DownloadQueueAuthor Author { get; set; }
        public bool CanRemove { get; set; }
        public bool CanRemoveFromClient { get; set; }
        public bool CanChangeCategory { get; set; }
        public bool CanIgnore { get; set; }
        public bool CanBlocklist { get; set; }
        public bool CanRetry { get; set; }
        public bool CanGrab { get; set; }
        public bool CanImport { get; set; }
        public QualityModel Quality { get; set; }
        public List<CustomFormatResource> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public decimal Size { get; set; }
        public string Title { get; set; }
        public decimal Sizeleft { get; set; }
        public TimeSpan? Timeleft { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public string Status { get; set; }
        public TrackedDownloadStatus? TrackedDownloadStatus { get; set; }
        public TrackedDownloadState? TrackedDownloadState { get; set; }
        public List<TrackedDownloadStatusMessage> StatusMessages { get; set; }
        public string ErrorMessage { get; set; }
        public string DownloadId { get; set; }
        public int? DownloadClientId { get; set; }
        public bool? DownloadClientAvailable { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string DownloadClient { get; set; }
        public string Indexer { get; set; }
        public string OutputPath { get; set; }
        public bool DownloadForced { get; set; }

        public static DownloadQueueResource From(QueueDownload download)
        {
            var row = download.Item.ToResource(false, false);
            return new DownloadQueueResource
            {
                Id = download.Id,
                Revision = download.Revision,
                Kind = download.Kind,
                CoverageSource = download.CoverageSource,
                Recordings = download.Recordings,
                AuthorId = download.AuthorId,
                Author = download.Author == null ? null : new DownloadQueueAuthor { AuthorName = download.Author.Name, TitleSlug = download.Author.Metadata.Value.TitleSlug },
                CanRemove = download.CanRemove,
                CanRemoveFromClient = download.CanRemoveFromClient,
                CanChangeCategory = download.CanChangeCategory,
                CanIgnore = download.CanIgnore,
                CanBlocklist = download.CanBlocklist,
                CanRetry = download.CanRetry,
                CanGrab = download.CanGrab,
                CanImport = download.CanImport,
                Quality = row.Quality,
                CustomFormats = row.CustomFormats,
                CustomFormatScore = row.CustomFormatScore,
                Size = row.Size,
                Title = row.Title,
                Sizeleft = row.Sizeleft,
                Timeleft = row.Timeleft,
                EstimatedCompletionTime = row.EstimatedCompletionTime,
                Status = row.Status,
                TrackedDownloadStatus = row.TrackedDownloadStatus,
                TrackedDownloadState = row.TrackedDownloadState,
                StatusMessages = row.StatusMessages,
                ErrorMessage = row.ErrorMessage,
                DownloadId = row.DownloadId,
                DownloadClientId = row.DownloadClientId,
                DownloadClientAvailable = row.DownloadClientAvailable,
                Protocol = row.Protocol,
                DownloadClient = row.DownloadClient,
                Indexer = row.Indexer,
                OutputPath = row.OutputPath,
                DownloadForced = row.DownloadForced
            };
        }
    }

    public class DownloadQueueTarget
    {
        public string Id { get; set; }
        public string Revision { get; set; }
    }

    public class DownloadQueueActionRequest
    {
        public List<DownloadQueueTarget> Targets { get; set; }
    }

    public class DownloadQueueRemoveRequest : DownloadQueueActionRequest
    {
        public bool RemoveFromClient { get; set; }
        public bool ChangeCategory { get; set; }
        public bool Blocklist { get; set; }
        public bool SkipRedownload { get; set; }
    }
}
