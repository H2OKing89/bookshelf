using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadService
    {
        TrackedDownload Find(string downloadId);
        TrackedDownload Find(string downloadId, int downloadClientId);
        void StopTracking(string downloadId);
        void StopTracking(string downloadId, int downloadClientId);
        void StopTracking(List<string> downloadIds);
        void StopTrackingMissing(int downloadClientId, List<string> downloadIds);
        void StopTrackingClient(int downloadClientId);
        TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem);
        List<TrackedDownload> GetTrackedDownloads();
        void UpdateTrackable(List<TrackedDownload> trackedDownloads);
    }

    public class TrackedDownloadService : ITrackedDownloadService,
                                          IHandle<BookGrabbedEvent>,
                                          IHandle<BookInfoRefreshedEvent>,
                                          IHandle<AuthorDeletedEvent>,
                                          IHandle<ConfigSavedEvent>,
                                          IHandle<ProviderUpdatedEvent<IDownloadClient>>,
                                          IHandle<ProviderDeletedEvent<IDownloadClient>>
    {
        private readonly IParsingService _parsingService;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IDownloadImportCompletionService _completionService;
        private readonly IImportRetryService _importRetryService;
        private readonly Logger _logger;
        private readonly ICached<TrackedDownload> _cache;

        public TrackedDownloadService(IParsingService parsingService,
                                      ICacheManager cacheManager,
                                      IHistoryService historyService,
                                      IEventAggregator eventAggregator,
                                      IDownloadHistoryService downloadHistoryService,
                                      ICustomFormatCalculationService formatCalculator,
                                      IDownloadImportCompletionService completionService,
                                      IImportRetryService importRetryService,
                                      Logger logger)
        {
            _parsingService = parsingService;
            _historyService = historyService;
            _cache = cacheManager.GetCache<TrackedDownload>(GetType());
            _formatCalculator = formatCalculator;
            _eventAggregator = eventAggregator;
            _downloadHistoryService = downloadHistoryService;
            _completionService = completionService;
            _importRetryService = importRetryService;
            _logger = logger;
        }

        public TrackedDownload Find(string downloadId)
        {
            var matches = _cache.Values.Where(item => string.Equals(item.DownloadItem.DownloadId, downloadId, StringComparison.Ordinal)).Take(2).ToList();
            if (matches.Count > 1)
            {
                throw new AmbiguousTrackedDownloadException(downloadId);
            }

            return matches.SingleOrDefault();
        }

        public TrackedDownload Find(string downloadId, int downloadClientId)
        {
            return _cache.Find(GetCacheKey(downloadClientId, downloadId));
        }

        private static string GetCacheKey(int downloadClientId, string downloadId)
        {
            return $"{downloadClientId}:{downloadId}";
        }

        public void UpdateBookCache(int bookId)
        {
            var updateCacheItems = _cache.Values.Where(x => x.RemoteBook != null && x.RemoteBook.Books.Any(a => a.Id == bookId)).ToList();

            if (updateCacheItems.Any())
            {
                foreach (var item in updateCacheItems)
                {
                    var parsedBookInfo = Parser.Parser.ParseBookTitle(item.DownloadItem.Title);
                    item.RemoteBook = null;
                    _importRetryService.Reset(item);

                    if (parsedBookInfo != null)
                    {
                        item.RemoteBook = _parsingService.Map(parsedBookInfo);
                    }
                }

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void StopTracking(string downloadId)
        {
            RemoveTrackedDownloads(new[] { Find(downloadId) });
        }

        public void StopTracking(string downloadId, int downloadClientId)
        {
            RemoveTrackedDownloads(new[] { Find(downloadId, downloadClientId) });
        }

        public void StopTracking(List<string> downloadIds)
        {
            // Resolve every identity before mutating the cache, including ambiguous legacy requests.
            RemoveTrackedDownloads(downloadIds.Select(downloadId => Find(downloadId)).ToList());
        }

        public void StopTrackingMissing(int downloadClientId, List<string> downloadIds)
        {
            var present = downloadIds.ToHashSet(StringComparer.Ordinal);
            RemoveTrackedDownloads(_cache.Values.Where(item => item.DownloadClient == downloadClientId && !present.Contains(item.DownloadItem.DownloadId)).ToList());
        }

        public void StopTrackingClient(int downloadClientId)
        {
            RemoveTrackedDownloads(_cache.Values.Where(item => item.DownloadClient == downloadClientId).ToList());
        }

        private void RemoveTrackedDownloads(IEnumerable<TrackedDownload> items)
        {
            var trackedDownloads = items.Where(item => item != null).Distinct().ToList();
            foreach (var item in trackedDownloads)
            {
                _cache.Remove(GetCacheKey(item.DownloadClient, item.DownloadItem.DownloadId));
            }

            if (trackedDownloads.Any())
            {
                _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(trackedDownloads));
            }
        }

        public TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem)
        {
            var existingItem = Find(downloadItem.DownloadId, downloadClient.Id);

            if (existingItem != null && existingItem.State != TrackedDownloadState.Downloading)
            {
                LogItemChange(existingItem, existingItem.DownloadItem, downloadItem);

                var sourceChanged = existingItem.DownloadItem.OutputPath != downloadItem.OutputPath;
                var categoryChanged = !string.Equals(existingItem.DownloadItem.Category, downloadItem.Category, StringComparison.Ordinal);
                if (sourceChanged || categoryChanged)
                {
                    existingItem.ImportItem = null;
                    existingItem.ImportReceipt = null;
                    if (sourceChanged)
                    {
                        existingItem.ImportCompletionAnnounced = false;
                    }

                    _importRetryService.Reset(existingItem);
                    if (existingItem.State == TrackedDownloadState.Imported)
                    {
                        _importRetryService.Block(existingItem);
                        existingItem.WarnCode(RejectionCode.CompletionUnverified, "Download output path or category changed. Previous completion cannot authorize cleanup in the new context.");
                    }
                    else if (existingItem.State == TrackedDownloadState.ImportPending ||
                             existingItem.State == TrackedDownloadState.ImportBlocked ||
                             existingItem.State == TrackedDownloadState.ImportFailed)
                    {
                        existingItem.State = TrackedDownloadState.Downloading;
                    }
                }

                existingItem.DownloadItem = downloadItem;
                existingItem.DownloadClient = downloadClient.Id;
                existingItem.Protocol = downloadClient.Protocol;
                existingItem.IsTrackable = true;

                return existingItem;
            }

            var trackedDownload = new TrackedDownload
            {
                DownloadClient = downloadClient.Id,
                DownloadItem = downloadItem,
                Protocol = downloadClient.Protocol,
                IsTrackable = true,
                KeepSourceFiles = existingItem?.KeepSourceFiles ?? false,
                ImportCompletionAnnounced = existingItem?.ImportCompletionAnnounced ?? false,
                HasNotifiedManualInteractionRequired = existingItem?.HasNotifiedManualInteractionRequired ?? false
            };

            try
            {
                var parsedBookInfo = Parser.Parser.ParseBookTitle(trackedDownload.DownloadItem.Title);
                var historyItems = _historyService.FindByDownloadId(downloadItem.DownloadId, downloadClient.Id)
                    .OrderByDescending(h => h.Date)
                    .ToList();

                if (parsedBookInfo != null)
                {
                    trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo);
                }

                var downloadHistory = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadItem.DownloadId, downloadClient.Id);

                if (downloadHistory != null)
                {
                    var state = GetStateFromHistory(downloadHistory.EventType);
                    trackedDownload.State = state;

                    if (downloadHistory.EventType == DownloadHistoryEventType.DownloadImportIncomplete)
                    {
                        var savedMessages = downloadHistory.Data?.FirstOrDefault(pair =>
                            string.Equals(pair.Key, "statusMessages", StringComparison.OrdinalIgnoreCase)).Value;
                        if (savedMessages != null && STJson.TryDeserialize<List<TrackedDownloadStatusMessage>>(savedMessages, out var messages) && messages != null)
                        {
                            trackedDownload.Warn(messages.ToArray());
                        }
                        else
                        {
                            trackedDownload.WarnCode(RejectionCode.ImportVerificationFailed, "Previous import needs review. Saved import diagnostics could not be read.");
                        }

                        _importRetryService.Block(trackedDownload);
                    }
                }

                if (historyItems.Any())
                {
                    var firstHistoryItem = historyItems.First();
                    var grabbedEvent = historyItems.FirstOrDefault(v => v.EventType == EntityHistoryEventType.Grabbed);

                    trackedDownload.Indexer = grabbedEvent?.Data?.GetValueOrDefault("indexer");

                    if (parsedBookInfo == null ||
                        trackedDownload.RemoteBook?.Author == null ||
                        trackedDownload.RemoteBook.Books.Empty())
                    {
                        // Try parsing the original source title and if that fails, try parsing it as a special
                        var historyAuthor = firstHistoryItem.Author;
                        var historyBooks = new List<Book> { firstHistoryItem.Book };

                        parsedBookInfo = Parser.Parser.ParseBookTitle(firstHistoryItem.SourceTitle);

                        if (parsedBookInfo != null)
                        {
                            trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo,
                                firstHistoryItem.AuthorId,
                                historyItems.Where(v => v.EventType == EntityHistoryEventType.Grabbed).Select(h => h.BookId)
                                    .Distinct());
                        }
                        else
                        {
                            parsedBookInfo =
                                Parser.Parser.ParseBookTitleWithSearchCriteria(firstHistoryItem.SourceTitle,
                                    historyAuthor,
                                    historyBooks);

                            if (parsedBookInfo != null)
                            {
                                trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo,
                                    firstHistoryItem.AuthorId,
                                    historyItems.Where(v => v.EventType == EntityHistoryEventType.Grabbed).Select(h => h.BookId)
                                        .Distinct());
                            }
                        }
                    }

                    if (trackedDownload.RemoteBook != null &&
                        Enum.TryParse(grabbedEvent?.Data?.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                    {
                        trackedDownload.RemoteBook.Release ??= new ReleaseInfo();
                        trackedDownload.RemoteBook.Release.IndexerFlags = flags;
                    }

                    var languages = RecordingLanguageEvidence.ReadHistory(grabbedEvent?.Data, RecordingLanguageEvidence.LanguagesKey);
                    if (trackedDownload.RemoteBook != null && languages != null)
                    {
                        trackedDownload.RemoteBook.RecordedLanguages = RecordingLanguageEvidence.ParseStored(languages);
                        trackedDownload.RemoteBook.SelectedEditionLanguageId = RecordingLanguageEvidence.ReadEditionLanguage(grabbedEvent.Data);
                    }

                    var audio = AudioFormatEvidence.FromHistory(grabbedEvent?.Data);
                    if (trackedDownload.RemoteBook != null && audio?.Origin == AudioEvidenceOrigin.Release)
                    {
                        trackedDownload.RemoteBook.RecordedAudioFormatEvidence = audio;
                    }
                }

                // Calculate custom formats
                if (trackedDownload.RemoteBook != null)
                {
                    trackedDownload.RemoteBook.CustomFormats = _formatCalculator.ParseCustomFormat(trackedDownload.RemoteBook, downloadItem.TotalSize);
                }

                if (downloadHistory?.EventType == DownloadHistoryEventType.DownloadImported)
                {
                    var receipt = _completionService.ReadReceipt(trackedDownload, downloadHistory);
                    if (receipt == null)
                    {
                        _importRetryService.Block(trackedDownload);
                        trackedDownload.WarnCode(RejectionCode.CompletionUnverified, "Previous import completion could not be verified. Download files are retained for review.");
                    }
                    else
                    {
                        trackedDownload.ImportReceipt = receipt;
                        trackedDownload.KeepSourceFiles = receipt.KeepSourceFiles;
                    }
                }

                // Track it so it can be displayed in the queue even though we can't determine which artist it is for
                if (trackedDownload.RemoteBook == null)
                {
                    _logger.Trace("No Book found for download '{0}'", trackedDownload.DownloadItem.Title);
                }
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Failed to find book for " + downloadItem.Title);
                return null;
            }

            LogItemChange(trackedDownload, existingItem?.DownloadItem, trackedDownload.DownloadItem);

            _cache.Set(GetCacheKey(trackedDownload.DownloadClient, trackedDownload.DownloadItem.DownloadId), trackedDownload);
            return trackedDownload;
        }

        public List<TrackedDownload> GetTrackedDownloads()
        {
            return _cache.Values.ToList();
        }

        public void UpdateTrackable(List<TrackedDownload> trackedDownloads)
        {
            var current = trackedDownloads.Select(item => GetCacheKey(item.DownloadClient, item.DownloadItem.DownloadId)).ToHashSet(StringComparer.Ordinal);
            foreach (var trackedDownload in GetTrackedDownloads())
            {
                trackedDownload.IsTrackable = current.Contains(GetCacheKey(trackedDownload.DownloadClient, trackedDownload.DownloadItem.DownloadId));
            }
        }

        private void LogItemChange(TrackedDownload trackedDownload, DownloadClientItem existingItem, DownloadClientItem downloadItem)
        {
            if (existingItem == null ||
                existingItem.Status != downloadItem.Status ||
                existingItem.CanBeRemoved != downloadItem.CanBeRemoved ||
                existingItem.CanMoveFiles != downloadItem.CanMoveFiles)
            {
                _logger.Debug("Tracking '{0}:{1}': ClientState={2}{3} ReadarrStage={4} Book='{5}' OutputPath={6}.",
                    downloadItem.DownloadClientInfo?.Name,
                    downloadItem.Title,
                    downloadItem.Status,
                    downloadItem.CanBeRemoved ? "" : downloadItem.CanMoveFiles ? " (busy)" : " (readonly)",
                    trackedDownload.State,
                    trackedDownload.RemoteBook?.ParsedBookInfo,
                    downloadItem.OutputPath);
            }
        }

        private void UpdateCachedItem(TrackedDownload trackedDownload)
        {
            var parsedEpisodeInfo = Parser.Parser.ParseBookTitle(trackedDownload.DownloadItem.Title);

            trackedDownload.RemoteBook = parsedEpisodeInfo == null ? null : _parsingService.Map(parsedEpisodeInfo, 0, new[] { 0 });
        }

        private static TrackedDownloadState GetStateFromHistory(DownloadHistoryEventType eventType)
        {
            switch (eventType)
            {
                case DownloadHistoryEventType.DownloadImportIncomplete:
                    return TrackedDownloadState.ImportBlocked;
                case DownloadHistoryEventType.DownloadImported:
                    return TrackedDownloadState.Imported;
                case DownloadHistoryEventType.DownloadFailed:
                    return TrackedDownloadState.DownloadFailed;
                case DownloadHistoryEventType.DownloadIgnored:
                    return TrackedDownloadState.Ignored;
                default:
                    return TrackedDownloadState.Downloading;
            }
        }

        public void Handle(ConfigSavedEvent message)
        {
            ResetBlockedImports(_cache.Values);
        }

        public void Handle(BookGrabbedEvent message)
        {
            if (message.DownloadClientId > 0 && message.DownloadId.IsNotNullOrWhiteSpace())
            {
                // A new grab supersedes every state of this client's previous job, including
                // imported/ignored entries and their completion/notification acknowledgments.
                StopTracking(message.DownloadId, message.DownloadClientId);
            }
        }

        public void Handle(ProviderUpdatedEvent<IDownloadClient> message)
        {
            var affected = _cache.Values.Where(item => item.DownloadClient == message.Definition.Id).ToList();
            foreach (var item in affected)
            {
                item.IsClientAvailable = false;
                item.IsTrackable = false;
            }

            ResetBlockedImports(affected);
        }

        public void Handle(ProviderDeletedEvent<IDownloadClient> message)
        {
            StopTrackingClient(message.ProviderId);
        }

        private void ResetBlockedImports(IEnumerable<TrackedDownload> items)
        {
            foreach (var item in items.Where(item => item.State == TrackedDownloadState.ImportBlocked || item.State == TrackedDownloadState.ImportFailed))
            {
                item.ImportItem = null;
                item.ImportReceipt = null;
                _importRetryService.Reset(item);
            }
        }

        public void Handle(BookInfoRefreshedEvent message)
        {
            var affectedBlocked = _cache.Values.Where(item =>
                (item.State == TrackedDownloadState.ImportBlocked || item.State == TrackedDownloadState.ImportFailed) &&
                (item.RemoteBook?.Author == null || item.RemoteBook.Author.Id == message.Author?.Id)).ToList();
            foreach (var item in affectedBlocked)
            {
                var parsed = Parser.Parser.ParseBookTitle(item.DownloadItem.Title);
                item.RemoteBook = parsed == null ? null : _parsingService.Map(parsed);
            }

            ResetBlockedImports(affectedBlocked);
            var needsToUpdate = false;

            foreach (var episode in message.Removed)
            {
                var cachedItems = _cache.Values.Where(t =>
                                            t.RemoteBook?.Books != null &&
                                            t.RemoteBook.Books.Any(e => e.Id == episode.Id))
                                        .ToList();

                if (cachedItems.Any())
                {
                    needsToUpdate = true;
                }

                cachedItems.ForEach(UpdateCachedItem);
            }

            if (needsToUpdate)
            {
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(AuthorDeletedEvent message)
        {
            var cachedItems = _cache.Values.Where(t =>
                                        t.RemoteBook?.Author != null &&
                                        t.RemoteBook.Author.Id == message.Author.Id)
                                    .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }
    }
}
