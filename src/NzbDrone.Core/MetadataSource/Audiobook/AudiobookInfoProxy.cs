using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Profiles.Metadata;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public class AudiobookInfoProxy : IProvideBookInfo, IProvideAuthorInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        private readonly IAudnexusProxy _audnexus;
        private readonly IAudibleCatalogProxy _audible;
        private readonly IConfigService _config;
        private readonly IAudiobookMetadataOverrideService _overrides;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMetadataProfileRepository _metadataProfiles;
        private readonly ICached<AuthorMetadata> _authors;
        private readonly ICached<MetadataLookup<AudnexusBookResource>> _details;
        private readonly ICached<MetadataLookup<AudiobookChapterMetadata>> _chapters;
        private readonly Logger _logger;

        public AudiobookInfoProxy(IAudnexusProxy audnexus,
            IAudibleCatalogProxy audible,
            IConfigService config,
            IAuthorService authorService,
            IBookService bookService,
            IEditionService editionService,
            ICacheManager cacheManager,
            IAudiobookMetadataOverrideService overrides,
            IMetadataProfileRepository metadataProfiles,
            Logger logger)
        {
            _logger = logger;
            _overrides = overrides;
            _audnexus = audnexus;
            _audible = audible;
            _config = config;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _metadataProfiles = metadataProfiles;
            _authors = cacheManager.GetCache<AuthorMetadata>(GetType(), "AudiobookAuthors");
            _details = cacheManager.GetCache<MetadataLookup<AudnexusBookResource>>(GetType(), "AudiobookRecordingDetails");
            _chapters = cacheManager.GetCache<MetadataLookup<AudiobookChapterMetadata>>(GetType(), "AudiobookRecordingChapters");
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime) => null;

        public List<object> SearchForNewEntity(string title)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var books = SearchForNewBook(title, null);
            var authors = books.Select(b => b.Author.Value).DistinctBy(a => a.ForeignAuthorId).ToList();
            if (!books.Any() && !string.IsNullOrWhiteSpace(title) && !title.Contains(':'))
            {
                authors = FindAuthors(title);
            }

            return authors.Cast<object>().Concat(books).ToList();
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var books = SearchForNewBook(title, null);
            return books.Any() ? books.Select(b => b.Author.Value).DistinctBy(a => a.ForeignAuthorId).ToList() : FindAuthors(title);
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(title))
            {
                return new List<Book>();
            }

            var query = title.Trim();
            if (AudiobookId.IsNative(query))
            {
                return Lookup(query);
            }

            if (query.StartsWith("asin:", StringComparison.OrdinalIgnoreCase))
            {
                var asin = query.Substring(5).Trim().ToUpperInvariant();
                return AudiobookId.IsAsin(asin) ? LookupAsin(asin) : new List<Book>();
            }

            if (query.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            {
                var value = query.Substring(7).Trim();
                if (AudiobookId.IsNative(value) || AudiobookId.IsAsin(value.ToUpperInvariant()))
                {
                    var id = AudiobookId.IsNative(value) ? value : AudiobookId.ForAsin(_config.AudnexusRegion, value.ToUpperInvariant());
                    return GetAuthorInfo(id).Books.Value;
                }

                query = value;
            }

            if (AudiobookId.IsAsin(query.ToUpperInvariant()))
            {
                return LookupAsin(query.ToUpperInvariant());
            }

            if (author.IsNotNullOrWhiteSpace())
            {
                query += " " + author.Trim();
            }

            return _audnexus.SearchBooks(query, _config.AudnexusRegion)
                .Where(AudiobookCatalogPolicy.IsRecording)
                .Select(resource => Map(resource, _config.AudnexusRegion, preferStoredSeries: true))
                .DistinctBy(book => book.ForeignBookId).ToList();
        }

        public List<Book> SearchByAsin(string asin) => SearchForNewBook("asin:" + asin, null);
        public List<Book> SearchByIsbn(string isbn) => SearchForNewBook(isbn, null);

        // Legacy file tags are not audiobook catalog identifiers.
        public List<Book> SearchByGoodreadsBookId(int goodreadsId, bool getAllEditions) => new List<Book>();

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var identity = AudiobookId.Parse(id);
            if (identity.IsName)
            {
                throw new BookInfoException("A recording requires an Audible ASIN.");
            }

            var resource = OptionalDetails(() => _audnexus.GetBook(identity.Value, identity.Region), id, "Audnexus");
            var primaryError = _details.Find(DetailKey(id, "Audnexus"))?.Error;
            ValidateRecordingIdentity(resource, identity.Value, identity.Region);
            if (resource != null && !AudiobookCatalogPolicy.IsRecording(resource))
            {
                throw new BookNotFoundException(id);
            }

            var hasAudnexusDetails = HasRequiredRecordingMetadata(resource);
            if (!hasAudnexusDetails && _config.AudibleRsEnabled)
            {
                var fallback = OptionalDetails(() => _audible.GetBook(identity.Value, identity.Region), id, "Audible");
                var fallbackError = _details.Find(DetailKey(id, "Audible"))?.Error;
                ValidateRecordingIdentity(fallback, identity.Value, identity.Region);
                resource = fallback ?? resource;
                if (!HasRequiredRecordingMetadata(resource) && (primaryError != null || fallbackError != null))
                {
                    throw new BookInfoException("Audiobook metadata providers are temporarily unavailable for this recording.", fallbackError ?? primaryError);
                }
            }
            else if (!hasAudnexusDetails && primaryError != null)
            {
                throw new BookInfoException("Unable to retrieve recording metadata from Audnexus.", primaryError);
            }

            if (!AudiobookCatalogPolicy.IsRecording(resource))
            {
                throw new BookNotFoundException(id);
            }

            if (!HasRequiredRecordingMetadata(resource))
            {
                throw new IncompleteRecordingMetadataException();
            }

            var book = Map(resource, identity.Region);
            if (hasAudnexusDetails && NeedsDetails(book) && _config.AudibleRsEnabled)
            {
                var details = OptionalDetails(() => _audible.GetBook(identity.Value, identity.Region), book.ForeignBookId, "Audible");
                if (details != null)
                {
                    MergeDetails(book, details, identity.Region, false);
                }
            }

            EnrichChapters(book, identity.Region);

            var authors = AudiobookContributors.All(resource).Where(credit => credit.Role == "Author")
                .Select(credit => MapAuthor(new AudnexusContributorResource { Asin = credit.Asin, Name = credit.Name, Role = credit.Role }, identity.Region))
                .Prepend(book.AuthorMetadata.Value)
                .DistinctBy(a => a.ForeignAuthorId).ToList();
            return Tuple.Create(book.AuthorMetadata.Value.ForeignAuthorId, book, authors);
        }

        public Author GetAuthorInfo(string id, bool useCache = true)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            id = _overrides.CanonicalAuthorId(id);
            var identity = AudiobookId.Parse(id);
            var local = _authorService.FindById(id);
            var profile = local == null ? null : _metadataProfiles.Get(local.MetadataProfileId);
            var metadata = local?.Metadata.Value ?? _authors.Find(id);
            if (metadata != null)
            {
                PreserveAuthorMetadata(metadata, _authors.Find(id));
            }

            if (!identity.IsName)
            {
                AudnexusAuthorResource authorProfile;
                try
                {
                    authorProfile = _audnexus.GetAuthor(identity.Value, identity.Region);
                }
                catch (BookInfoException) when (metadata != null && _config.AudibleRsEnabled)
                {
                    _logger.Debug("The known author profile is temporarily unavailable; retaining stored metadata for {0}.", id);
                    authorProfile = null;
                }

                if (authorProfile != null)
                {
                    var updated = MapAuthor(authorProfile, identity.Region);
                    PreserveAuthorMetadata(updated, metadata);
                    AudiobookAuthorIdentity.PreferDisplayName(updated, metadata, _overrides.FindAuthor(authorProfile, identity.Region));
                    metadata = updated;
                }
            }
            else
            {
                metadata ??= MapAuthor(new AudnexusContributorResource { Name = identity.Value }, identity.Region);
            }

            if (metadata == null)
            {
                throw new BookInfoException("No author profile was found for this ASIN. Search for an audiobook title by this author first.");
            }

            // Read every alias page before optional detail lookups, so a detail
            // outage cannot interrupt discovery of the rest of the bibliography.
            var resources = _overrides.AuthorSearchNames(metadata, identity.Region)
                .SelectMany(name => _config.AudibleRsEnabled
                    ? _audible.GetAuthorBooks(name, identity.Region)
                    : _audnexus.SearchBooks(name, identity.Region))
                .DistinctBy(resource => resource.Asin).ToList();
            var authorIdentity = _overrides.FindAuthor(new AudnexusContributorResource
            {
                Asin = identity.IsName ? null : identity.Value,
                Name = metadata.Name
            }, identity.Region);
            AudiobookAuthorIdentity.PreferDisplayName(metadata, local?.Metadata.Value, authorIdentity, resources);
            var discovered = resources.Where(AudiobookCatalogPolicy.IsRecording)
                .Where(resource => MatchesProfileLanguage(resource, profile))
                .Where(resource => MatchesAuthor(resource, identity.Region, metadata))
                .Select(resource => Enrich(resource, identity.Region, metadata))
                .Where(b => b.AuthorMetadata.Value.ForeignAuthorId == id).ToList();

            // Neither a capped search nor a regional catalog is an authoritative deletion feed.
            var known = local == null ? new List<Book>() : _bookService.GetBooksByAuthorMetadataId(local.AuthorMetadataId);
            foreach (var book in known)
            {
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                book.Editions = _editionService.GetEditionsByBook(book.Id);
            }

            var allBooks = discovered.Concat(known).DistinctBy(b => b.ForeignBookId).ToList();
            var result = new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = allBooks,
                Series = MergeSeries(allBooks)
            };
            if (local != null)
            {
                result.UseDbFieldsFrom(local);
                metadata.Id = local.AuthorMetadataId;
            }

            foreach (var book in allBooks)
            {
                book.Author = result;
                book.AuthorMetadata = metadata;
                book.AuthorMetadataId = metadata.Id;
            }

            RememberAuthor(metadata);
            return result;
        }

        private List<Book> Lookup(string id)
        {
            try
            {
                return new List<Book> { GetBookInfo(id).Item2 };
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
        }

        private List<Book> LookupAsin(string asin)
        {
            IncompleteRecordingMetadataException incomplete = null;
            foreach (var region in AudiobookId.LookupRegions(_config.AudnexusRegion))
            {
                try
                {
                    var books = Lookup(AudiobookId.ForAsin(region, asin));
                    if (books.Any())
                    {
                        return books;
                    }
                }
                catch (IncompleteRecordingMetadataException ex)
                {
                    // A skeletal regional product is not a verified recording.
                    // Other marketplaces can still identify this exact ASIN.
                    incomplete = ex;
                }
            }

            if (incomplete != null)
            {
                throw incomplete;
            }

            return new List<Book>();
        }

        private List<Author> FindAuthors(string name)
        {
            return _audnexus.SearchAuthors(name, _config.AudnexusRegion)
                .Where(a => a?.Name.IsNotNullOrWhiteSpace() == true)
                .Select(a =>
                {
                    var metadata = MapAuthor(a, _config.AudnexusRegion);
                    RememberAuthor(metadata);
                    return _authorService.FindById(metadata.ForeignAuthorId) ?? new Author
                    {
                        Metadata = metadata, Books = new List<Book>(), Series = new List<Series>()
                    };
                }).DistinctBy(a => a.ForeignAuthorId).ToList();
        }

        private Book Map(AudnexusBookResource resource, string region, AuthorMetadata expectedAuthor = null, bool preferStoredSeries = false)
        {
            var existing = resource == null ? null : _bookService.FindById(AudiobookId.ForAsin(region, resource.Asin));
            var contributor = AudiobookContributors.PrimaryAuthor(resource);
            var identity = contributor == null ? null : _overrides.FindAuthor(contributor, region);
            var establishedAuthor = existing?.AuthorMetadata.Value ?? expectedAuthor ?? FindEstablishedAuthor(contributor, region);
            var book = AudiobookMetadataMapper.Book(resource, region, establishedAuthor, identity);
            var metadata = book.AuthorMetadata.Value;
            if (existing?.AuthorMetadata?.Value != null &&
                _overrides.CanonicalAuthorId(existing.AuthorMetadata.Value.ForeignAuthorId) != _overrides.CanonicalAuthorId(metadata.ForeignAuthorId))
            {
                throw new BookInfoException("The recording's author credits conflict with its established owner.");
            }

            RememberAuthor(metadata);
            foreach (var credit in book.Editions.Value.SelectMany(edition => edition.Contributors)
                .Where(credit => credit.Role == "Author" &&
                    (AudiobookId.IsAsin(credit.Asin) ? !AudiobookId.Parse(metadata.ForeignAuthorId).IsName &&
                        credit.Asin == AudiobookId.Parse(metadata.ForeignAuthorId).Value :
                        new[] { metadata.Name }.Concat(metadata.Aliases).Contains(credit.Name, StringComparer.OrdinalIgnoreCase))))
            {
                credit.Name = metadata.Name;
            }

            if (existing != null)
            {
                book.UseDbFieldsFrom(existing);
                var editions = _editionService.GetEditionsByBook(existing.Id);
                AudiobookMetadataMerger.PreserveMissing(book, existing, editions, preferStoredSeries);
                foreach (var edition in book.Editions.Value)
                {
                    var previous = editions.SingleOrDefault(e => e.ForeignEditionId == edition.ForeignEditionId);
                    if (previous != null)
                    {
                        // Catalog descriptions can be extracts, even when they
                        // are not labelled as merchandising summaries.
                        var previousOverview = AudiobookSynopsis.Clean(previous.Overview);
                        if (previousOverview.IsNotNullOrWhiteSpace() && AudiobookSynopsis.Clean(resource.Summary).IsNullOrWhiteSpace() &&
                            (resource.DescriptionIsSummary || SynopsisLength(previous.Overview) > SynopsisLength(edition.Overview)))
                        {
                            edition.Overview = previousOverview;
                        }

                        edition.UseDbFieldsFrom(previous);
                    }
                }
            }

            var author = _authorService.FindById(metadata.ForeignAuthorId);
            if (author != null)
            {
                metadata.Id = author.AuthorMetadataId;
                book.Author = author;
                book.AuthorMetadataId = author.AuthorMetadataId;
            }

            return book;
        }

        private AuthorMetadata FindEstablishedAuthor(AudnexusContributorResource contributor, string region)
        {
            if (!AudiobookId.IsAsin(contributor?.Asin))
            {
                return null;
            }

            var sameMarketplace = _authorService.FindById(AudiobookId.ForAsin(region, contributor.Asin));
            if (sameMarketplace != null)
            {
                return sameMarketplace.Metadata.Value;
            }

            // A regional recording can belong to an established author whose
            // identical contributor ASIN was added in a different marketplace.
            // Two already separate owners are ambiguous; never choose by name.
            var existing = AudiobookId.LookupRegions(_config.AudnexusRegion).Where(candidate => candidate != region)
                .Select(candidate => _authorService.FindById(AudiobookId.ForAsin(candidate, contributor.Asin)))
                .Where(author => author != null).DistinctBy(author => author.ForeignAuthorId).ToList();
            return existing.Count == 1 ? existing[0].Metadata.Value : null;
        }

        private bool MatchesAuthor(AudnexusBookResource resource, string region, AuthorMetadata expectedAuthor)
        {
            var contributors = AudiobookContributors.All(resource).Where(credit => credit.Role == "Author")
                .Select(credit => new AudnexusContributorResource { Asin = credit.Asin, Name = credit.Name, Role = credit.Role }).ToList();
            if (!contributors.Any())
            {
                throw new BookInfoException("Audiobook metadata is missing its author.");
            }

            var expectedIdentity = AudiobookId.Parse(expectedAuthor.ForeignAuthorId);
            if (contributors.Any(contributor => MapAuthor(contributor, region).ForeignAuthorId == expectedAuthor.ForeignAuthorId ||
                (!expectedIdentity.IsName && contributor.Asin == expectedIdentity.Value)))
            {
                return true;
            }

            // A catalog's name query is not proof of identity: homonyms with an
            // explicit, conflicting contributor ASIN must remain separate.
            return contributors.Any(contributor => !AudiobookId.IsAsin(contributor.Asin) &&
                new[] { expectedAuthor.Name }.Concat(expectedAuthor.Aliases).Any(name =>
                    string.Equals(AudiobookTextNormalizer.Text(name), AudiobookTextNormalizer.Text(contributor.Name), StringComparison.OrdinalIgnoreCase)));
        }

        private Book Enrich(AudnexusBookResource resource, string region, AuthorMetadata expectedAuthor = null)
        {
            var book = Map(resource, region, expectedAuthor, true);

            // A catalog can look complete while omitting another series, genres or
            // a fuller synopsis. Revisit details periodically even for rich records.
            // Cache absent optional fields and 404s as well as successful details.
            var details = OptionalDetails(() => _audnexus.GetBook(resource.Asin, region), book.ForeignBookId, "Audnexus");
            if (details != null)
            {
                MergeDetails(book, details, region, true);
            }

            if (NeedsDetails(book) && _config.AudibleRsEnabled)
            {
                details = OptionalDetails(() => _audible.GetBook(resource.Asin, region), book.ForeignBookId, "Audible");
                if (details != null)
                {
                    MergeDetails(book, details, region, false);
                }
            }

            EnrichChapters(book, region);
            return book;
        }

        private AudnexusBookResource OptionalDetails(Func<AudnexusBookResource> lookup, string recordingId, string source)
            => OptionalMetadata(lookup, recordingId, source, _details);

        private T OptionalMetadata<T>(Func<T> lookup, string recordingId, string source, ICached<MetadataLookup<T>> cache)
            where T : class
        {
            var cancellationToken = CommandExecutionContext.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var key = DetailKey(recordingId, source);
            var cached = cache.Find(key);
            if (cached != null)
            {
                return cached.Value;
            }

            try
            {
                var details = lookup();
                cancellationToken.ThrowIfCancellationRequested();
                cache.Set(key, new MetadataLookup<T> { Value = details }, TimeSpan.FromDays(1));
                return details;
            }
            catch (BookInfoException ex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // An optional enrichment outage must not discard a successful
                // author catalog or its stored metadata. Identity checks below
                // remain strict for every detail response that was received.
                _logger.Debug("Additional metadata for recording {0} is temporarily unavailable.", recordingId);
                cache.Set(key, new MetadataLookup<T> { Error = ex }, TimeSpan.FromMinutes(1));
                return null;
            }
        }

        private void EnrichChapters(Book book, string region)
        {
            var asin = AudiobookId.Parse(book.ForeignBookId).Value;
            var metadata = OptionalMetadata(
                () => AudiobookMetadataMapper.Chapters(_audnexus.GetChapters(asin, region), asin, region),
                book.ForeignBookId,
                "Audnexus",
                _chapters);
            if (metadata != null)
            {
                var edition = book.Editions.Value.Single();
                edition.ChapterMetadata = metadata.JsonClone();
                if (edition.RuntimeLengthMinutes.GetValueOrDefault() <= 0 && metadata.RuntimeLengthMs > 0 &&
                    metadata.RuntimeLengthMs / 60000 <= int.MaxValue)
                {
                    edition.RuntimeLengthMinutes = Math.Max(1, (int)(metadata.RuntimeLengthMs.Value / 60000));
                }
            }
        }

        private string DetailKey(string recordingId, string source) => source + ":" +
            (source == "Audnexus" ? _config.AudnexusUrl : _config.AudibleRsUrl + ":" + _config.AudibleRsAccount) + ":" + recordingId;

        private void MergeDetails(Book book, AudnexusBookResource details, string region, bool preferSeries)
        {
            if (details.Asin != AudiobookId.Parse(book.ForeignBookId).Value || details.Region != region ||
                !AudiobookCatalogPolicy.IsRecording(details))
            {
                throw new BookInfoException("Audiobook details do not match this recording, marketplace, or author.");
            }

            if (!HasRequiredRecordingMetadata(details))
            {
                return;
            }

            if (!MatchesAuthor(details, region, book.AuthorMetadata.Value))
            {
                throw new BookInfoException("Audiobook details do not match this recording, marketplace, or author.");
            }

            var detailed = Map(details, region, book.AuthorMetadata.Value);
            AudiobookMetadataMerger.PreserveMissing(book, detailed, detailed.Editions.Value, preferSeries);
            var edition = book.Editions.Value.Single();
            var detailedOverview = detailed.Editions.Value.Single().Overview;
            if (!details.DescriptionIsSummary &&
                (AudiobookSynopsis.Clean(details.Summary).IsNotNullOrWhiteSpace() || SynopsisLength(detailedOverview) > SynopsisLength(edition.Overview)))
            {
                edition.Overview = detailedOverview;
            }
        }

        private static int SynopsisLength(string value) => AudiobookSynopsis.Clean(value)?.Length ?? 0;

        private static bool HasRequiredRecordingMetadata(AudnexusBookResource resource) =>
            AudiobookId.IsAsin(resource?.Asin) && resource.Title.IsNotNullOrWhiteSpace() &&
            AudiobookContributors.PrimaryAuthor(resource) != null;

        private static void ValidateRecordingIdentity(AudnexusBookResource resource, string asin, string region)
        {
            if (resource != null && (resource.Asin != asin || resource.Region != region))
            {
                throw new BookInfoException("The metadata response does not match this recording or marketplace.");
            }
        }

        private sealed class IncompleteRecordingMetadataException : BookInfoException
        {
            public IncompleteRecordingMetadataException()
                : base("Recording metadata is incomplete in this marketplace; a title and author are required to identify it.")
            {
            }
        }

        private static bool NeedsDetails(Book book)
        {
            var edition = book.Editions.Value.Single();
            return edition.Overview.IsNullOrWhiteSpace() || edition.Narrators?.Any() != true ||
                book.Genres?.Any() != true || AudiobookCoverSelector.NeedsEnrichment(edition.Images) ||
                edition.Publisher.IsNullOrWhiteSpace() || edition.Language.IsNullOrWhiteSpace() ||
                edition.RuntimeLengthMinutes.GetValueOrDefault() <= 0 || edition.Isbn13.IsNullOrWhiteSpace() ||
                edition.Subtitle.IsNullOrWhiteSpace() || book.SeriesLinks?.Value?.Any() != true;
        }

        private sealed class MetadataLookup<T>
        {
            public T Value { get; set; }
            public BookInfoException Error { get; set; }
        }

        private AuthorMetadata MapAuthor(AudnexusContributorResource contributor, string region) =>
            AudiobookMetadataMapper.Author(contributor, region, _overrides.FindAuthor(contributor, region));

        private static bool MatchesProfileLanguage(AudnexusBookResource resource, MetadataProfile profile)
        {
            if (profile?.AllowedLanguages.IsNullOrWhiteSpace() != false)
            {
                return true;
            }

            var allowed = profile.AllowedLanguages.Split(',')
                .Select(value => value.Trim() == "null" ? null : AudiobookTextNormalizer.Language(value));
            return allowed.Contains(AudiobookTextNormalizer.Language(resource.Language));
        }

        private void RememberAuthor(AuthorMetadata metadata)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var previous = _authors.Find(metadata.ForeignAuthorId) ?? _authorService.FindById(metadata.ForeignAuthorId)?.Metadata.Value;
            PreserveAuthorMetadata(metadata, _authors.Find(metadata.ForeignAuthorId));
            PreserveAuthorMetadata(metadata, _authorService.FindById(metadata.ForeignAuthorId)?.Metadata.Value);
            var parsed = AudiobookId.Parse(metadata.ForeignAuthorId);
            AudiobookAuthorIdentity.PreferDisplayName(metadata, previous, _overrides.FindAuthor(new AudnexusContributorResource
            {
                Asin = parsed.IsName ? null : parsed.Value,
                Name = metadata.Name
            }, parsed.Region));
            _authors.Set(metadata.ForeignAuthorId, metadata, TimeSpan.FromHours(1));
        }

        private static void PreserveAuthorMetadata(AuthorMetadata target, AuthorMetadata previous)
        {
            if (previous == null || target.ForeignAuthorId != previous.ForeignAuthorId)
            {
                return;
            }

            AudiobookAuthorIdentity.PreserveNames(target, previous);
            if (target.Overview.IsNullOrWhiteSpace())
            {
                target.Overview = previous.Overview;
            }

            if (target.Images?.Any() != true)
            {
                target.Images = previous.Images;
            }

            target.Genres = AudiobookTextNormalizer.Names((target.Genres ?? new List<string>()).Concat(previous.Genres ?? new List<string>()));
            target.SimilarAuthors = AudiobookMetadataMerger.MergePeopleReferences(target.SimilarAuthors, previous.SimilarAuthors);
            target.Links = (target.Links ?? new List<Links>()).Concat(previous.Links ?? new List<Links>())
                .DistinctBy(link => link.Url).ToList();
        }

        private static List<Series> MergeSeries(List<Book> books)
        {
            var links = books.SelectMany(b => b.SeriesLinks?.Value ?? new List<SeriesBookLink>()).ToList();
            return links.GroupBy(l => l.Series.Value.ForeignSeriesId).Select(group =>
            {
                var series = group.First().Series.Value;
                series.LinkItems = group.ToList();
                series.WorkCount = group.Count();
                series.PrimaryWorkCount = group.Count(l => l.IsPrimary);
                foreach (var link in group)
                {
                    link.Series = series;
                }

                return series;
            }).ToList();
        }
    }
}
