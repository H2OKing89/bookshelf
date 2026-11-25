using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IRejectedImportService
    {
        bool TryProcess(TrackedDownload trackedDownload, List<ImportResult> results, Rejection localRejection);
    }

    public class RejectedImportService : IRejectedImportService
    {
        private readonly IIndexerFactory _indexers;
        private readonly IDownloadHistoryRepository _downloads;
        private readonly IHistoryService _history;
        private readonly IRejectedImportRepository _repository;
        private readonly IDownloadImportSourceService _sources;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public RejectedImportService(IIndexerFactory indexers, IDownloadHistoryRepository downloads, IHistoryService history, IRejectedImportRepository repository, IDownloadImportSourceService sources, IEventAggregator events, Logger logger)
        {
            _indexers = indexers;
            _downloads = downloads;
            _history = history;
            _repository = repository;
            _sources = sources;
            _events = events;
            _logger = logger;
        }

        public static bool IsPolicyCode(string code) => code is RejectionCode.DangerousFile or RejectionCode.ExecutableFile or RejectionCode.UserRejectedExtension;

        internal static RejectedImportAction ActionFor(IndexerDefinition indexer, string code) => code switch
        {
            RejectionCode.DangerousFile => indexer.DangerousFileAction,
            RejectionCode.ExecutableFile => indexer.ExecutableFileAction,
            RejectionCode.UserRejectedExtension => indexer.UserRejectedExtensionAction,
            _ => RejectedImportAction.Review
        };

        public bool TryProcess(TrackedDownload trackedDownload, List<ImportResult> results, Rejection localRejection)
        {
            var item = trackedDownload?.DownloadItem;
            if (localRejection == null || !IsPolicyCode(localRejection.Code) || localRejection.Type != RejectionType.Permanent ||
                item == null || item.Removed || item.Status != DownloadItemStatus.Completed || item.IsEncrypted ||
                item.DownloadClientInfo?.Id != trackedDownload.DownloadClient || trackedDownload.DownloadClient <= 0 || string.IsNullOrWhiteSpace(item.DownloadId) ||
                trackedDownload.ImportReceipt != null || (trackedDownload.State != TrackedDownloadState.ImportBlocked && trackedDownload.State != TrackedDownloadState.Importing) ||
                (results?.Any(result => result.Result != ImportResultType.Rejected || result.ImportedFile != null ||
                    result.Rejections.Count == 0 || result.Rejections.Any(rejection => rejection.Type != RejectionType.Permanent || rejection.Code != localRejection.Code)) ?? true))
            {
                return false;
            }

            try
            {
                if (!_sources.IsSafeRejectionSource(trackedDownload))
                {
                    return Review(trackedDownload, "The output source is not a verified local download outside the library. Review the rejection manually.");
                }

                var scoped = (_downloads.FindByDownloadId(item.DownloadId) ?? new List<DownloadHistory>())
                    .Where(row => row.DownloadId == item.DownloadId && row.DownloadClientId == trackedDownload.DownloadClient)
                    .OrderByDescending(row => row.Date).ThenByDescending(row => row.Id).ToList();
                var grab = scoped.FirstOrDefault(row => row.EventType == DownloadHistoryEventType.DownloadGrabbed);
                if (grab?.Id <= 0 || grab?.Release == null || grab.IndexerId <= 0 || grab.Release.IndexerId != grab.IndexerId ||
                    grab.AuthorId <= 0 || string.IsNullOrWhiteSpace(grab.Release.Guid) || grab.SourceTitle != grab.Release.Title ||
                    grab.Protocol != item.DownloadClientInfo.Protocol || grab.Protocol != grab.Release.DownloadProtocol)
                {
                    return Review(trackedDownload, "The download has no unambiguous saved indexer grab. Review the rejection manually.");
                }

                var newer = scoped.TakeWhile(row => row.Id != grab.Id).ToList();
                if (newer.Any(row => row.EventType is DownloadHistoryEventType.FileImported or DownloadHistoryEventType.DownloadImported or DownloadHistoryEventType.DownloadIgnored or DownloadHistoryEventType.DownloadSourceRetained))
                {
                    return Review(trackedDownload, "The download has previous import or source-retention evidence. Review the remaining files manually.");
                }

                if (newer.Any(row => row.EventType == DownloadHistoryEventType.DownloadFailed))
                {
                    trackedDownload.State = TrackedDownloadState.DownloadFailed;
                    return true;
                }

                var indexer = _indexers.Find(grab.IndexerId);
                if (indexer?.Id != grab.IndexerId || !indexer.Enable)
                {
                    return Review(trackedDownload, "The original indexer is unavailable, disabled or deleted. Review the rejection manually.");
                }

                var action = ActionFor(indexer, localRejection.Code);
                if (action != RejectedImportAction.Blocklist && action != RejectedImportAction.BlocklistAndRetry)
                {
                    return Review(trackedDownload, "The original indexer's rejection policy requires manual review. No automatic failure, retry or removal was performed.");
                }

                var failure = BuildFailure(trackedDownload, grab, localRejection, action);
                if (failure == null)
                {
                    return Review(trackedDownload, "The original grab's book or release identity is incomplete or conflicting. Review the rejection manually.");
                }

                var recorded = _repository.TryRecord(grab, failure, action);
                if (recorded == RejectedImportRecordResult.Changed)
                {
                    return Review(trackedDownload, "The saved grab or rejection policy changed. Review the rejection manually.");
                }

                trackedDownload.State = TrackedDownloadState.DownloadFailed;
                if (recorded == RejectedImportRecordResult.Applied)
                {
                    failure.AutomaticRejectionRecorded = true;
                    _events.PublishEvent(failure);
                }

                return true;
            }
            catch (Exception error)
            {
                // Do not act on a partial write or an unverified history lookup.
                // Do not include provider data in logs.
                _logger.Debug("Unable to apply indexer rejection policy ({0}); retaining download", error.GetType().Name);
                return Review(trackedDownload, "The indexer rejection policy could not be recorded. The download and its files were retained.");
            }
        }

        private DownloadFailedEvent BuildFailure(TrackedDownload trackedDownload, DownloadHistory grab, Rejection rejection, RejectedImportAction action)
        {
            if (!STJson.TryDeserialize<List<int>>(Value(grab.Data, "bookIds") ?? string.Empty, out var books) || books == null ||
                books.Count == 0 || books.Count > 4096 || books.Any(book => book <= 0) || books.Distinct().Count() != books.Count)
            {
                return null;
            }

            // Match the per-book grab records to the persisted release GUID and
            // original client, never to a newly parsed title or another client.
            var history = _history.FindByDownloadId(grab.DownloadId, grab.DownloadClientId) ?? new List<EntityHistory>();
            if (history.Any(row => row.Date >= grab.Date.AddSeconds(-1) && row.EventType is EntityHistoryEventType.BookFileImported or EntityHistoryEventType.DownloadImported or EntityHistoryEventType.DownloadIgnored or EntityHistoryEventType.DownloadFailed))
            {
                return null;
            }

            var matches = history.Where(row => row.EventType == EntityHistoryEventType.Grabbed && row.DownloadId == grab.DownloadId &&
                row.AuthorId == grab.AuthorId && row.SourceTitle == grab.SourceTitle && row.Quality != null &&
                row.Date >= grab.Date.AddMinutes(-2) && row.Date <= grab.Date.AddMinutes(2) && Value(row.Data, "guid") == grab.Release.Guid &&
                int.TryParse(Value(row.Data, "downloadClientId"), out var client) && client == grab.DownloadClientId).ToList();
            if (matches.Count != books.Count || !matches.Select(row => row.BookId).ToHashSet().SetEquals(books) ||
                matches.Select(row => row.BookId).Distinct().Count() != books.Count)
            {
                return null;
            }

            var first = matches[0];
            if (matches.Any(row => !Equals(row.Quality, first.Quality)) || grab.Release.PublishDate == default ||
                grab.Release.Size < 0 || string.IsNullOrWhiteSpace(grab.Release.Indexer) || (grab.Protocol != DownloadProtocol.Torrent && grab.Protocol != DownloadProtocol.Usenet))
            {
                return null;
            }

            var data = new Dictionary<string, string>(first.Data, StringComparer.OrdinalIgnoreCase)
            {
                ["publishedDate"] = grab.Release.PublishDate.ToString("O", CultureInfo.InvariantCulture),
                ["size"] = grab.Release.Size.ToString(CultureInfo.InvariantCulture),
                ["indexer"] = grab.Release.Indexer,
                ["protocol"] = ((int)grab.Protocol).ToString(CultureInfo.InvariantCulture)
            };
            Enum.TryParse(Value(grab.Data, "releaseSource"), true, out ReleaseSourceType releaseSource);
            var retry = action == RejectedImportAction.BlocklistAndRetry && releaseSource != ReleaseSourceType.Unknown;

            return new DownloadFailedEvent
            {
                AuthorId = grab.AuthorId,
                BookIds = books,
                Quality = first.Quality,
                SourceTitle = grab.SourceTitle,
                DownloadId = grab.DownloadId,
                DownloadClient = Value(first.Data, EntityHistory.DOWNLOAD_CLIENT),
                Message = $"{rejection.Reason}. Indexer policy: marked failed and blocklisted; {(retry ? "search again is subject to Failed Download Handling settings" : "no automatic search")}. Client data retained.",
                Data = data,
                TrackedDownload = trackedDownload,
                SkipRedownload = !retry,
                ReleaseSource = releaseSource,
                ImportRejectionCode = rejection.Code,
                ImportRejectionAction = action.ToString()
            };
        }

        private static string Value(Dictionary<string, string> data, string key) => data?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        private static bool Review(TrackedDownload trackedDownload, string explanation)
        {
            var rejection = new Rejection(explanation, RejectionType.Temporary, RejectionCode.IndexerRejectionReview);
            trackedDownload.Warn(trackedDownload.StatusMessages.Concat(new[]
            {
                new TrackedDownloadStatusMessage("Indexer rejection policy", new List<string> { explanation }, new[] { rejection })
            }).ToArray());
            return false;
        }
    }
}
