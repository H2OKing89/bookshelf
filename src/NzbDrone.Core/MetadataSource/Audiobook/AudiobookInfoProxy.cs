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
        private readonly IMetadataProviderStatusService _status;
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
            IMetadataProviderStatusService status,
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
            _status = status;
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

            return MetadataProviderObservation.Observe(_status,
                MetadataProvider.Audnexus,
                MetadataProviderOperation.RecordingRefresh,
                observation => ReadBookInfo(id, identity, observation));
        }

        private Tuple<string, Book, List<AuthorMetadata>> ReadBookInfo(string id,
                                                                     (string Region, string Value, bool IsName) identity,
                                                                     MetadataProviderObservation observation)
        {
            var resource = OptionalDetails(() => _audnexus.GetBook(identity.Value, identity.Region), id, MetadataProvider.Audnexus, observation);
            var primaryError = _details.Find(DetailKey(id, MetadataProvider.Audnexus))?.Error;
            ValidateRecordingIdentity(resource, identity.Value, identity.Region);
            if (resource != null && !AudiobookCatalogPolicy.IsRecording(resource))
            {
                throw new BookNotFoundException(id);
            }

            var hasAudnexusDetails = HasRequiredRecordingMetadata(resource);
            if (!hasAudnexusDetails && _config.AudibleRsEnabled)
            {
                var fallback = OptionalDetails(() => _audible.GetBook(identity.Value, identity.Region), id, MetadataProvider.Audible, observation);
                var fallbackError = _details.Find(DetailKey(id, MetadataProvider.Audible))?.Error;
                ValidateRecordingIdentity(fallback, identity.Value, identity.Region);
                if (fallback != null || fallbackError != null)
                {
                    observation.UseProvider(MetadataProvider.Audible);
                }

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
            ObserveSparseRecording(resource, observation);
            if (hasAudnexusDetails && NeedsDetails(book) && _config.AudibleRsEnabled)
            {
                var details = OptionalDetails(() => _audible.GetBook(identity.Value, identity.Region), book.ForeignBookId, MetadataProvider.Audible, observation);
                if (details != null)
                {
                    MergeDetails(book, details, identity.Region, false, observation);
                }
            }

            EnrichChapters(book, identity.Region, observation);

            // A known recording can now carry an explicit ASIN for its legacy
            // name-only owner. Keep that single owner until the author refresh
            // verifies and applies the identity upgrade; do not upsert a second
            // metadata row as a side effect of refreshing an individual book.
            var legacyCredit = AudiobookId.Parse(book.AuthorMetadata.Value.ForeignAuthorId).IsName
                ? VerifiedLegacyCredit(resource, identity.Region, _bookService.FindById(id), book.AuthorMetadata.Value)
                : null;
            var authors = AudiobookContributors.All(resource).Where(credit => credit.Role == "Author" && (legacyCredit == null || credit.Asin != legacyCredit.Asin))
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
            var provider = _config.AudibleRsEnabled ? MetadataProvider.Audible : MetadataProvider.Audnexus;
            return MetadataProviderObservation.Observe(_status,
                provider,
                MetadataProviderOperation.AuthorRefresh,
                observation => ReadAuthorInfo(id, identity, provider, observation));
        }

        private Author ReadAuthorInfo(string id,
                                      (string Region, string Value, bool IsName) identity,
                                      MetadataProvider provider,
                                      MetadataProviderObservation observation)
        {
            var local = _authorService.FindById(id);
            var profile = local == null ? null : _metadataProfiles.Get(local.MetadataProfileId);
            var metadata = local?.Metadata.Value ?? _authors.Find(id);
            if (identity.IsName && local != null)
            {
                metadata = metadata.JsonClone();
            }

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
                catch (BookInfoException error) when (metadata != null && provider == MetadataProvider.Audible)
                {
                    ObserveOptionalError(error, observation);
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
                else if (metadata != null)
                {
                    observation.Partial(MetadataProviderReason.ConfirmedMissing);
                }
            }
            else
            {
                metadata ??= MapAuthor(new AudnexusContributorResource { Name = identity.Value }, identity.Region);
            }

            if (metadata == null)
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Missing, MetadataProviderReason.ConfirmedMissing),
                    "No author profile was found for this ASIN. Search for an audiobook title by this author first.");
            }

            // Read every alias page before optional detail lookups, so a detail
            // outage cannot interrupt discovery of the rest of the bibliography.
            var catalog = _overrides.AuthorSearchNames(metadata, identity.Region)
                .SelectMany(name => provider == MetadataProvider.Audible
                    ? _audible.GetAuthorBooks(name, identity.Region)
                    : _audnexus.SearchBooks(name, identity.Region))
                .ToList();
            var resources = catalog.DistinctBy(resource => resource.Asin).ToList();
            var authorIdentity = _overrides.FindAuthor(new AudnexusContributorResource
            {
                Asin = identity.IsName ? null : identity.Value,
                Name = metadata.Name
            }, identity.Region);
            AudiobookAuthorIdentity.PreferDisplayName(metadata, local?.Metadata.Value, authorIdentity, resources);

            // Neither a capped search nor a regional catalog is an authoritative deletion feed.
            var known = local == null ? new List<Book>() : _bookService.GetBooksByAuthorMetadataId(local.AuthorMetadataId);
            var promotion = FindLegacyPromotion(local, metadata, identity.Region, catalog, known);
            if (promotion != null)
            {
                metadata = promotion.Metadata;
                id = metadata.ForeignAuthorId;
            }

            var knownIds = known.Select(book => book.ForeignBookId).ToHashSet();
            var discovered = new List<Book>();
            foreach (var resource in resources.Where(AudiobookCatalogPolicy.IsRecording).Where(resource => MatchesProfileLanguage(resource, profile)))
            {
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                if (!MatchesAuthor(resource, identity.Region, metadata))
                {
                    if (knownIds.Contains(AudiobookId.ForAsin(identity.Region, resource.Asin)))
                    {
                        observation.Flag(MetadataProviderState.IdentityReview, MetadataProviderReason.IdentityConflict);
                    }

                    continue;
                }

                var book = Enrich(resource, identity.Region, metadata, observation, promotion);
                if (book.AuthorMetadata.Value.ForeignAuthorId == id)
                {
                    discovered.Add(book);
                }
            }

            if (provider == MetadataProvider.Audnexus)
            {
                observation.Partial(MetadataProviderReason.KeywordCoverage);
            }

            if (promotion != null)
            {
                // The catalog response is a proposal, not a write. In particular
                // don't reparent model instances that were read from the library.
                known = known.Select(CopyKnownBook).ToList();
            }

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
                Series = MergeSeries(allBooks),
                IdentityPromotionFrom = promotion?.OldId,
                IdentityPromotionRecordingId = promotion?.RecordingId
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

            if (promotion == null)
            {
                RememberAuthor(metadata);
            }

            return result;
        }

        private sealed class LegacyAuthorPromotion
        {
            public LegacyAuthorPromotion(string oldId, string recordingId, AuthorMetadata metadata)
            {
                OldId = oldId;
                RecordingId = recordingId;
                Metadata = metadata;
            }

            public string OldId { get; }
            public string RecordingId { get; }
            public AuthorMetadata Metadata { get; }
        }

        private static bool MatchesLegacyName(AuthorMetadata metadata, string name)
        {
            return new[] { metadata.Name }.Concat(metadata.Aliases ?? new List<string>())
                .Any(alias => string.Equals(AudiobookTextNormalizer.Text(alias), AudiobookTextNormalizer.Text(name), StringComparison.OrdinalIgnoreCase));
        }

        // A name search is not identity evidence. Only an exact, locally stored
        // recording (including its edition and marketplace) can supply an anchor.
        private bool IsStoredRecording(AudnexusBookResource resource, string region, Book book, AuthorMetadata owner)
        {
            if (book == null || owner == null || owner.Id <= 0 || book.Id <= 0 || book.AuthorMetadataId != owner.Id ||
                !AudiobookCatalogPolicy.IsRecording(resource) || resource.Region != region || !AudiobookId.IsAsin(resource.Asin) ||
                book.ForeignBookId != AudiobookId.ForAsin(region, resource.Asin))
            {
                return false;
            }

            return (_editionService.GetEditionsByBook(book.Id) ?? new List<Edition>()).Any(edition => edition.Id > 0 && edition.BookId == book.Id &&
                edition.ForeignEditionId == book.ForeignBookId && edition.Asin == resource.Asin && edition.AudibleRegion == region);
        }

        private static AudnexusContributorResource UniqueLegacyCredit(AudnexusBookResource resource, AuthorMetadata owner)
        {
            var primary = AudiobookContributors.PrimaryAuthor(resource);
            if (primary == null || !AudiobookId.IsAsin(primary.Asin) || !MatchesLegacyName(owner, primary.Name))
            {
                return null;
            }

            var matchingAsins = AudiobookContributors.All(resource)
                .Where(credit => credit.Role == "Author" && AudiobookId.IsAsin(credit.Asin) && MatchesLegacyName(owner, credit.Name))
                .Select(credit => credit.Asin).Distinct().ToList();
            return matchingAsins.Count == 1 && matchingAsins[0] == primary.Asin ? primary : null;
        }

        private bool HasOtherAuthorOwner(string asin, string region, AuthorMetadata owner)
        {
            return AudiobookId.LookupRegions(region).Select(market => _authorService.FindById(AudiobookId.ForAsin(market, asin)))
                .Any(author => author != null && author.AuthorMetadataId != owner.Id);
        }

        private bool StoredCreditsAgree(Book book, AuthorMetadata owner, string asin)
        {
            return CreditsAgree((_editionService.GetEditionsByBook(book.Id) ?? new List<Edition>())
                .Where(edition => edition.ForeignEditionId == book.ForeignBookId)
                .SelectMany(edition => edition.Contributors ?? new List<AudiobookContributor>()), owner, asin);
        }

        private static bool CreditsAgree(IEnumerable<AudiobookContributor> credits, AuthorMetadata owner, string asin)
        {
            return credits.Where(credit => credit?.Role == "Author" && AudiobookId.IsAsin(credit.Asin) && MatchesLegacyName(owner, credit.Name))
                .All(credit => credit.Asin == asin);
        }

        private AudnexusContributorResource VerifiedLegacyCredit(AudnexusBookResource resource, string region, Book book, AuthorMetadata owner)
        {
            if (owner == null || !AudiobookId.IsNative(owner.ForeignAuthorId))
            {
                return null;
            }

            var identity = AudiobookId.Parse(owner.ForeignAuthorId);
            if (!identity.IsName || identity.Region != region || !IsStoredRecording(resource, region, book, owner))
            {
                return null;
            }

            var credit = UniqueLegacyCredit(resource, owner);
            return credit != null && StoredCreditsAgree(book, owner, credit.Asin) && !HasOtherAuthorOwner(credit.Asin, region, owner) &&
                _overrides.CanonicalAuthorId(AudiobookId.ForAsin(region, credit.Asin)) == AudiobookId.ForAsin(region, credit.Asin) ? credit : null;
        }

        private LegacyAuthorPromotion FindLegacyPromotion(Author local, AuthorMetadata metadata, string region, List<AudnexusBookResource> catalog, List<Book> known)
        {
            if (local == null || local.Id <= 0 || local.AuthorMetadataId <= 0 || !AudiobookId.IsNative(local.ForeignAuthorId))
            {
                return null;
            }

            var legacy = AudiobookId.Parse(local.ForeignAuthorId);
            if (!legacy.IsName || legacy.Region != region)
            {
                return null;
            }

            // Read all returned copies of a known recording, including copies
            // returned through different aliases. Conflicting positive evidence
            // is not resolved by taking the first provider result.
            var anchors = new List<(AudnexusContributorResource Credit, string RecordingId)>();
            foreach (var resource in catalog.Where(resource => AudiobookCatalogPolicy.IsRecording(resource) && resource.Region == region))
            {
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                var recordingId = AudiobookId.ForAsin(region, resource.Asin);
                var book = known.FirstOrDefault(item => item.ForeignBookId == recordingId);
                if (!IsStoredRecording(resource, region, book, metadata))
                {
                    continue;
                }

                var primary = AudiobookContributors.PrimaryAuthor(resource);
                if (primary != null && !AudiobookId.IsAsin(primary.Asin) && MatchesLegacyName(metadata, primary.Name))
                {
                    if (AudiobookContributors.All(resource).Any(credit => credit.Role == "Author" && AudiobookId.IsAsin(credit.Asin) && MatchesLegacyName(metadata, credit.Name)))
                    {
                        return null;
                    }

                    // Still name-only: this record neither confirms nor disproves
                    // the new ASIN. Do not treat absence of an ID as evidence.
                    continue;
                }

                var credit = UniqueLegacyCredit(resource, metadata);
                if (credit == null)
                {
                    return null;
                }

                anchors.Add((credit, recordingId));
            }

            if (anchors.Count == 0 || anchors.Select(anchor => anchor.Credit.Asin).Distinct().Count() != 1)
            {
                return null;
            }

            var evidence = anchors[0];
            var canonicalId = AudiobookId.ForAsin(region, evidence.Credit.Asin);
            if (HasOtherAuthorOwner(evidence.Credit.Asin, region, metadata) || _overrides.CanonicalAuthorId(canonicalId) != canonicalId ||
                known.Where(book => book.Id > 0 && book.AuthorMetadataId == metadata.Id && book.ForeignBookId?.StartsWith($"audible:{region}:", StringComparison.Ordinal) == true)
                    .Any(book => !StoredCreditsAgree(book, metadata, evidence.Credit.Asin)))
            {
                return null;
            }

            var promoted = metadata.JsonClone();
            promoted.ForeignAuthorId = canonicalId;
            promoted.Links = (promoted.Links ?? new List<Links>()).Concat(MapAuthor(evidence.Credit, region).Links).DistinctBy(link => link.Url).ToList();
            return new LegacyAuthorPromotion(local.ForeignAuthorId, evidence.RecordingId, promoted);
        }

        private static Book CopyKnownBook(Book original)
        {
            var copy = new Book { ImportListOriginId = original.ImportListOriginId, BookFiles = original.BookFiles };
            copy.UseMetadataFrom(original);
            copy.UseDbFieldsFrom(original);
            copy.SeriesLinks = (original.SeriesLinks?.Value ?? new List<SeriesBookLink>()).Select(link =>
            {
                var series = new Series { ForeignAuthorId = link.Series.Value.ForeignAuthorId };
                series.UseMetadataFrom(link.Series.Value);
                series.UseDbFieldsFrom(link.Series.Value);
                var result = new SeriesBookLink { Series = series, Book = copy };
                result.UseMetadataFrom(link);
                result.UseDbFieldsFrom(link);
                return result;
            }).ToList();
            return copy;
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

        private Book Map(AudnexusBookResource resource, string region, AuthorMetadata expectedAuthor = null, bool preferStoredSeries = false, LegacyAuthorPromotion promotion = null)
        {
            var existing = resource == null ? null : _bookService.FindById(AudiobookId.ForAsin(region, resource.Asin));
            var contributor = AudiobookContributors.PrimaryAuthor(resource);
            var identity = contributor == null ? null : _overrides.FindAuthor(contributor, region);
            var establishedAuthor = existing?.AuthorMetadata.Value ?? expectedAuthor ?? FindEstablishedAuthor(contributor, region);
            var promotingOwner = promotion != null && existing?.AuthorMetadata.Value?.ForeignAuthorId == promotion.OldId &&
                existing.AuthorMetadataId == promotion.Metadata.Id;
            if (promotingOwner)
            {
                establishedAuthor = promotion.Metadata;
            }

            var retainLegacyOwner = promotion == null && VerifiedLegacyCredit(resource, region, existing, establishedAuthor) != null;
            var book = AudiobookMetadataMapper.Book(resource, region, establishedAuthor, identity, retainVerifiedOwner: retainLegacyOwner);
            var metadata = book.AuthorMetadata.Value;
            if (existing?.AuthorMetadata?.Value != null &&
                _overrides.CanonicalAuthorId(existing.AuthorMetadata.Value.ForeignAuthorId) != _overrides.CanonicalAuthorId(metadata.ForeignAuthorId) &&
                !(promotingOwner && metadata.ForeignAuthorId == promotion.Metadata.ForeignAuthorId))
            {
                throw IdentityConflict("The recording's author credits conflict with its established owner.");
            }

            if (promotion == null)
            {
                RememberAuthor(metadata);
            }

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
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Partial, MetadataProviderReason.SparseMetadata),
                    "Audiobook metadata is missing its author.");
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

        private Book Enrich(AudnexusBookResource resource, string region, AuthorMetadata expectedAuthor, MetadataProviderObservation observation, LegacyAuthorPromotion promotion = null)
        {
            var book = Map(resource, region, expectedAuthor, true, promotion);

            // A catalog can look complete while omitting another series, genres or
            // a fuller synopsis. Revisit details periodically even for rich records.
            // Cache absent optional fields and 404s as well as successful details.
            var details = OptionalDetails(() => _audnexus.GetBook(resource.Asin, region), book.ForeignBookId, MetadataProvider.Audnexus, observation);
            if (details != null)
            {
                MergeDetails(book, details, region, true, observation, promotion);
            }

            if (NeedsDetails(book) && _config.AudibleRsEnabled)
            {
                details = OptionalDetails(() => _audible.GetBook(resource.Asin, region), book.ForeignBookId, MetadataProvider.Audible, observation);
                if (details != null)
                {
                    MergeDetails(book, details, region, false, observation, promotion);
                }
            }

            ObserveSparseRecording(resource, observation);
            EnrichChapters(book, region, observation);
            return book;
        }

        private AudnexusBookResource OptionalDetails(Func<AudnexusBookResource> lookup,
                                                    string recordingId,
                                                    MetadataProvider provider,
                                                    MetadataProviderObservation observation)
            => OptionalMetadata(lookup, recordingId, provider, MetadataProviderOperation.RecordingDetails, _details, observation);

        private T OptionalMetadata<T>(Func<T> lookup,
                                      string recordingId,
                                      MetadataProvider provider,
                                      MetadataProviderOperation operation,
                                      ICached<MetadataLookup<T>> cache,
                                      MetadataProviderObservation observation)
            where T : class
        {
            var cancellationToken = CommandExecutionContext.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var key = DetailKey(recordingId, provider);
            var cached = cache.Find(key);
            if (cached != null)
            {
                if (cached.Error != null)
                {
                    ObserveOptionalError(cached.Error, observation);
                }
                else if (cached.Value != null)
                {
                    observation.UseCached();
                    _status.Report(new MetadataProviderStatus(provider, operation, MetadataProviderState.Cached, null, DateTime.UtcNow, null, null));
                }

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
                ObserveOptionalError(ex, observation);

                // An optional enrichment outage must not discard a successful
                // author catalog or its stored metadata. Identity checks below
                // remain strict for every detail response that was received.
                _logger.Debug("Additional metadata for recording {0} is temporarily unavailable.", recordingId);
                cache.Set(key, new MetadataLookup<T> { Error = ex }, TimeSpan.FromMinutes(1));
                return null;
            }
        }

        private static void ObserveOptionalError(BookInfoException error, MetadataProviderObservation observation)
        {
            if (error.ProviderIssue?.State == MetadataProviderState.IdentityReview)
            {
                observation.Flag(MetadataProviderState.IdentityReview, MetadataProviderReason.IdentityConflict);
            }
            else
            {
                observation.Partial(error.ProviderIssue?.Reason ?? MetadataProviderReason.Transport);
            }
        }

        private void EnrichChapters(Book book, string region, MetadataProviderObservation observation)
        {
            var asin = AudiobookId.Parse(book.ForeignBookId).Value;
            var metadata = OptionalMetadata(
                () => AudiobookMetadataMapper.Chapters(_audnexus.GetChapters(asin, region), asin, region),
                book.ForeignBookId,
                MetadataProvider.Audnexus,
                MetadataProviderOperation.Chapters,
                _chapters,
                observation);
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

        private string DetailKey(string recordingId, MetadataProvider provider) => provider + ":" +
            (provider == MetadataProvider.Audnexus ? _config.AudnexusUrl : _config.AudibleRsUrl + ":" + _config.AudibleRsAccount) + ":" + recordingId;

        private void MergeDetails(Book book, AudnexusBookResource details, string region, bool preferSeries, MetadataProviderObservation observation, LegacyAuthorPromotion promotion = null)
        {
            if (details.Asin != AudiobookId.Parse(book.ForeignBookId).Value || details.Region != region ||
                !AudiobookCatalogPolicy.IsRecording(details))
            {
                throw IdentityConflict("Audiobook details do not match this recording, marketplace, or author.");
            }

            if (!HasRequiredRecordingMetadata(details))
            {
                observation.Partial(MetadataProviderReason.SparseMetadata);
                return;
            }

            var owner = book.AuthorMetadata.Value;
            var matches = MatchesAuthor(details, region, owner);
            if (AudiobookId.Parse(owner.ForeignAuthorId).IsName && AudiobookContributors.All(details)
                .Any(credit => credit.Role == "Author" && AudiobookId.IsAsin(credit.Asin) && MatchesLegacyName(owner, credit.Name)))
            {
                var verified = VerifiedLegacyCredit(details, region, _bookService.FindById(book.ForeignBookId), owner);
                matches = verified != null && CreditsAgree(book.Editions.Value.SelectMany(edition => edition.Contributors), owner, verified.Asin);
            }

            if (!matches)
            {
                throw IdentityConflict("Audiobook details do not match this recording, marketplace, or author.");
            }

            var detailed = Map(details, region, book.AuthorMetadata.Value, promotion: promotion);
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

        private static void ObserveSparseRecording(AudnexusBookResource resource, MetadataProviderObservation observation)
        {
            if (resource.RuntimeLengthMin.GetValueOrDefault() <= 0 ||
                (resource.Description.IsNullOrWhiteSpace() && resource.Summary.IsNullOrWhiteSpace()) ||
                resource.Image.IsNullOrWhiteSpace())
            {
                observation.Partial(MetadataProviderReason.SparseMetadata);
            }
        }

        private static BookInfoException IdentityConflict(string message) =>
            new BookInfoException(new MetadataProviderIssue(MetadataProviderState.IdentityReview, MetadataProviderReason.IdentityConflict), message);

        private static void ValidateRecordingIdentity(AudnexusBookResource resource, string asin, string region)
        {
            if (resource != null && (resource.Asin != asin || resource.Region != region))
            {
                throw IdentityConflict("The metadata response does not match this recording or marketplace.");
            }
        }

        private sealed class IncompleteRecordingMetadataException : BookInfoException
        {
            public IncompleteRecordingMetadataException()
                : base(new MetadataProviderIssue(MetadataProviderState.Partial, MetadataProviderReason.SparseMetadata),
                    "Recording metadata is incomplete in this marketplace; a title and author are required to identify it.")
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

            // A verified name-to-ASIN promotion retains the old route slug.
            // Subsequent canonical profile refreshes must not change that route.
            if (previous.TitleSlug.IsNotNullOrWhiteSpace())
            {
                target.TitleSlug = previous.TitleSlug;
            }

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
