using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;
using Readarr.Http;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Readarr.Api.V1.Indexers
{
    [V1ApiController]
    public class ReleaseController : ReleaseControllerBase
    {
        private readonly IFetchAndParseRss _rssFetcherAndParser;
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
        private readonly IDownloadService _downloadService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IParsingService _parsingService;
        private readonly Logger _logger;

        private static readonly TimeSpan SearchResultLifetime = TimeSpan.FromMinutes(30);
        private readonly ICached<CachedReleases> _remoteBookCache;

        public ReleaseController(IFetchAndParseRss rssFetcherAndParser,
                             ISearchForReleases releaseSearchService,
                             IMakeDownloadDecision downloadDecisionMaker,
                             IPrioritizeDownloadDecision prioritizeDownloadDecision,
                             IDownloadService downloadService,
                             IAuthorService authorService,
                             IBookService bookService,
                             IParsingService parsingService,
                             ICacheManager cacheManager,
                             Logger logger)
        {
            _rssFetcherAndParser = rssFetcherAndParser;
            _releaseSearchService = releaseSearchService;
            _downloadDecisionMaker = downloadDecisionMaker;
            _prioritizeDownloadDecision = prioritizeDownloadDecision;
            _downloadService = downloadService;
            _authorService = authorService;
            _bookService = bookService;
            _parsingService = parsingService;
            _logger = logger;

            PostValidator.RuleFor(s => s.IndexerId).ValidId();
            PostValidator.RuleFor(s => s.Guid).NotEmpty();

            _remoteBookCache = cacheManager.GetCache<CachedReleases>(GetType(), "remoteBooks");
        }

        [HttpPost]
        public async Task<ActionResult<ReleaseResource>> DownloadRelease(ReleaseResource release)
        {
            ValidateResource(release);

            var cached = FindRelease(release);
            var remoteBook = cached.RemoteBook.CopyForInteractiveGrab();

            try
            {
                if (remoteBook.Author == null)
                {
                    if (release.BookId.HasValue)
                    {
                        var book = _bookService.GetBook(release.BookId.Value);

                        remoteBook.Author = _authorService.GetAuthor(book.AuthorId);
                        remoteBook.Books = new List<Book> { book };
                    }
                    else if (release.AuthorId.HasValue)
                    {
                        var author = _authorService.GetAuthor(release.AuthorId.Value);
                        var books = _parsingService.GetBooks(remoteBook.ParsedBookInfo, author);

                        if (books.Empty())
                        {
                            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse books in the release");
                        }

                        remoteBook.Author = author;
                        remoteBook.Books = books;
                    }
                    else
                    {
                        throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to find matching author and books");
                    }
                }
                else if (remoteBook.Books.Empty())
                {
                    var books = _parsingService.GetBooks(remoteBook.ParsedBookInfo, remoteBook.Author);

                    if (books.Empty() && release.BookId.HasValue)
                    {
                        var book = _bookService.GetBook(release.BookId.Value);
                        if (book.AuthorId != remoteBook.Author.Id)
                        {
                            throw ContextChanged();
                        }

                        books = new List<Book> { book };
                    }

                    remoteBook.Books = books;
                }

                if (remoteBook.Books.Empty())
                {
                    throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse books in the release");
                }

                // Keep a matched pack intact when it includes the requested book.
                // A known match to another recording or author cannot become the
                // target simply because an older search row is grabbed.
                var bookId = cached.Scope.BookId ?? release.BookId;
                var authorId = cached.Scope.AuthorId ?? release.AuthorId;
                if ((bookId.HasValue && !remoteBook.Books.Any(book => book.Id == bookId.Value)) ||
                    (authorId.HasValue && remoteBook.Author.Id != authorId.Value))
                {
                    throw ContextChanged();
                }

                await _downloadService.DownloadReport(remoteBook, release.DownloadClientId);
            }
            catch (ReleaseDownloadException ex)
            {
                _logger.Error(ex, "Getting release from indexer failed");
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Getting release from indexer failed");
            }

            return Ok(release);
        }

        [HttpGet]
        public async Task<List<ReleaseResource>> GetReleases(int? bookId, int? authorId)
        {
            if (bookId.HasValue)
            {
                return await GetBookReleases(bookId.Value);
            }

            if (authorId.HasValue)
            {
                return await GetAuthorReleases(authorId.Value);
            }

            return await GetRss();
        }

        private async Task<List<ReleaseResource>> GetBookReleases(int bookId)
        {
            try
            {
                var decisions = await _releaseSearchService.BookSearch(bookId, true, true, true);
                var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

                return MapSearchDecisions(prioritizedDecisions, new SearchScope(bookId, null));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Book search failed");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<List<ReleaseResource>> GetAuthorReleases(int authorId)
        {
            try
            {
                var decisions = await _releaseSearchService.AuthorSearch(authorId, false, true, true);
                var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

                return MapSearchDecisions(prioritizedDecisions, new SearchScope(null, authorId));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Author search failed");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<List<ReleaseResource>> GetRss()
        {
            var reports = await _rssFetcherAndParser.Fetch();
            var decisions = _downloadDecisionMaker.GetRssDecision(reports);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

            return MapSearchDecisions(prioritizedDecisions, default);
        }

        private List<ReleaseResource> MapSearchDecisions(IEnumerable<DownloadDecision> decisions, SearchScope scope)
        {
            var resources = new List<ReleaseResource>();
            foreach (var decision in decisions)
            {
                var resource = MapDecision(decision, resources.Count);
                resource.SearchResultId = Guid.NewGuid().ToString("N");
                CacheRelease(resource, decision.RemoteBook, scope);
                resources.Add(resource);
            }

            return resources;
        }

        private void CacheRelease(ReleaseResource resource, RemoteBook remoteBook, SearchScope scope)
        {
            var key = GetCacheKey(resource);
            var now = DateTime.UtcNow;
            var result = new CachedRelease(resource.SearchResultId, scope, remoteBook.CopyForInteractiveGrab(), now + SearchResultLifetime);

            // CacheManager supplies the same cache to concurrent controllers.
            // Publish a new bucket so readers never see a partial update.
            lock (_remoteBookCache)
            {
                var previous = _remoteBookCache.Find(key);
                var entries = previous?.Entries ?? Array.Empty<CachedRelease>();
                var ambiguousScopes = new HashSet<SearchScope>(previous?.AmbiguousScopes ?? new HashSet<SearchScope>());
                var legacyAmbiguous = previous?.LegacyAmbiguous == true;

                foreach (var entry in entries)
                {
                    if (!SameDestination(entry, result))
                    {
                        legacyAmbiguous = true;
                        if (entry.Scope.Equals(scope))
                        {
                            ambiguousScopes.Add(scope);
                        }
                    }
                }

                _remoteBookCache.Set(key,
                    new CachedReleases(entries.Where(entry => entry.ExpiresAt > now).Append(result).ToArray(), legacyAmbiguous, ambiguousScopes),
                    SearchResultLifetime);
            }
        }

        private CachedRelease FindRelease(ReleaseResource release)
        {
            var bucket = _remoteBookCache.Find(GetCacheKey(release));
            if (bucket == null)
            {
                throw ResultExpired();
            }

            var now = DateTime.UtcNow;
            var entries = bucket.Entries.Where(entry => entry.ExpiresAt > now).ToList();
            var scope = new SearchScope(release.BookId, release.BookId.HasValue ? null : release.AuthorId);

            if (release.SearchResultId != null)
            {
                var exact = entries.SingleOrDefault(entry => entry.SearchResultId == release.SearchResultId);
                if (exact == null)
                {
                    throw ResultExpired();
                }

                if (!scope.IsRss && !exact.Scope.IsRss && !exact.Scope.Equals(scope))
                {
                    throw ContextChanged();
                }

                return exact;
            }

            // Older clients send only GUID/indexer, or an explicit target when
            // forcing an unknown result. Resolve those only when unambiguous.
            if (!scope.IsRss)
            {
                var scoped = entries.LastOrDefault(entry => entry.Scope.Equals(scope));
                if (scoped != null)
                {
                    if (bucket.AmbiguousScopes.Contains(scope))
                    {
                        throw ContextChanged();
                    }

                    return scoped;
                }
            }

            var latest = entries.LastOrDefault();
            if (latest == null)
            {
                throw ResultExpired();
            }

            if (bucket.LegacyAmbiguous || (!scope.IsRss && !latest.Scope.IsRss))
            {
                throw ContextChanged();
            }

            return latest;
        }

        private static bool SameDestination(CachedRelease left, CachedRelease right)
        {
            return left.Scope.Equals(right.Scope) && left.RemoteBook.Author?.Id == right.RemoteBook.Author?.Id &&
                   left.RemoteBook.Books.Select(book => book.Id).ToHashSet().SetEquals(right.RemoteBook.Books.Select(book => book.Id));
        }

        private static NzbDroneClientException ResultExpired() =>
            new NzbDroneClientException(HttpStatusCode.NotFound, "The search result is no longer available. Refresh the search and try again.");

        private static NzbDroneClientException ContextChanged() =>
            new NzbDroneClientException(HttpStatusCode.Conflict, "The release does not have an unambiguous match to this search. Refresh the search and review the recording.");

        private string GetCacheKey(ReleaseResource resource)
        {
            return string.Concat(resource.IndexerId, "_", resource.Guid);
        }

        private readonly struct SearchScope : IEquatable<SearchScope>
        {
            public SearchScope(int? bookId, int? authorId)
            {
                BookId = bookId;
                AuthorId = authorId;
            }

            public int? BookId { get; }
            public int? AuthorId { get; }
            public bool IsRss => !BookId.HasValue && !AuthorId.HasValue;

            public bool Equals(SearchScope other) => BookId == other.BookId && AuthorId == other.AuthorId;

            public override bool Equals(object obj) => obj is SearchScope other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(BookId, AuthorId);
        }

        private sealed class CachedRelease
        {
            public CachedRelease(string searchResultId, SearchScope scope, RemoteBook remoteBook, DateTime expiresAt)
            {
                SearchResultId = searchResultId;
                Scope = scope;
                RemoteBook = remoteBook;
                ExpiresAt = expiresAt;
            }

            public string SearchResultId { get; }
            public SearchScope Scope { get; }
            public RemoteBook RemoteBook { get; }
            public DateTime ExpiresAt { get; }
        }

        private sealed class CachedReleases
        {
            public CachedReleases(CachedRelease[] entries, bool legacyAmbiguous, HashSet<SearchScope> ambiguousScopes)
            {
                Entries = entries;
                LegacyAmbiguous = legacyAmbiguous;
                AmbiguousScopes = ambiguousScopes;
            }

            public CachedRelease[] Entries { get; }
            public bool LegacyAmbiguous { get; }
            public HashSet<SearchScope> AmbiguousScopes { get; }
        }
    }
}
