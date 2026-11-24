using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using Readarr.Api.V1.Indexers;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class ReleaseSearchIdentityFixture
    {
        private readonly string _guid = "owned-release";
        private Mock<ISearchForReleases> _search;
        private Mock<IMakeDownloadDecision> _decisions;
        private Mock<IDownloadService> _downloads;
        private Mock<IAuthorService> _authors;
        private Mock<IBookService> _books;
        private Mock<IParsingService> _parsing;
        private CacheManager _cache;
        private List<RemoteBook> _grabbed;

        [SetUp]
        public void SetUp()
        {
            _search = new Mock<ISearchForReleases>();
            _decisions = new Mock<IMakeDownloadDecision>();
            _downloads = new Mock<IDownloadService>();
            _authors = new Mock<IAuthorService>();
            _books = new Mock<IBookService>();
            _parsing = new Mock<IParsingService>();
            _cache = new CacheManager();
            _grabbed = new List<RemoteBook>();

            _downloads.Setup(service => service.DownloadReport(It.IsAny<RemoteBook>(), It.IsAny<int?>()))
                .Callback<RemoteBook, int?>((book, _) =>
                {
                    _grabbed.Add(book);
                    book.SeedConfiguration = new TorrentSeedConfiguration();
                    book.ParsedBookInfo.ExtraInfo["fixture"] = "request-local";
                })
                .Returns(Task.CompletedTask);
        }

        [Test]
        public async Task exact_book_search_rows_should_survive_searching_another_book_with_the_same_guid()
        {
            var author = Author(7);
            var first = Remote(author, Book(11, author));
            var second = Remote(author, Book(12, author));
            BookSearch(11, first);
            BookSearch(12, second);

            var firstResult = (await Controller().GetReleases(11, null)).Single();
            var secondResult = (await Controller().GetReleases(12, null)).Single();
            firstResult.SearchResultId.Should().NotBeNullOrEmpty().And.NotBe(secondResult.SearchResultId);
            firstResult.Guid.Should().Be(secondResult.Guid);

            await Controller().DownloadRelease(Grab(firstResult, bookId: 11));
            await Controller().DownloadRelease(Grab(secondResult));

            _grabbed.Select(book => book.Books.Single().Id).Should().Equal(11, 12);
            _grabbed[0].Should().NotBeSameAs(first);
            first.SeedConfiguration.Should().BeNull();
            first.ParsedBookInfo.ExtraInfo.Should().BeEmpty();
        }

        [Test]
        public async Task an_exact_result_should_not_be_accepted_for_another_context_or_indexer()
        {
            var author = Author(7);
            BookSearch(11, Remote(author, Book(11, author)));
            var result = (await Controller().GetReleases(11, null)).Single();

            AssertRejected(Grab(result, bookId: 12), HttpStatusCode.Conflict);
            AssertRejected(Grab(result, authorId: 7), HttpStatusCode.Conflict);
            var wrongIndexer = Grab(result, bookId: 11);
            wrongIndexer.IndexerId = 2;
            AssertRejected(wrongIndexer, HttpStatusCode.NotFound);
            _grabbed.Should().BeEmpty();
        }

        [Test]
        public async Task legacy_grabs_should_require_scope_when_other_searches_reuse_the_guid()
        {
            var first = Author(7);
            var second = Author(8);
            AuthorSearch(7, Remote(first, Book(11, first)));
            AuthorSearch(8, Remote(second, Book(12, second)));
            var result = (await Controller().GetReleases(null, 7)).Single();
            await Controller().GetReleases(null, 8);

            AssertRejected(Grab(result, useIdentity: false), HttpStatusCode.Conflict);
            AssertRejected(Grab(result, authorId: 9, useIdentity: false), HttpStatusCode.Conflict);
            await Controller().DownloadRelease(Grab(result, authorId: 7, useIdentity: false));
            await Controller().DownloadRelease(Grab(result, authorId: 8, useIdentity: false));

            _grabbed.Select(book => book.Author.Id).Should().Equal(7, 8);
        }

        [Test]
        public async Task distinct_mappings_within_one_author_scope_should_keep_each_original_result()
        {
            var author = Author(7);
            AuthorSearch(7, Remote(author, Book(11, author)));
            var first = (await Controller().GetReleases(null, 7)).Single();
            AuthorSearch(7, Remote(author, Book(12, author)));
            var second = (await Controller().GetReleases(null, 7)).Single();

            AssertRejected(Grab(first, authorId: 7, useIdentity: false), HttpStatusCode.Conflict);
            await Controller().DownloadRelease(Grab(first, authorId: 7));
            await Controller().DownloadRelease(Grab(second, authorId: 7));
            _grabbed.Select(book => book.Books.Single().Id).Should().Equal(11, 12);
        }

        [Test]
        public async Task equivalent_repeated_searches_should_allow_the_legacy_grab()
        {
            var author = Author(7);
            BookSearch(11, Remote(author, Book(11, author)));
            var result = (await Controller().GetReleases(11, null)).Single();
            BookSearch(11, Remote(author, Book(11, author)));
            await Controller().GetReleases(11, null);

            await Controller().DownloadRelease(Grab(result, useIdentity: false));
            _grabbed.Single().Books.Single().Id.Should().Be(11);
        }

        [Test]
        public async Task an_unavailable_result_identity_should_not_fall_back_to_a_new_result()
        {
            var author = Author(7);
            BookSearch(11, Remote(author, Book(11, author)));
            var result = (await Controller().GetReleases(11, null)).Single();
            _cache.Caches.Single().Clear();
            await Controller().GetReleases(11, null);

            AssertRejected(Grab(result, bookId: 11), HttpStatusCode.NotFound);
            _grabbed.Should().BeEmpty();
        }

        [Test]
        public async Task later_changes_to_a_release_report_should_not_replace_an_existing_result()
        {
            var author = Author(7);
            var source = Remote(author, Book(11, author));
            source.Release.DownloadUrl = "https://synthetic.invalid/first";
            source.Release.BookAuthors.Add("First author");
            source.ParsedBookInfo.StructuredRelease = source.Release;
            BookSearch(11, source);
            var result = (await Controller().GetReleases(11, null)).Single();

            source.Release.DownloadUrl = "https://synthetic.invalid/second";
            source.Release.BookAuthors.Add("Second author");
            await Controller().GetReleases(11, null);
            await Controller().DownloadRelease(Grab(result, bookId: 11));

            var submitted = _grabbed.Single();
            submitted.Release.DownloadUrl.Should().Be("https://synthetic.invalid/first");
            submitted.Release.BookAuthors.Should().Equal("First author");
            submitted.ParsedBookInfo.StructuredRelease.Should().BeSameAs(submitted.Release);
        }

        [Test]
        public async Task legacy_and_exact_rss_forced_associations_should_not_change_the_cached_mapping()
        {
            var unknown = Remote(null);
            _decisions.Setup(service => service.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), false))
                .Returns(new List<DownloadDecision> { new DownloadDecision(unknown) });
            var rssResult = (await Controller().GetReleases(null, null)).Single();
            var first = Author(7);
            var second = Author(8);
            _books.Setup(service => service.GetBook(11)).Returns(Book(11, first));
            _books.Setup(service => service.GetBook(12)).Returns(Book(12, second));
            _authors.Setup(service => service.GetAuthor(7)).Returns(first);
            _authors.Setup(service => service.GetAuthor(8)).Returns(second);

            await Controller().DownloadRelease(Grab(rssResult, bookId: 11, useIdentity: false));
            await Controller().DownloadRelease(Grab(rssResult, bookId: 12));

            _grabbed.Select(book => book.Books.Single().Id).Should().Equal(11, 12);
            unknown.Author.Should().BeNull();
            unknown.Books.Should().BeEmpty();
            unknown.ParsedBookInfo.ExtraInfo.Should().BeEmpty();
        }

        [Test]
        public async Task matched_packs_should_include_the_requested_book_and_preserve_all_members()
        {
            var author = Author(7);
            BookSearch(11, Remote(author, Book(11, author), Book(12, author)));
            var result = (await Controller().GetReleases(11, null)).Single();
            await Controller().DownloadRelease(Grab(result, bookId: 11));
            _grabbed.Single().Books.Select(book => book.Id).Should().Equal(11, 12);
        }

        [Test]
        public async Task a_known_wrong_book_match_should_not_be_grabbed_for_the_requested_recording()
        {
            var author = Author(7);
            BookSearch(11, Remote(author, Book(12, author)));
            var result = (await Controller().GetReleases(11, null)).Single();

            AssertRejected(Grab(result), HttpStatusCode.Conflict);
            _grabbed.Should().BeEmpty();
        }

        private ReleaseController Controller()
        {
            var rss = new Mock<IFetchAndParseRss>();
            rss.Setup(service => service.Fetch()).ReturnsAsync(new List<ReleaseInfo>());
            var prioritize = new Mock<IPrioritizeDownloadDecision>();
            prioritize.Setup(service => service.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns((List<DownloadDecision> decisions) => decisions);
            var controller = new ReleaseController(rss.Object,
                _search.Object,
                _decisions.Object,
                prioritize.Object,
                _downloads.Object,
                _authors.Object,
                _books.Object,
                _parsing.Object,
                _cache,
                LogManager.GetCurrentClassLogger());
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.Request.Method = "POST";
            return controller;
        }

        private void BookSearch(int bookId, RemoteBook book)
        {
            _search.Setup(service => service.BookSearch(bookId, true, true, true))
                .ReturnsAsync(new List<DownloadDecision> { new DownloadDecision(book) });
        }

        private void AuthorSearch(int authorId, RemoteBook book)
        {
            _search.Setup(service => service.AuthorSearch(authorId, false, true, true))
                .ReturnsAsync(new List<DownloadDecision> { new DownloadDecision(book) });
        }

        private RemoteBook Remote(Author author, params Book[] books)
        {
            return new RemoteBook
            {
                Release = new ReleaseInfo { Guid = _guid, IndexerId = 1, Title = "Owned release" },
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.M4B) },
                Author = author,
                Books = books.ToList(),
                DownloadAllowed = books.Length > 0
            };
        }

        private static Author Author(int id)
        {
            return new Author
            {
                Id = id,
                QualityProfile = new QualityProfile
                {
                    Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.M4B, Allowed = true } }
                }
            };
        }

        private static Book Book(int id, Author author) => new Book { Id = id, Author = author };

        private static ReleaseResource Grab(ReleaseResource result, int? bookId = null, int? authorId = null, bool useIdentity = true)
        {
            return new ReleaseResource
            {
                Guid = result.Guid,
                IndexerId = result.IndexerId,
                SearchResultId = useIdentity ? result.SearchResultId : null,
                BookId = bookId,
                AuthorId = authorId
            };
        }

        private void AssertRejected(ReleaseResource request, HttpStatusCode status)
        {
            var error = Assert.ThrowsAsync<NzbDroneClientException>(async () => await Controller().DownloadRelease(request));
            error.StatusCode.Should().Be(status);
        }
    }
}
