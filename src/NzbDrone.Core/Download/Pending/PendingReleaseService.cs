using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Download.Pending
{
    public interface IPendingReleaseService
    {
        void Add(DownloadDecision decision, PendingReleaseReason reason);
        void AddMany(List<Tuple<DownloadDecision, PendingReleaseReason>> decisions);
        List<ReleaseInfo> GetPending();
        List<RemoteBook> GetPendingRemoteBooks(int authorId);
        List<Queue.Queue> GetPendingQueue();
        Queue.Queue FindPendingQueueItem(int queueId);
        void RemovePendingQueueItems(int queueId);
        RemoteBook OldestPendingRelease(int authorId, int[] bookIds);
    }

    public class PendingReleaseService : IPendingReleaseService,
                                         IHandle<AuthorDeletedEvent>,
                                         IHandle<BookGrabbedEvent>,
                                         IHandle<RssSyncCompleteEvent>,
                                         IHandle<MinimumUpgradeFormatScoreChangedEvent>,
                                         IHandle<QualityProfileUpdatedEvent>,
                                         IHandle<CustomFormatUpdatedEvent>,
                                         IHandle<DelayProfilesUpdatedEvent>,
                                         IHandle<ConfigSavedEvent>,
                                         IHandle<AuthorEditedEvent>,
                                         IHandle<AuthorsEditedEvent>,
                                         IHandle<AuthorRefreshCompleteEvent>,
                                         IHandle<BookAddedEvent>,
                                         IHandle<BookEditedEvent>,
                                         IHandle<BookMetadataRefreshedEvent>,
                                         IHandle<BookDeletedEvent>,
                                         IHandle<ProviderUpdatedEvent<IIndexer>>,
                                         IHandle<ProviderDeletedEvent<IIndexer>>,
                                         IHandle<ProviderAddedEvent<IDownloadClient>>,
                                         IHandle<ProviderUpdatedEvent<IDownloadClient>>,
                                         IHandle<ProviderDeletedEvent<IDownloadClient>>
    {
        private static readonly HashSet<string> PendingConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(IConfigService.MinimumAge),
            nameof(IConfigService.MaximumSize),
            nameof(IConfigService.RssSyncInterval),
            nameof(IConfigService.DownloadPropersAndRepacks),
            nameof(IConfigService.EnableCompletedDownloadHandling)
        };

        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IPendingReleaseRepository _repository;
        private readonly IAuthorService _authorService;
        private readonly IParsingService _parsingService;
        private readonly IDelayProfileService _delayProfileService;
        private readonly ITaskManager _taskManager;
        private readonly IConfigService _configService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IRemoteBookAggregationService _aggregationService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPendingReleasePolicy _policy;
        private readonly IDecisionEngineSpecification[] _upgradeChecks;
        private readonly Logger _logger;

        public PendingReleaseService(IIndexerStatusService indexerStatusService,
                                    IPendingReleaseRepository repository,
                                    IAuthorService authorService,
                                    IParsingService parsingService,
                                    IDelayProfileService delayProfileService,
                                    ITaskManager taskManager,
                                    IConfigService configService,
                                    ICustomFormatCalculationService formatCalculator,
                                    IRemoteBookAggregationService aggregationService,
                                    IDownloadClientFactory downloadClientFactory,
                                    IIndexerFactory indexerFactory,
                                    IEventAggregator eventAggregator,
                                    IPendingReleasePolicy policy,
                                    UpgradeDiskSpecification diskUpgrade,
                                    QueueSpecification queueUpgrade,
                                    HistorySpecification historyUpgrade,
                                    Logger logger)
        {
            _indexerStatusService = indexerStatusService;
            _repository = repository;
            _authorService = authorService;
            _parsingService = parsingService;
            _delayProfileService = delayProfileService;
            _taskManager = taskManager;
            _configService = configService;
            _formatCalculator = formatCalculator;
            _aggregationService = aggregationService;
            _downloadClientFactory = downloadClientFactory;
            _indexerFactory = indexerFactory;
            _eventAggregator = eventAggregator;
            _policy = policy;
            _upgradeChecks = new IDecisionEngineSpecification[] { diskUpgrade, queueUpgrade, historyUpgrade };
            _logger = logger;
        }

        public void Add(DownloadDecision decision, PendingReleaseReason reason)
        {
            AddMany(new List<Tuple<DownloadDecision, PendingReleaseReason>> { Tuple.Create(decision, reason) });
        }

        public void AddMany(List<Tuple<DownloadDecision, PendingReleaseReason>> decisions)
        {
            foreach (var authorDecisions in decisions.GroupBy(v => v.Item1.RemoteBook.Author.Id))
            {
                var author = authorDecisions.First().Item1.RemoteBook.Author;
                var alreadyPending = _repository.AllByAuthorId(author.Id);

                alreadyPending = IncludeRemoteBooks(alreadyPending, authorDecisions.ToDictionaryIgnoreDuplicates(v => v.Item1.RemoteBook.Release.Title, v => v.Item1.RemoteBook));
                foreach (var pair in authorDecisions)
                {
                    var decision = pair.Item1;
                    var reason = pair.Item2;

                    var bookIds = decision.RemoteBook.Books.Select(book => book.Id).ToHashSet();
                    var matchingReports = alreadyPending.Where(pending => bookIds.SetEquals(pending.RemoteBook.Books.Select(book => book.Id)))
                        .Where(MatchingReleasePredicate(decision.RemoteBook.Release)).OrderBy(pending => pending.Added).ThenBy(pending => pending.Id).ToList();

                    if (matchingReports.Any())
                    {
                        var matchingReport = matchingReports.First();

                        if (matchingReport.Reason != reason)
                        {
                            if (matchingReport.Reason == PendingReleaseReason.DownloadClientUnavailable)
                            {
                                _logger.Debug("The release {0} is already pending with reason {1}, not changing reason", decision.RemoteBook, matchingReport.Reason);
                            }
                            else
                            {
                                _logger.Debug("The release {0} is already pending with reason {1}, changing to {2}", decision.RemoteBook, matchingReport.Reason, reason);
                                matchingReport.Reason = reason;
                                _repository.Update(matchingReport);
                            }
                        }
                        else
                        {
                            _logger.Debug("The release {0} is already pending with reason {1}, not adding again", decision.RemoteBook, reason);
                        }

                        if (matchingReports.Count() > 1)
                        {
                            _logger.Debug("The release {0} had {1} duplicate pending, removing duplicates.", decision.RemoteBook, matchingReports.Count() - 1);

                            foreach (var duplicate in matchingReports.Skip(1))
                            {
                                _repository.Delete(duplicate.Id);
                                alreadyPending.Remove(duplicate);
                            }
                        }

                        continue;
                    }

                    _logger.Debug("Adding release {0} to pending releases with reason {1}", decision.RemoteBook, reason);
                    alreadyPending.Add(Insert(decision, reason));
                }
            }
        }

        public List<ReleaseInfo> GetPending()
        {
            var releases = _repository.All().Select(p =>
            {
                var release = p.Release;

                release.PendingReleaseReason = p.Reason;
                release.PendingReleaseId = p.Id;
                release.PendingAuthorId = p.AuthorId > 0 ? p.AuthorId : null;
                release.PendingBookIds = p.AdditionalInfo?.BookIds;
                release.IndexerFlags = p.AdditionalInfo?.IndexerFlags ?? release.IndexerFlags;

                return release;
            }).ToList();

            if (releases.Any())
            {
                releases = FilterBlockedIndexers(releases);
                var active = releases.Where(release => release.IndexerId > 0).Select(release => release.IndexerId).Distinct()
                    .Where(indexerId => _indexerFactory.Find(indexerId)?.Enable == true).ToHashSet();
                releases = releases.Where(release => release.IndexerId <= 0 || active.Contains(release.IndexerId)).ToList();
            }

            return releases;
        }

        public List<RemoteBook> GetPendingRemoteBooks(int authorId)
        {
            return IncludeRemoteBooks(_repository.AllByAuthorId(authorId)).Select(v => v.RemoteBook).Where(_policy.IsEligible).ToList();
        }

        public List<Queue.Queue> GetPendingQueue()
        {
            var queued = new List<Queue.Queue>();

            var nextRssSync = new Lazy<DateTime>(() => _taskManager.GetNextExecution(typeof(RssSyncCommand)));

            var pendingReleases = IncludeRemoteBooks(_repository.WithoutFallback()).Where(pending => _policy.IsEligible(pending.RemoteBook));
            foreach (var pendingRelease in pendingReleases)
            {
                foreach (var book in pendingRelease.RemoteBook.Books)
                {
                    var ect = pendingRelease.Release.PublishDate.AddMinutes(GetDelay(pendingRelease.RemoteBook));

                    if (ect < nextRssSync.Value)
                    {
                        ect = nextRssSync.Value;
                    }
                    else
                    {
                        ect = ect.AddMinutes(_configService.RssSyncInterval);
                    }

                    var timeleft = ect.Subtract(DateTime.UtcNow);

                    if (timeleft.TotalSeconds < 0)
                    {
                        timeleft = TimeSpan.Zero;
                    }

                    string downloadClientName = null;
                    var indexer = _indexerFactory.Find(pendingRelease.Release.IndexerId);

                    if (indexer is { DownloadClientId: > 0 })
                    {
                        var downloadClient = _downloadClientFactory.Find(indexer.DownloadClientId);

                        downloadClientName = downloadClient?.Name;
                    }

                    var queue = new Queue.Queue
                    {
                        Id = GetQueueId(pendingRelease, book),
                        Author = pendingRelease.RemoteBook.Author,
                        Book = book,
                        Quality = pendingRelease.RemoteBook.ParsedBookInfo.Quality,
                        Title = pendingRelease.Title,
                        Size = pendingRelease.RemoteBook.Release.Size,
                        Sizeleft = pendingRelease.RemoteBook.Release.Size,
                        RemoteBook = pendingRelease.RemoteBook,
                        Timeleft = timeleft,
                        EstimatedCompletionTime = ect,
                        Status = pendingRelease.Reason.ToString(),
                        Protocol = pendingRelease.RemoteBook.Release.DownloadProtocol,
                        Indexer = pendingRelease.RemoteBook.Release.Indexer,
                        DownloadClient = downloadClientName
                    };

                    queued.Add(queue);
                }
            }

            // Retain the same score and preferred-size ordering used when evaluating RSS reports.
            var comparer = new DownloadDecisionComparer(_configService, _delayProfileService);
            var deduped = queued.GroupBy(item => (item.Author.Id, item.Book.Id))
                .Select(group => group.OrderByDescending(item => new DownloadDecision(item.RemoteBook), comparer).First());

            return deduped.ToList();
        }

        public Queue.Queue FindPendingQueueItem(int queueId)
        {
            return GetPendingQueue().SingleOrDefault(p => p.Id == queueId);
        }

        public void RemovePendingQueueItems(int queueId)
        {
            var targetItem = FindPendingRelease(queueId);
            var authorReleases = _repository.AllByAuthorId(targetItem.AuthorId);

            var releasesToRemove = authorReleases.Where(
                c => c.ParsedBookInfo.BookTitle == targetItem.ParsedBookInfo.BookTitle);

            _repository.DeleteMany(releasesToRemove.Select(c => c.Id));
        }

        public RemoteBook OldestPendingRelease(int authorId, int[] bookIds)
        {
            var authorReleases = GetPendingReleases(authorId);

            return authorReleases.Select(r => r.RemoteBook)
                                 .Where(r => r.Books.Select(e => e.Id).Intersect(bookIds).Any() && _policy.IsEligible(r))
                                 .MaxBy(p => p.Release.AgeHours);
        }

        private List<ReleaseInfo> FilterBlockedIndexers(List<ReleaseInfo> releases)
        {
            var blockedIndexers = new HashSet<int>(_indexerStatusService.GetBlockedProviders().Select(v => v.ProviderId));

            return releases.Where(release => !blockedIndexers.Contains(release.IndexerId)).ToList();
        }

        private List<PendingRelease> GetPendingReleases()
        {
            return IncludeRemoteBooks(_repository.All().ToList());
        }

        private List<PendingRelease> GetPendingReleases(int authorId)
        {
            return IncludeRemoteBooks(_repository.AllByAuthorId(authorId).ToList());
        }

        private List<PendingRelease> IncludeRemoteBooks(List<PendingRelease> releases, Dictionary<string, RemoteBook> knownRemoteBooks = null, int? qualityProfileId = null)
        {
            var result = new List<PendingRelease>();

            var authorMap = new Dictionary<int, Author>();

            if (knownRemoteBooks != null)
            {
                foreach (var author in knownRemoteBooks.Values.Select(v => v.Author))
                {
                    if (!authorMap.ContainsKey(author.Id))
                    {
                        authorMap[author.Id] = author;
                    }
                }
            }

            if (releases.Count == 0)
            {
                return result;
            }

            foreach (var author in _authorService.FindAuthors(releases.Select(v => v.AuthorId).Distinct().Where(v => !authorMap.ContainsKey(v))))
            {
                authorMap[author.Id] = author;
            }

            foreach (var release in releases)
            {
                var author = authorMap.GetValueOrDefault(release.AuthorId);

                // Just in case the author was removed, but wasn't cleaned up yet (housekeeper will clean it up)
                if (author == null)
                {
                    continue;
                }

                if (qualityProfileId.HasValue && author.QualityProfileId != qualityProfileId.Value)
                {
                    continue;
                }

                List<Book> books;

                if (knownRemoteBooks != null && knownRemoteBooks.TryGetValue(release.Release.Title, out var knownRemoteBook))
                {
                    books = knownRemoteBook.Books;
                }
                else
                {
                    if (release.Release.HasStructuredBookIdentity())
                    {
                        // The stored parsed model omits its reference to the structured release.
                        release.ParsedBookInfo.StructuredRelease = release.Release;
                    }

                    books = _parsingService.GetBooks(release.ParsedBookInfo, author);
                }

                if (books == null || books.Count == 0 ||
                    (release.AdditionalInfo?.BookIds != null && !books.Select(book => book.Id).ToHashSet().SetEquals(release.AdditionalInfo.BookIds)))
                {
                    continue;
                }

                release.Release.IndexerFlags = release.AdditionalInfo?.IndexerFlags ?? release.Release.IndexerFlags;
                release.RemoteBook = new RemoteBook
                {
                    Author = author,
                    Books = books,
                    ReleaseSource = release.AdditionalInfo?.ReleaseSource ?? ReleaseSourceType.Unknown,
                    ParsedBookInfo = release.ParsedBookInfo,
                    Release = release.Release
                };

                _aggregationService.Augment(release.RemoteBook);
                release.RemoteBook.CustomFormats = _formatCalculator.ParseCustomFormat(release.RemoteBook, release.Release.Size) ?? new List<CustomFormat>();
                release.RemoteBook.CustomFormatScore = author.QualityProfile?.Value?.CalculateCustomFormatScore(release.RemoteBook.CustomFormats) ?? 0;

                result.Add(release);
            }

            return result;
        }

        private PendingRelease Insert(DownloadDecision decision, PendingReleaseReason reason)
        {
            var pending = new PendingRelease
            {
                AuthorId = decision.RemoteBook.Author.Id,
                ParsedBookInfo = decision.RemoteBook.ParsedBookInfo,
                Release = decision.RemoteBook.Release,
                Title = decision.RemoteBook.Release.Title,
                Added = DateTime.UtcNow,
                Reason = reason,
                RemoteBook = decision.RemoteBook,
                AdditionalInfo = new PendingReleaseAdditionalInfo
                {
                    ReleaseSource = decision.RemoteBook.ReleaseSource,
                    BookIds = decision.RemoteBook.Books.Select(book => book.Id).Distinct().ToList(),
                    IndexerFlags = decision.RemoteBook.Release.IndexerFlags
                }
            };

            _repository.Insert(pending);
            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
            return pending;
        }

        private void Delete(PendingRelease pendingRelease)
        {
            _repository.Delete(pendingRelease);
            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
        }

        private int GetDelay(RemoteBook remoteBook)
        {
            var delayProfile = _delayProfileService.AllForTags(remoteBook.Author.Tags).OrderBy(d => d.Order).First();
            var delay = delayProfile.GetProtocolDelay(remoteBook.Release.DownloadProtocol);
            var minimumAge = _configService.MinimumAge;

            return new[] { delay, minimumAge }.Max();
        }

        private void RemoveGrabbed(RemoteBook remoteBook)
        {
            var pendingReleases = GetPendingReleases(remoteBook.Author.Id);
            var bookIds = remoteBook.Books.Select(e => e.Id);

            var existingReports = pendingReleases.Where(r => r.RemoteBook.Books.Select(e => e.Id)
                                                             .Intersect(bookIds)
                                                             .Any())
                                                             .ToList();

            if (existingReports.Empty())
            {
                return;
            }

            var profile = remoteBook.Author.QualityProfile.Value;

            foreach (var existingReport in existingReports)
            {
                var compare = new QualityModelComparer(profile).Compare(remoteBook.ParsedBookInfo.Quality,
                                                                        existingReport.RemoteBook.ParsedBookInfo.Quality);

                //Only remove lower/equal quality pending releases
                //It is safer to retry these releases on the next round than remove it and try to re-add it (if its still in the feed)
                if (compare >= 0)
                {
                    _logger.Debug("Removing previously pending release, as it was grabbed.");
                    Delete(existingReport);
                }
            }
        }

        private void RemoveRejected(List<DownloadDecision> rejected)
        {
            _logger.Debug("Removing failed releases from pending");
            var pending = _repository.All().ToList();

            foreach (var rejectedRelease in rejected)
            {
                var permanentRejections = rejectedRelease.Rejections.Where(rejection => rejection.Type == RejectionType.Permanent).ToList();
                if (permanentRejections.Count > 0 && permanentRejections.All(rejection => PendingReleasePolicy.CanReconsider(rejection.Code)))
                {
                    // Keep the original delay clock when the reason may be cleared by a later policy change.
                    continue;
                }

                var report = rejectedRelease.RemoteBook.Release;
                var authorId = report.PendingAuthorId ?? rejectedRelease.RemoteBook.Author?.Id;
                var matching = pending.Where(release => !authorId.HasValue || release.AuthorId == authorId)
                    .Where(release => !report.PendingReleaseId.HasValue || release.Id == report.PendingReleaseId)
                    .Where(MatchingReleasePredicate(report)).ToList();

                foreach (var pendingRelease in matching)
                {
                    _logger.Debug("Removing previously pending release, as it has now been rejected.");
                    Delete(pendingRelease);
                    pending.Remove(pendingRelease);
                }
            }
        }

        private PendingRelease FindPendingRelease(int queueId)
        {
            return GetPendingReleases().First(p => p.RemoteBook.Books.Any(e => queueId == GetQueueId(p, e)));
        }

        private int GetQueueId(PendingRelease pendingRelease, Book book)
        {
            return HashConverter.GetHashInt31(string.Format("pending-{0}-book{1}", pendingRelease.Id, book.Id));
        }

        public void Handle(AuthorDeletedEvent message)
        {
            _repository.DeleteByAuthorId(message.Author.Id);
            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
        }

        public void Handle(BookGrabbedEvent message)
        {
            RemoveGrabbed(message.Book);
        }

        public void Handle(RssSyncCompleteEvent message)
        {
            RemoveRejected(message.ProcessedDecisions.Rejected);
        }

        public void Handle(MinimumUpgradeFormatScoreChangedEvent message)
        {
            var pending = IncludeRemoteBooks(_repository.All().ToList(), qualityProfileId: message.QualityProfileId);
            var belowMinimum = pending.Where(release =>
            {
                var book = release.RemoteBook;
                return book.Author.QualityProfile.Value.MinUpgradeFormatScore > 0 && book.Books.Any() &&
                    _upgradeChecks.Any(specification => specification.IsSatisfiedBy(book, null).Code == RejectionCode.CustomFormatUpgradeBelowMinimum);
            }).Select(release => release.Id).ToList();

            if (belowMinimum.Count > 0)
            {
                _repository.DeleteMany(belowMinimum);
                _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
            }
            else
            {
                NotifyPendingAuthors(author => author.QualityProfileId == message.QualityProfileId);
            }
        }

        public void Handle(QualityProfileUpdatedEvent message)
        {
            NotifyPendingAuthors(author => author.QualityProfileId == message.QualityProfileId);
        }

        public void Handle(CustomFormatUpdatedEvent message)
        {
            if (message.Removed)
            {
                NotifyIfPending();
                return;
            }

            NotifyPendingAuthors(author => author.QualityProfile?.Value?.FormatItems.Any(item =>
                item.Format.Id == message.FormatId && item.Score != 0) == true);
        }

        public void Handle(DelayProfilesUpdatedEvent message)
        {
            if (message.Tags == null)
            {
                NotifyIfPending();
                return;
            }

            NotifyPendingAuthors(author => author.Tags.Overlaps(message.Tags));
        }

        public void Handle(ConfigSavedEvent message)
        {
            if (message.ChangedKeys != null && PendingConfigKeys.Overlaps(message.ChangedKeys))
            {
                NotifyIfPending();
            }
        }

        public void Handle(AuthorEditedEvent message)
        {
            var current = message.Author;
            var old = message.OldAuthor;
            if (current.QualityProfileId != old.QualityProfileId || current.Monitored != old.Monitored ||
                !current.Tags.SetEquals(old.Tags) || current.AuthorMetadataId != old.AuthorMetadataId)
            {
                NotifyPendingIds(new[] { current.Id });
            }
        }

        public void Handle(AuthorsEditedEvent message)
        {
            NotifyPendingIds(message.AuthorIds);
        }

        public void Handle(AuthorRefreshCompleteEvent message)
        {
            NotifyPendingAuthors(author => author.AuthorMetadataId == message.Author.AuthorMetadataId);
        }

        public void Handle(BookAddedEvent message)
        {
            NotifyBookChanged(message.Book);
        }

        public void Handle(BookEditedEvent message)
        {
            NotifyBookChanged(message.Book);
            if (message.OldBook.AuthorMetadataId != message.Book.AuthorMetadataId)
            {
                NotifyBookChanged(message.OldBook);
            }
        }

        public void Handle(BookMetadataRefreshedEvent message)
        {
            NotifyPendingAuthors(author => author.AuthorMetadataId == message.AuthorMetadataId);
        }

        public void Handle(BookDeletedEvent message)
        {
            NotifyBookChanged(message.Book);
        }

        public void Handle(ProviderUpdatedEvent<IIndexer> message)
        {
            NotifyIfPending();
        }

        public void Handle(ProviderDeletedEvent<IIndexer> message)
        {
            NotifyIfPending();
        }

        public void Handle(ProviderAddedEvent<IDownloadClient> message)
        {
            NotifyIfPending();
        }

        public void Handle(ProviderUpdatedEvent<IDownloadClient> message)
        {
            NotifyIfPending();
        }

        public void Handle(ProviderDeletedEvent<IDownloadClient> message)
        {
            NotifyIfPending();
        }

        private void NotifyBookChanged(Book book)
        {
            // Multiple author profiles can share the same work metadata.
            NotifyPendingAuthors(author => author.AuthorMetadataId == book.AuthorMetadataId);
        }

        private void NotifyPendingAuthors(Func<Author, bool> affected)
        {
            var authorIds = _repository.GetAuthorIds();
            if (authorIds?.Count > 0 && _authorService.FindAuthors(authorIds).Any(affected))
            {
                _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
            }
        }

        private void NotifyPendingIds(IEnumerable<int> ids)
        {
            var authorIds = _repository.GetAuthorIds();
            if (authorIds?.Intersect(ids).Any() == true)
            {
                _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
            }
        }

        private void NotifyIfPending()
        {
            if (_repository.HasItems())
            {
                _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
            }
        }

        private static Func<PendingRelease, bool> MatchingReleasePredicate(ReleaseInfo release)
        {
            return p => p.Title == release.Title &&
                        p.Release.PublishDate == release.PublishDate &&
                        p.Release.Indexer == release.Indexer;
        }
    }
}
