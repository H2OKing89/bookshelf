using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public class AudiobookInfoProxy : IProvideBookInfo, IProvideAuthorInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        private readonly IAudnexusProxy _audnexus;
        private readonly IAudibleCatalogProxy _audible;
        private readonly IConfigService _config;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly ICached<AuthorMetadata> _authors;

        public AudiobookInfoProxy(IAudnexusProxy audnexus,
            IAudibleCatalogProxy audible,
            IConfigService config,
            IAuthorService authorService,
            IBookService bookService,
            IEditionService editionService,
            ICacheManager cacheManager)
        {
            _audnexus = audnexus;
            _audible = audible;
            _config = config;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _authors = cacheManager.GetCache<AuthorMetadata>(GetType(), "AudiobookAuthors");
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime) => null;

        public List<object> SearchForNewEntity(string title)
        {
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
            var books = SearchForNewBook(title, null);
            return books.Any() ? books.Select(b => b.Author.Value).DistinctBy(a => a.ForeignAuthorId).ToList() : FindAuthors(title);
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
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
                return AudiobookId.IsAsin(asin) ? Lookup(AudiobookId.ForAsin(_config.AudnexusRegion, asin)) : new List<Book>();
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
                return Lookup(AudiobookId.ForAsin(_config.AudnexusRegion, query.ToUpperInvariant()));
            }

            if (author.IsNotNullOrWhiteSpace())
            {
                query += " " + author.Trim();
            }

            return _audnexus.SearchBooks(query, _config.AudnexusRegion)
                .Select(resource => Map(resource, _config.AudnexusRegion))
                .DistinctBy(book => book.ForeignBookId).ToList();
        }

        public List<Book> SearchByAsin(string asin) => SearchForNewBook("asin:" + asin, null);
        public List<Book> SearchByIsbn(string isbn) => SearchForNewBook(isbn, null);

        // Legacy file tags are not audiobook catalog identifiers.
        public List<Book> SearchByGoodreadsBookId(int goodreadsId, bool getAllEditions) => new List<Book>();

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
        {
            var identity = AudiobookId.Parse(id);
            if (identity.IsName)
            {
                throw new BookInfoException("A recording requires an Audible ASIN.");
            }

            var resource = _audnexus.GetBook(identity.Value, identity.Region);
            if (resource == null && _config.AudibleRsEnabled)
            {
                resource = _audible.GetBook(identity.Value, identity.Region);
            }

            if (resource == null)
            {
                throw new BookNotFoundException(id);
            }

            var book = Map(resource, identity.Region);
            var authors = resource.Authors.Where(a => a?.Name.IsNotNullOrWhiteSpace() == true)
                .Select(a => AudiobookMetadataMapper.Author(a, identity.Region)).DistinctBy(a => a.ForeignAuthorId).ToList();
            return Tuple.Create(book.AuthorMetadata.Value.ForeignAuthorId, book, authors);
        }

        public Author GetAuthorInfo(string id, bool useCache = true)
        {
            var identity = AudiobookId.Parse(id);
            var local = _authorService.FindById(id);
            var metadata = local?.Metadata.Value ?? _authors.Find(id);
            if (!identity.IsName)
            {
                var profile = _audnexus.GetAuthor(identity.Value, identity.Region);
                if (profile != null)
                {
                    metadata = AudiobookMetadataMapper.Author(profile, identity.Region);
                }
            }
            else
            {
                metadata ??= AudiobookMetadataMapper.Author(new AudnexusContributorResource { Name = identity.Value }, identity.Region);
            }

            if (metadata == null)
            {
                throw new BookInfoException("No author profile was found for this ASIN. Search for an audiobook title by this author first.");
            }

            var resources = _config.AudibleRsEnabled
                ? _audible.GetAuthorBooks(metadata.Name, identity.Region)
                : _audnexus.SearchBooks(metadata.Name, identity.Region);
            var discovered = resources.Select(r => Map(r, identity.Region))
                .Where(b => b.AuthorMetadata.Value.ForeignAuthorId == id).ToList();

            // Neither a capped search nor a regional catalog is an authoritative deletion feed.
            var known = local == null ? new List<Book>() : _bookService.GetBooksByAuthorMetadataId(local.AuthorMetadataId);
            foreach (var book in known)
            {
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

            _authors.Set(id, metadata, TimeSpan.FromHours(1));
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

        private List<Author> FindAuthors(string name)
        {
            return _audnexus.SearchAuthors(name, _config.AudnexusRegion)
                .Where(a => a?.Name.IsNotNullOrWhiteSpace() == true)
                .Select(a =>
                {
                    var metadata = AudiobookMetadataMapper.Author(a, _config.AudnexusRegion);
                    _authors.Set(metadata.ForeignAuthorId, metadata, TimeSpan.FromHours(1));
                    return _authorService.FindById(metadata.ForeignAuthorId) ?? new Author
                    {
                        Metadata = metadata, Books = new List<Book>(), Series = new List<Series>()
                    };
                }).DistinctBy(a => a.ForeignAuthorId).ToList();
        }

        private Book Map(AudnexusBookResource resource, string region)
        {
            var book = AudiobookMetadataMapper.Book(resource, region);
            var metadata = book.AuthorMetadata.Value;
            _authors.Set(metadata.ForeignAuthorId, metadata, TimeSpan.FromHours(1));
            var existing = _bookService.FindById(book.ForeignBookId);
            if (existing != null)
            {
                book.UseDbFieldsFrom(existing);
                var editions = _editionService.GetEditionsByBook(existing.Id);
                foreach (var edition in book.Editions.Value)
                {
                    var previous = editions.SingleOrDefault(e => e.ForeignEditionId == edition.ForeignEditionId);
                    if (previous != null)
                    {
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
