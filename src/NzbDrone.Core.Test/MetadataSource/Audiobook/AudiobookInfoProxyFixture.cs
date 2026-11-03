using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookInfoProxyFixture : CoreTest<AudiobookInfoProxy>
    {
        private const string AuthorId = "audible:us:B0GVW7FF1Q";
        private List<AudnexusBookResource> _resources;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(r => r.All()).Returns(AudiobookMetadataOverrideSeed.Read());
            Mocker.SetConstant<IAudiobookMetadataOverrideService>(Mocker.Resolve<AudiobookMetadataOverrideService>());
            _resources = new List<AudnexusBookResource> { Recording("B0H8F9D4GD", "Volume 1"), Recording("B0HMVRXW3C", "Volume 2") };
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudnexusRegion).Returns("us");
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.SearchBooks(It.IsAny<string>(), "us")).Returns(() => _resources);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(It.IsAny<string>(), "us"))
                .Returns((string asin, string region) => _resources.SingleOrDefault(r => r.Asin == asin));
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor("B0GVW7FF1Q", "us"))
                .Returns(new AudnexusAuthorResource { Asin = "B0GVW7FF1Q", Region = "us", Name = "Yu Omiya" });
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.SearchAuthors(It.IsAny<string>(), "us"))
                .Returns(new List<AudnexusContributorResource>());
        }

        private static AudnexusBookResource Recording(string asin, string title)
        {
            return new AudnexusBookResource
            {
                Asin = asin, Region = "us", Title = title, ReleaseDate = new DateTime(2026, 10, 29),
                RuntimeLengthMin = 360, Language = "english", Description = "Recording description",
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = "B0GVW7FF1Q", Name = "Yu Omiya" } },
                Narrators = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "David Lee Huynh" } },
                SeriesPrimary = new AudnexusSeriesResource { Asin = "B0H8QJKN8Z", Name = "My Girlfriend Cheated on Me", Position = "1.5" }
            };
        }

        [Test]
        public void should_search_author_and_recordings_without_goodreads()
        {
            var results = Subject.SearchForNewEntity("Yu Omiya");
            results.OfType<Author>().Should().ContainSingle(a => a.Name == "Yu Omiya");
            results.OfType<Book>().Should().HaveCount(2);
            results.OfType<Book>().Select(b => b.ForeignBookId).Should().OnlyContain(id => id.StartsWith("audible:us:"));
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.SearchBooks("Yu Omiya", "us"), Times.Once());
        }

        [Test]
        public void should_map_recording_metadata_and_series()
        {
            var book = Subject.SearchByAsin("b0h8f9d4gd").Single();
            book.ReleaseDate.Should().Be(new DateTime(2026, 10, 29));
            book.Editions.Value.Should().ContainSingle(e => !e.IsEbook && e.Asin == "B0H8F9D4GD" && e.AudibleRegion == "us" && e.Monitored);
            book.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            book.SeriesLinks.Value.Should().ContainSingle(l => l.Position == "1.5" && l.IsPrimary);
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
        }

        [Test]
        public void should_keep_marketplaces_separate()
        {
            var resource = _resources[0];
            var us = AudiobookMetadataMapper.Book(resource, "us");
            resource.Region = "uk";
            var uk = AudiobookMetadataMapper.Book(resource, "uk");
            uk.ForeignBookId.Should().NotBe(us.ForeignBookId);
            uk.Editions.Value.Single().ForeignEditionId.Should().NotBe(us.Editions.Value.Single().ForeignEditionId);
        }

        [Test]
        public void should_keep_different_narrations_as_distinct_recordings()
        {
            _resources[1].Title = _resources[0].Title;
            var books = Subject.SearchForNewBook("Volume 1", null);
            books.Should().HaveCount(2);
            books.Select(b => b.ForeignBookId).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_propagate_search_outages_instead_of_reporting_no_results()
        {
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.SearchBooks(It.IsAny<string>(), "us"))
                .Throws(new BookInfoException("Unavailable"));
            Assert.Throws<BookInfoException>(() => Subject.SearchForNewEntity("Yu Omiya"));
        }

        [Test]
        public void should_return_empty_results_for_unmatched_searches()
        {
            _resources.Clear();
            Subject.SearchForNewEntity("Unknown writer").Should().BeEmpty();
        }

        [Test]
        public void should_report_missing_recording_without_reinterpreting_its_id()
        {
            Assert.Throws<BookNotFoundException>(() => Subject.GetBookInfo("audible:us:B000000000"));
            Subject.SearchByAsin("B000000000").Should().BeEmpty();
        }

        [Test]
        public void should_reject_legacy_ids_and_ignore_goodreads_file_tags()
        {
            Assert.Throws<BookInfoException>(() => Subject.GetBookInfo("1234"));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorInfo("1234"));
            Subject.SearchByGoodreadsBookId(1234, true).Should().BeEmpty();
        }

        [Test]
        public void should_not_treat_capped_search_results_as_a_complete_bibliography()
        {
            var known = AudiobookMetadataMapper.Book(Recording("B000000001", "Older recording"), "us");
            known.Id = 7;
            var author = new Author { Id = 5, AuthorMetadataId = 6, Metadata = known.AuthorMetadata.Value };
            Mocker.GetMock<IAuthorService>().Setup(x => x.FindById(AuthorId)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(x => x.GetBooksByAuthorMetadataId(6)).Returns(new List<Book> { known });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(7)).Returns(known.Editions.Value);

            var result = Subject.GetAuthorInfo(AuthorId);

            result.Books.Value.Should().HaveCount(3);
            result.Books.Value.Should().Contain(b => b.ForeignBookId == known.ForeignBookId);
            result.Metadata.Value.Id.Should().Be(6);
        }

        [Test]
        public void should_use_audible_rs_for_author_discovery_when_connected()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks("Yu Omiya", "us")).Returns(_resources);
            var author = Subject.GetAuthorInfo(AuthorId);
            author.Books.Value.Should().HaveCount(2);
            author.Series.Value.Should().ContainSingle(s => s.LinkItems.Value.Count == 2);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.SearchBooks(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_fall_back_to_capped_search_when_author_catalog_fails()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks("Yu Omiya", "us"))
                .Throws(new BookInfoException("Incomplete catalog"));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorInfo(AuthorId));
        }

        [Test]
        public void should_preserve_existing_ids_and_monitoring_in_search_results()
        {
            var known = AudiobookMetadataMapper.Book(_resources[0], "us");
            known.Id = 42;
            known.Monitored = true;
            var edition = known.Editions.Value.Single();
            edition.Id = 43;
            edition.ManualAdd = true;
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(known.ForeignBookId)).Returns(known);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(known.Editions.Value);
            var found = Subject.SearchByAsin(_resources[0].Asin).Single();
            found.Id.Should().Be(42);
            found.Monitored.Should().BeTrue();
            found.Editions.Value.Single().Id.Should().Be(43);
            found.Editions.Value.Single().ManualAdd.Should().BeTrue();
        }

        [Test]
        public void should_support_contributors_without_author_asins()
        {
            _resources[0].Authors[0].Asin = null;
            var found = Subject.SearchByAsin(_resources[0].Asin).Single();
            var identity = AudiobookId.Parse(found.AuthorMetadata.Value.ForeignAuthorId);
            identity.IsName.Should().BeTrue();
            identity.Value.Should().Be("Yu Omiya");
            found.AuthorMetadata.Value.TitleSlug.Should().NotContain(":").And.NotContain("%");
        }

        [Test]
        public void should_include_an_exact_name_without_asin_in_an_established_bibliography()
        {
            _resources[0].Authors[0].Asin = null;

            var result = Subject.GetAuthorInfo(AuthorId);

            result.Books.Value.Should().HaveCount(2);
            result.Books.Value.Should().OnlyContain(b => b.AuthorMetadata.Value.ForeignAuthorId == AuthorId);
        }

        [TestCase("B000000001", "Yu Omiya")]
        [TestCase(null, "Someone Else")]
        public void should_not_attach_a_conflicting_contributor_to_an_established_author(string asin, string name)
        {
            _resources[0].Authors[0].Asin = asin;
            _resources[0].Authors[0].Name = name;

            Subject.GetAuthorInfo(AuthorId).Books.Value.Should().ContainSingle(b => b.ForeignBookId == "audible:us:B0HMVRXW3C");
        }

        [Test]
        public void should_keep_the_stored_recording_author_when_a_detail_response_omits_its_asin()
        {
            var stored = AudiobookMetadataMapper.Book(_resources[0], "us");
            stored.Id = 42;
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            _resources[0].Authors[0].Asin = null;

            var result = Subject.GetBookInfo(stored.ForeignBookId);

            result.Item1.Should().Be(AuthorId);
            result.Item3.Should().ContainSingle(a => a.ForeignAuthorId == AuthorId);
        }
    }
}
