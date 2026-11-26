using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.Search;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class AudiobookSearchDecisionFixture : CoreTest<DownloadDecisionMaker>
    {
        private Author _author;
        private Book _first;

        [SetUp]
        public void Setup()
        {
            _first = Recording("B000000001", "Example Series", "1");
            _first.Id = 1;
            var companion = Recording("B000000002", "Example Series: Recollections (Light Novel)", null);
            companion.Id = 2;
            _author = _first.Author.Value;
            _author.Id = 7;
            _author.AuthorMetadataId = 9;
            var books = new List<Book> { _first, companion };
            foreach (var book in books)
            {
                book.Editions.Value[0].BookId = book.Id;
                foreach (var link in book.SeriesLinks.Value)
                {
                    link.BookId = book.Id;
                }
            }

            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthor(7)).Returns(books);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByAuthor(7)).Returns(books.SelectMany(book => book.Editions.Value).ToList());
            Mocker.GetMock<ISeriesBookLinkService>().Setup(service => service.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns(books.SelectMany(book => book.SeriesLinks.Value).ToList());
            Mocker.SetConstant<IParsingService>(Mocker.Resolve<ParsingService>());
            Mocker.SetConstant<IEnumerable<IDecisionEngineSpecification>>(new[] { Mocker.Resolve<BookRequestedSpecification>() });
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(service => service.ParseCustomFormat(It.IsAny<RemoteBook>(), It.IsAny<long>()))
                .Returns(new List<CustomFormat>());
        }

        private static Book Recording(string asin, string title, string position) => AudiobookMetadataMapper.Book(
            new AudnexusBookResource
            {
                Asin = asin, Region = "us", Title = title,
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = "B000000099", Name = "Example Author" } },
                Narrators = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "Example Reader" } },
                SeriesPrimary = new AudnexusSeriesResource { Name = "Example Series", Position = position }
            },
            "us");

        [TestCase("Example Series - Recollections", "27", false)]
        [TestCase("Example Series, Vol. 1", "1", true)]
        public void should_use_structured_metadata_before_deciding_if_the_requested_book_matches(string title, string position, bool approved)
        {
            var release = new ReleaseInfo
            {
                Title = title + " by Example Author [ENG / M4B]", Book = title, Author = "Example Author",
                BookAuthors = new List<string> { "Example Author" }, Narrators = new List<string> { "Example Reader" },
                BookSeries = new List<ReleaseBookSeries> { new ReleaseBookSeries { Name = "Example Series", Position = position } }
            };
            var criteria = new BookSearchCriteria { Author = _author, Books = new List<Book> { _first } };

            var decision = Subject.GetSearchDecision(new List<ReleaseInfo> { release }, criteria).Single();

            decision.Approved.Should().Be(approved);
            decision.RemoteBook.ParsedBookInfo.BookTitle.Should().Be(title);
            decision.RemoteBook.Books.Should().ContainSingle(book => book.Id == (approved ? 1 : 2));
            if (!approved)
            {
                decision.Rejections.Should().Contain(rejection => rejection.Reason == "Book wasn't requested");
            }

            Mocker.GetMock<IBookService>().Verify(service => service.FindByTitleInexact(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }
    }
}
