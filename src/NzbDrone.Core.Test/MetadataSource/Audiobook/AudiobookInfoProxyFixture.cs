using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookInfoProxyFixture : CoreTest<AudiobookInfoProxy>
    {
        private const string AuthorId = "audible:us:B0GVW7FF1Q";
        private const string LegacyAuthorName = "Alex Example";
        private const string PromotedAuthorAsin = "B000000011";
        private List<AudnexusBookResource> _resources;
        private MetadataProviderStatusService _status;

        [SetUp]
        public void Setup()
        {
            _status = new MetadataProviderStatusService();
            Mocker.SetConstant<IMetadataProviderStatusService>(_status);
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
        public void cancelled_lookup_should_not_poison_optional_recording_metadata_cache()
        {
            using var source = new CancellationTokenSource();
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(_resources[0].Asin, "us"))
                .Returns(() =>
                {
                    source.Cancel();
                    throw new OperationCanceledException(source.Token);
                });

            using (CommandExecutionContext.Begin(source.Token))
            {
                Assert.Throws<OperationCanceledException>(() => Subject.GetBookInfo(AudiobookId.ForAsin("us", _resources[0].Asin)));
            }

            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(_resources[0].Asin, "us")).Returns(_resources[0]);
            Subject.GetBookInfo(AudiobookId.ForAsin("us", _resources[0].Asin)).Item2.Title.Should().Be(_resources[0].Title);
        }

        [Test]
        public void should_use_the_established_english_author_name_in_same_identity_recording_credits()
        {
            _resources[0].Authors[0].Name = "道造";
            var existing = AudiobookMetadataMapper.Book(_resources[0], "us");
            existing.AuthorMetadata.Value.Name = "Mitizou";
            existing.AuthorMetadata.Value.Aliases.Add("道造");
            Mocker.GetMock<IBookService>().Setup(service => service.FindById(existing.ForeignBookId)).Returns(existing);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(existing.Id)).Returns(existing.Editions.Value);

            var result = Subject.GetBookInfo(existing.ForeignBookId).Item2;

            result.AuthorMetadata.Value.Name.Should().Be("Mitizou");
            result.Editions.Value.Single().Contributors.Should().Contain(credit => credit.Role == "Author" && credit.Name == "Mitizou");
            _resources[0].Authors[0].Name.Should().Be("道造");
        }

        [Test]
        public void should_exclude_non_english_catalog_recordings_before_optional_enrichment()
        {
            var local = new Author
            {
                Id = 5, AuthorMetadataId = 6, MetadataProfileId = 1,
                Metadata = AudiobookMetadataMapper.Author(_resources[0].Authors.Single(), "us")
            };
            _resources[1].Language = "japanese";
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(AuthorId)).Returns(local);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(repository => repository.Get(1))
                .Returns(new MetadataProfile { AllowedLanguages = "eng" });
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthorMetadataId(6)).Returns(new List<Book>());

            var result = Subject.GetAuthorInfo(AuthorId);

            result.Books.Value.Should().ContainSingle(book => book.ForeignBookId == AudiobookId.ForAsin("us", _resources[0].Asin));
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(_resources[1].Asin, "us"), Times.Never());
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetChapters(_resources[1].Asin, "us"), Times.Never());
        }

        [TestCase("eng", null, 1)]
        [TestCase("eng,null", null, 2)]
        [TestCase("jpn", "japanese", 1)]
        [TestCase(null, "japanese", 2)]
        public void should_honor_the_configured_language_selection_including_unknown_language(string allowed, string language, int expected)
        {
            var local = new Author
            {
                Id = 5, AuthorMetadataId = 6, MetadataProfileId = 1,
                Metadata = AudiobookMetadataMapper.Author(_resources[0].Authors.Single(), "us")
            };
            _resources[1].Language = language;
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(AuthorId)).Returns(local);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(repository => repository.Get(1))
                .Returns(new MetadataProfile { AllowedLanguages = allowed });
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthorMetadataId(6)).Returns(new List<Book>());

            Subject.GetAuthorInfo(AuthorId).Books.Value.Should().HaveCount(expected);
        }

        [Test]
        public void should_retain_existing_recordings_when_the_new_discovery_language_excludes_them()
        {
            _resources[1].Language = "japanese";
            var known = AudiobookMetadataMapper.Book(_resources[1], "us");
            known.Id = 7;
            var local = new Author { Id = 5, AuthorMetadataId = 6, MetadataProfileId = 1, Metadata = known.AuthorMetadata.Value };
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(AuthorId)).Returns(local);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(repository => repository.Get(1))
                .Returns(new MetadataProfile { AllowedLanguages = "eng" });
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthorMetadataId(6)).Returns(new List<Book> { known });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(7)).Returns(known.Editions.Value);

            Subject.GetAuthorInfo(AuthorId).Books.Value.Should().Contain(book => book.ForeignBookId == known.ForeignBookId);
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(_resources[1].Asin, "us"), Times.Never());
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

        [TestCase("B0F14RFHS6")]
        [TestCase("asin:B0F14RFHS6")]
        public void should_discover_a_bare_uk_asin_without_inventing_a_us_recording(string query)
        {
            var uk = Recording("B0F14RFHS6", "Harry Potter and the Philosopher's Stone (Full-Cast Edition)");
            uk.Region = "uk";
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(uk.Asin, "uk")).Returns(uk);

            var book = Subject.SearchForNewBook(query, null).Single();

            book.ForeignBookId.Should().Be("audible:uk:B0F14RFHS6");
            book.Editions.Value.Single().Asin.Should().Be(uk.Asin);
            book.Editions.Value.Single().AudibleRegion.Should().Be("uk");
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(uk.Asin, "us"), Times.Once());
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(uk.Asin, "uk"), Times.Once());
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(uk.Asin, "au"), Times.Never());
        }

        [Test]
        public void should_stop_at_a_successful_preferred_marketplace_lookup()
        {
            var asin = _resources[0].Asin;

            Subject.SearchByAsin(asin).Should().ContainSingle(book => book.ForeignBookId == "audible:us:" + asin);

            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(asin, It.Is<string>(region => region != "us")), Times.Never());
        }

        [Test]
        public void should_keep_explicit_marketplace_ids_strict_when_the_asin_exists_elsewhere()
        {
            var uk = Recording("B0F14RFHS6", "Harry Potter and the Philosopher's Stone (Full-Cast Edition)");
            uk.Region = "uk";
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(uk.Asin, "uk")).Returns(uk);

            Subject.SearchForNewBook("audible:us:" + uk.Asin, null).Should().BeEmpty();

            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(uk.Asin, "uk"), Times.Never());
        }

        [Test]
        public void should_not_search_other_marketplaces_after_a_provider_failure()
        {
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook("B0F14RFHS6", "us"))
                .Throws(new BookInfoException("Provider unavailable"));

            Assert.Throws<BookInfoException>(() => Subject.SearchByAsin("B0F14RFHS6"));

            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook("B0F14RFHS6", It.Is<string>(region => region != "us")), Times.Never());
        }

        [Test]
        public void should_skip_a_skeletal_us_product_and_identify_the_exact_uk_recording()
        {
            const string asin = "B0F14RFHS6";
            var uk = Recording(asin, "Harry Potter and the Philosopher's Stone (Full-Cast Edition)");
            uk.Region = "uk";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(asin, "us"))
                .Returns(new AudnexusBookResource { Asin = asin, Region = "us" });
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(asin, "uk")).Returns(uk);

            var book = Subject.SearchByAsin(asin).Single();

            book.ForeignBookId.Should().Be("audible:uk:" + asin);
            book.Editions.Value.Single().Title.Should().Be(uk.Title);
            book.Editions.Value.Single().AudibleRegion.Should().Be("uk");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_try_the_same_marketplace_alternate_provider_when_primary_required_fields_are_missing(bool missingTitle)
        {
            var complete = Recording("B0F14RFHS6", "A complete US recording");
            var partial = Recording(complete.Asin, complete.Title);
            if (missingTitle)
            {
                partial.Title = null;
            }
            else
            {
                partial.Authors = null;
            }

            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(complete.Asin, "us")).Returns(partial);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(complete.Asin, "us")).Returns(complete);

            var book = Subject.SearchByAsin(complete.Asin).Single();

            book.ForeignBookId.Should().Be("audible:us:" + complete.Asin);
            book.Editions.Value.Single().Title.Should().Be(complete.Title);
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(complete.Asin, It.Is<string>(region => region != "us")), Times.Never());
        }

        [Test]
        public void should_try_other_marketplaces_when_primary_metadata_is_incomplete_and_the_alternate_is_absent()
        {
            const string asin = "B0F14RFHS6";
            var uk = Recording(asin, "A verified UK recording");
            uk.Region = "uk";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(asin, "us"))
                .Returns(new AudnexusBookResource { Asin = asin, Region = "us" });
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(asin, "uk")).Returns(uk);

            Subject.SearchByAsin(asin).Should().ContainSingle(book => book.ForeignBookId == "audible:uk:" + asin);
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(proxy => proxy.GetBook(asin, "us"), Times.Once());
        }

        [Test]
        public void should_preserve_explicit_marketplace_constraints_for_incomplete_metadata()
        {
            const string asin = "B0F14RFHS6";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(asin, "us"))
                .Returns(new AudnexusBookResource { Asin = asin, Region = "us" });

            var failure = Assert.Catch<BookInfoException>(() => Subject.SearchForNewBook("audible:us:" + asin, null));

            failure.Message.Should().Contain("incomplete in this marketplace");
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(asin, It.Is<string>(region => region != "us")), Times.Never());
        }

        [Test]
        public void should_not_hide_provider_failures_behind_an_incomplete_alternate_product()
        {
            const string asin = "B0F14RFHS6";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(asin, "us"))
                .Throws(new BookInfoException("Provider unavailable"));
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(asin, "us"))
                .Returns(new AudnexusBookResource { Asin = asin, Region = "us" });

            var failure = Assert.Throws<BookInfoException>(() => Subject.SearchByAsin(asin));

            failure.Message.Should().Contain("temporarily unavailable");
            Mocker.GetMock<IAudnexusProxy>().Verify(proxy => proxy.GetBook(asin, It.Is<string>(region => region != "us")), Times.Never());
        }

        [Test]
        public void should_keep_verified_primary_metadata_when_optional_details_are_skeletal()
        {
            var asin = _resources[0].Asin;
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(asin, "us"))
                .Returns(new AudnexusBookResource { Asin = asin, Region = "us" });

            var book = Subject.GetBookInfo("audible:us:" + asin).Item2;

            book.Title.Should().Be(_resources[0].Title);
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
        }

        [Test]
        public void should_keep_a_verified_regional_recording_with_the_established_author_identity()
        {
            var established = new Author
            {
                Id = 5, AuthorMetadataId = 6,
                Metadata = AudiobookMetadataMapper.Author(_resources[0].Authors.Single(), "us")
            };
            established.Metadata.Value.Id = 6;
            var uk = Recording("B0F14RFHS6", "Harry Potter and the Philosopher's Stone (Full-Cast Edition)");
            uk.Region = "uk";
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(AuthorId)).Returns(established);
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(uk.Asin, "uk")).Returns(uk);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(uk.Asin, "uk")).Returns(uk);

            var book = Subject.GetBookInfo("audible:uk:" + uk.Asin).Item2;

            book.ForeignBookId.Should().Be("audible:uk:" + uk.Asin);
            book.Editions.Value.Single().AudibleRegion.Should().Be("uk");
            book.Author.Value.Should().BeSameAs(established);
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
            book.AuthorMetadataId.Should().Be(6);
        }

        [Test]
        public void should_not_choose_a_regional_author_from_a_matching_name_without_an_explicit_contributor_asin()
        {
            var established = new Author { Id = 5, Metadata = AudiobookMetadataMapper.Author(_resources[0].Authors.Single(), "us") };
            var uk = Recording("B0F14RFHS6", "Harry Potter and the Philosopher's Stone (Full-Cast Edition)");
            uk.Region = "uk";
            uk.Authors[0].Asin = null;
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(AuthorId)).Returns(established);
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetBook(uk.Asin, "uk")).Returns(uk);

            var book = Subject.GetBookInfo("audible:uk:" + uk.Asin).Item2;

            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be("audible:uk:name:Yu%20Omiya");
            book.Author.Value.Should().NotBeSameAs(established);
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
            _status.GetStatus().Should().ContainSingle(status => status.Operation == MetadataProviderOperation.AuthorRefresh && status.State == MetadataProviderState.Partial);
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
            _status.GetStatus().Should().ContainSingle(status => status.Operation == MetadataProviderOperation.AuthorRefresh && status.State == MetadataProviderState.Unavailable);
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

        private (Author Author, List<Book> Books) GivenLegacyAuthor(bool audibleCatalog = true)
        {
            foreach (var recording in _resources)
            {
                recording.Authors = new List<AudnexusContributorResource>
                {
                    new AudnexusContributorResource { Name = LegacyAuthorName, Asin = PromotedAuthorAsin }
                };
            }

            // The catalog originally omitted the author ASIN for this exact recording.
            var storedResource = Recording(_resources[0].Asin, _resources[0].Title);
            storedResource.Authors = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = LegacyAuthorName }
            };
            var stored = AudiobookMetadataMapper.Book(storedResource, "us");
            var metadata = stored.AuthorMetadata.Value;
            metadata.Id = 6;
            metadata.TitleSlug = "established-author-slug";
            metadata.Overview = "Stored author biography";
            metadata.Aliases.Add("Alex E. Example");
            metadata.Links.Add(new Links { Name = "Website", Url = "https://example.com/author" });
            var author = new Author
            {
                Id = 5, AuthorMetadataId = 6, Metadata = metadata, Monitored = false,
                MonitorNewItems = NewItemMonitorTypes.New, Path = "/library/Alex Example", QualityProfileId = 3,
                MetadataProfileId = 1, Tags = new HashSet<int> { 11 }
            };
            var known = new List<Book>();
            RememberLegacyRecording(author, known, stored, 42, 43);
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthorMetadataId(author.AuthorMetadataId)).Returns(known);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudibleRsEnabled).Returns(audibleCatalog);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetAuthorBooks(It.IsAny<string>(), "us")).Returns(() => _resources);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(It.IsAny<string>(), "us"))
                .Returns((string asin, string region) => _resources.SingleOrDefault(recording => recording.Asin == asin));
            return (author, known);
        }

        private void RememberLegacyRecording(Author author, List<Book> known, Book book, int id, int editionId)
        {
            book.Id = id;
            book.AuthorMetadataId = author.AuthorMetadataId;
            book.AuthorMetadata = author.Metadata.Value;
            book.Author = author;
            book.Monitored = false;
            book.AnyEditionOk = true;
            var edition = book.Editions.Value.Single();
            edition.Id = editionId;
            edition.BookId = id;
            edition.Monitored = false;
            edition.ManualAdd = true;
            known.Add(book);
            Mocker.GetMock<IBookService>().Setup(service => service.FindById(book.ForeignBookId)).Returns(book);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(id)).Returns(book.Editions.Value);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_promote_a_name_only_author_using_an_exact_owned_recording_and_discover_the_next_release(bool audibleCatalog)
        {
            var (author, known) = GivenLegacyAuthor(audibleCatalog);
            var oldId = author.ForeignAuthorId;
            var canonicalId = AudiobookId.ForAsin("us", PromotedAuthorAsin);
            _resources[1].ReleaseDate = DateTime.UtcNow.AddDays(30);

            // A homonym in the catalog is not evidence against the verified owner.
            var homonym = Recording("B000000020", "Unrelated release");
            homonym.Authors = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = LegacyAuthorName, Asin = "B000000012" }
            };
            _resources.Add(homonym);

            var result = Subject.GetAuthorInfo(oldId);

            result.ForeignAuthorId.Should().Be(canonicalId);
            result.IdentityPromotionFrom.Should().Be(oldId);
            result.IdentityPromotionRecordingId.Should().Be(known[0].ForeignBookId);
            result.Id.Should().Be(author.Id);
            result.AuthorMetadataId.Should().Be(author.AuthorMetadataId);
            result.Metadata.Value.Id.Should().Be(author.AuthorMetadataId);
            result.Metadata.Value.TitleSlug.Should().Be(author.Metadata.Value.TitleSlug);
            result.Metadata.Value.Name.Should().Be(author.Name);
            result.Metadata.Value.Overview.Should().Be(author.Metadata.Value.Overview);
            result.Metadata.Value.Aliases.Should().Contain("Alex E. Example");
            result.Metadata.Value.Links.Should().Contain(link => link.Url == "https://example.com/author");
            result.Metadata.Value.Links.Should().Contain(link => link.Name == "Audible" && link.Url.Contains(PromotedAuthorAsin));
            result.Path.Should().Be(author.Path);
            result.Monitored.Should().BeFalse();
            result.MonitorNewItems.Should().Be(NewItemMonitorTypes.New);
            result.QualityProfileId.Should().Be(author.QualityProfileId);
            result.MetadataProfileId.Should().Be(author.MetadataProfileId);
            result.Tags.Should().Equal(11);

            result.Books.Value.Select(book => book.ForeignBookId).Should().BeEquivalentTo(new[]
            {
                AudiobookId.ForAsin("us", _resources[0].Asin), AudiobookId.ForAsin("us", _resources[1].Asin)
            });
            result.Books.Value.Should().OnlyContain(book => book.AuthorMetadata.Value.ForeignAuthorId == canonicalId && book.AuthorMetadataId == author.AuthorMetadataId);
            var previous = result.Books.Value.Single(book => book.ForeignBookId == known[0].ForeignBookId);
            previous.Id.Should().Be(42);
            previous.Monitored.Should().BeFalse();
            previous.AnyEditionOk.Should().BeTrue();
            var previousEdition = previous.Editions.Value.Single();
            previousEdition.Id.Should().Be(43);
            previousEdition.BookId.Should().Be(42);
            previousEdition.Monitored.Should().BeFalse();
            previousEdition.ManualAdd.Should().BeTrue();

            // Metadata lookup/preview must not persist a promotion or alter shared model instances.
            author.ForeignAuthorId.Should().Be(oldId);
            known[0].AuthorMetadata.Value.ForeignAuthorId.Should().Be(oldId);
            known[0].Editions.Value.Single().Contributors.Should().Contain(credit => credit.Role == "Author" && credit.Asin == null);
        }

        [TestCase("unanchored")]
        [TestCase("unsaved-book")]
        [TestCase("wrong-book-owner")]
        [TestCase("no-persisted-edition")]
        [TestCase("wrong-edition-recording")]
        [TestCase("wrong-edition-marketplace")]
        [TestCase("wrong-edition-owner")]
        [TestCase("wrong-catalog-marketplace")]
        [TestCase("different-name")]
        [TestCase("ambiguous-credit")]
        [TestCase("nonprimary-credit")]
        [TestCase("stored-credit-conflict")]
        [TestCase("local-recording")]
        [TestCase("canonical-owner-us")]
        [TestCase("canonical-owner-uk")]
        public void should_not_promote_a_legacy_author_without_unambiguous_recording_evidence(string conflict)
        {
            var (author, known) = GivenLegacyAuthor();
            var oldId = author.ForeignAuthorId;
            var book = known[0];
            var edition = book.Editions.Value.Single();
            switch (conflict)
            {
                case "unanchored":
                    _resources.RemoveAt(0);
                    break;
                case "unsaved-book":
                    book.Id = 0;
                    edition.BookId = 0;
                    Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(0)).Returns(book.Editions.Value);
                    break;
                case "wrong-book-owner":
                    book.AuthorMetadataId = 99;
                    break;
                case "no-persisted-edition":
                    edition.Id = 0;
                    break;
                case "wrong-edition-recording":
                    edition.ForeignEditionId = AudiobookId.ForAsin("us", "B000000030");
                    edition.Asin = "B000000030";
                    break;
                case "wrong-edition-marketplace":
                    edition.AudibleRegion = "uk";
                    break;
                case "wrong-edition-owner":
                    edition.BookId = 99;
                    break;
                case "wrong-catalog-marketplace":
                    _resources[0].Region = "uk";
                    break;
                case "different-name":
                    _resources[0].Authors[0].Name = "Different Author";
                    break;
                case "ambiguous-credit":
                    _resources[0].Authors.Add(new AudnexusContributorResource { Name = LegacyAuthorName, Asin = "B000000012" });
                    break;
                case "nonprimary-credit":
                    _resources[0].Authors.Insert(0, new AudnexusContributorResource { Name = "Different Author", Asin = "B000000012" });
                    break;
                case "stored-credit-conflict":
                    edition.Contributors.Single(credit => credit.Role == "Author").Asin = "B000000012";
                    break;
                case "local-recording":
                    book.ForeignBookId = LocalRecordingId.NewId();
                    edition.ForeignEditionId = book.ForeignBookId;
                    edition.Asin = null;
                    break;
                case "canonical-owner-us":
                case "canonical-owner-uk":
                    var region = conflict.EndsWith("-us", StringComparison.Ordinal) ? "us" : "uk";
                    var metadata = AudiobookMetadataMapper.Author(_resources[0].Authors[0], region);
                    metadata.Id = 100;
                    Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(metadata.ForeignAuthorId)).Returns(
                        new Author { Id = 99, AuthorMetadataId = 100, Metadata = metadata });
                    break;
            }

            var result = Subject.GetAuthorInfo(oldId);

            result.ForeignAuthorId.Should().Be(oldId);
            result.IdentityPromotionFrom.Should().BeNull();
            result.IdentityPromotionRecordingId.Should().BeNull();
            result.Books.Value.Select(item => item.ForeignBookId).Should().Equal(book.ForeignBookId);
            author.ForeignAuthorId.Should().Be(oldId);
        }

        [Test]
        public void should_not_promote_when_multiple_owned_recordings_disagree_about_the_author_asin()
        {
            var (author, known) = GivenLegacyAuthor();
            var other = Recording(_resources[1].Asin, _resources[1].Title);
            other.Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = LegacyAuthorName } };
            RememberLegacyRecording(author, known, AudiobookMetadataMapper.Book(other, "us"), 44, 45);
            _resources[1].Authors[0].Asin = "B000000012";

            var result = Subject.GetAuthorInfo(author.ForeignAuthorId);

            result.ForeignAuthorId.Should().Be(author.ForeignAuthorId);
            result.IdentityPromotionFrom.Should().BeNull();
            result.Books.Value.Select(book => book.Id).Should().BeEquivalentTo(new[] { 42, 44 });
        }

        [Test]
        public void should_not_promote_when_alias_catalogs_disagree_about_the_same_recording()
        {
            var (author, known) = GivenLegacyAuthor();
            var alternate = Recording(_resources[0].Asin, _resources[0].Title);
            alternate.Authors = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = LegacyAuthorName, Asin = "B000000012" }
            };
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetAuthorBooks("Alex E. Example", "us"))
                .Returns(new List<AudnexusBookResource> { alternate });

            var result = Subject.GetAuthorInfo(author.ForeignAuthorId);

            result.ForeignAuthorId.Should().Be(author.ForeignAuthorId);
            result.IdentityPromotionFrom.Should().BeNull();
            result.Books.Value.Select(book => book.Id).Should().Equal(known[0].Id);
        }

        [Test]
        public void should_keep_a_known_recording_omitted_by_the_catalog_when_promoting_and_preserve_the_original_models()
        {
            var (author, known) = GivenLegacyAuthor();
            var oldId = author.ForeignAuthorId;
            var omitted = Recording("B000000031", "An older catalog release");
            omitted.Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = LegacyAuthorName } };
            RememberLegacyRecording(author, known, AudiobookMetadataMapper.Book(omitted, "us"), 44, 45);

            var result = Subject.GetAuthorInfo(oldId);

            result.ForeignAuthorId.Should().Be(AudiobookId.ForAsin("us", PromotedAuthorAsin));
            result.Books.Value.Should().HaveCount(3);
            var returned = result.Books.Value.Single(book => book.Id == 44);
            returned.Editions.Value.Single().Id.Should().Be(45);
            returned.AuthorMetadata.Value.ForeignAuthorId.Should().Be(result.ForeignAuthorId);
            returned.AuthorMetadataId.Should().Be(author.AuthorMetadataId);
            known[1].AuthorMetadata.Value.ForeignAuthorId.Should().Be(oldId);
            known[1].Author.Value.Should().BeSameAs(author);
        }

        [Test]
        public void should_not_promote_an_existing_name_only_author_during_a_catalog_outage()
        {
            var (author, known) = GivenLegacyAuthor();
            var oldId = author.ForeignAuthorId;
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetAuthorBooks(It.IsAny<string>(), "us"))
                .Throws(new BookInfoException("Catalog unavailable"));

            Assert.Throws<BookInfoException>(() => Subject.GetAuthorInfo(oldId));

            author.ForeignAuthorId.Should().Be(oldId);
            known[0].AuthorMetadata.Value.ForeignAuthorId.Should().Be(oldId);
        }

        [Test]
        public void should_keep_an_exact_owned_recording_with_its_legacy_author_until_promotion()
        {
            var (author, known) = GivenLegacyAuthor();
            var oldId = author.ForeignAuthorId;

            var result = Subject.GetBookInfo(known[0].ForeignBookId);

            result.Item2.Id.Should().Be(42);
            result.Item2.Editions.Value.Single().Id.Should().Be(43);
            result.Item2.AuthorMetadataId.Should().Be(author.AuthorMetadataId);
            result.Item2.AuthorMetadata.Value.ForeignAuthorId.Should().Be(oldId);
            result.Item3.Select(metadata => metadata.ForeignAuthorId).Should().Equal(oldId);
            author.ForeignAuthorId.Should().Be(oldId);
        }

        [Test]
        public void should_reject_conflicting_detail_author_credits_for_the_same_owned_recording()
        {
            var (author, known) = GivenLegacyAuthor();
            var originalId = author.ForeignAuthorId;
            var conflicting = Recording(_resources[0].Asin, _resources[0].Title);
            conflicting.Authors = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = LegacyAuthorName, Asin = "B000000012" }
            };
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(proxy => proxy.GetBook(_resources[0].Asin, "us")).Returns(conflicting);

            Assert.Throws<BookInfoException>(() => Subject.GetBookInfo(known[0].ForeignBookId));
            author.ForeignAuthorId.Should().Be(originalId);
        }

        [Test]
        public void should_keep_the_existing_author_slug_and_ownership_after_a_promotion_is_applied()
        {
            var (author, known) = GivenLegacyAuthor();
            var oldId = author.ForeignAuthorId;
            var proposed = Subject.GetAuthorInfo(oldId);
            var canonicalId = AudiobookId.ForAsin("us", PromotedAuthorAsin);
            proposed.ForeignAuthorId.Should().Be(canonicalId);

            // Model the persisted state after the author refresh applies the proposal.
            // A normal canonical refresh and a new exact-ASIN lookup must use that same row.
            var persisted = new Author { Metadata = proposed.Metadata.Value };
            persisted.UseDbFieldsFrom(proposed);
            known[0].AuthorMetadata = persisted.Metadata.Value;
            known[0].Author = persisted;
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(oldId)).Returns((Author)null);
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(canonicalId)).Returns(persisted);
            Mocker.GetMock<IAudnexusProxy>().Setup(proxy => proxy.GetAuthor(PromotedAuthorAsin, "us"))
                .Returns(new AudnexusAuthorResource { Asin = PromotedAuthorAsin, Region = "us", Name = LegacyAuthorName });

            var refreshed = Subject.GetAuthorInfo(canonicalId);

            refreshed.ForeignAuthorId.Should().Be(canonicalId);
            refreshed.Metadata.Value.TitleSlug.Should().Be(author.Metadata.Value.TitleSlug);
            refreshed.Id.Should().Be(author.Id);
            refreshed.AuthorMetadataId.Should().Be(author.AuthorMetadataId);
            refreshed.IdentityPromotionFrom.Should().BeNull();
            refreshed.IdentityPromotionRecordingId.Should().BeNull();
            refreshed.Books.Value.Should().HaveCount(2);
            var next = Subject.SearchByAsin(_resources[1].Asin).Single();
            next.Author.Value.Id.Should().Be(author.Id);
            next.AuthorMetadataId.Should().Be(author.AuthorMetadataId);
            next.AuthorMetadata.Value.ForeignAuthorId.Should().Be(canonicalId);
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

        [Test]
        public void should_enrich_missing_catalog_synopsis_and_narrators_from_recording_details()
        {
            var details = Recording(_resources[0].Asin, "Different provider title");
            _resources[0].Description = null;
            _resources[0].Narrators = null;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var author = Subject.GetAuthorInfo(AuthorId);
            var book = author.Books.Value.Single(b => b.ForeignBookId == "audible:us:" + details.Asin);

            book.Title.Should().Be("Volume 1");
            book.Editions.Value.Single().Overview.Should().Be("Recording description");
            book.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(details.Asin, "us"), Times.Once());
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(_resources[1].Asin, "us"), Times.Once());
        }

        [Test]
        public void should_fall_back_to_connected_catalog_details_when_audnexus_omits_credits()
        {
            var details = Recording(_resources[0].Asin, "Volume 1");
            _resources[0].Narrators = null;
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks("Yu Omiya", "us")).Returns(_resources);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var book = Subject.GetAuthorInfo(AuthorId).Books.Value.Single(b => b.ForeignBookId == "audible:us:" + details.Asin);

            book.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(details.Asin, "us"), Times.Once());
        }

        [Test]
        public void should_preserve_stored_details_while_periodically_checking_recordings()
        {
            var stored = AudiobookMetadataMapper.Book(Recording(_resources[0].Asin, "Volume 1"), "us");
            stored.Id = 42;
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            _resources[0].Description = null;
            _resources[0].Narrators = null;

            var book = Subject.GetAuthorInfo(AuthorId).Books.Value.Single(b => b.ForeignBookId == stored.ForeignBookId);

            book.Editions.Value.Single().Overview.Should().Be("Recording description");
            book.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            Subject.GetAuthorInfo(AuthorId);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(stored.Editions.Value.Single().Asin, "us"), Times.Once());
        }

        [Test]
        public void should_keep_a_full_stored_synopsis_when_the_catalog_only_provides_a_short_summary()
        {
            var stored = AudiobookMetadataMapper.Book(Recording(_resources[0].Asin, "Volume 1"), "us");
            stored.Id = 42;
            stored.Editions.Value.Single().Overview = "The full stored recording synopsis.";
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            _resources[0].Description = "Short...";
            _resources[0].DescriptionIsSummary = true;

            Subject.SearchByAsin(_resources[0].Asin).Single().Editions.Value.Single().Overview
                .Should().Be("The full stored recording synopsis.");
        }

        [Test]
        public void should_keep_a_longer_stored_synopsis_when_an_extended_description_is_only_an_extract()
        {
            var stored = AudiobookMetadataMapper.Book(Recording(_resources[0].Asin, "Volume 1"), "us");
            stored.Id = 42;
            stored.Editions.Value.Single().Overview = "The full stored recording synopsis with several paragraphs of useful context.";
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            _resources[0].Description = "<p class=\"provider-formatting-with-a-lengthy-attribute-not-part-of-the-description\"><strong>Extract.</strong></p>";

            Subject.SearchByAsin(_resources[0].Asin).Single().Editions.Value.Single().Overview
                .Should().Be(stored.Editions.Value.Single().Overview);
        }

        [Test]
        public void should_not_shorten_a_preserved_synopsis_while_enriching_missing_narrator_credits()
        {
            var asin = _resources[0].Asin;
            var stored = AudiobookMetadataMapper.Book(Recording(asin, "Volume 1"), "us");
            stored.Id = 42;
            stored.Editions.Value.Single().Overview = "The full stored recording synopsis with several paragraphs of useful context.";
            stored.Editions.Value.Single().Narrators.Clear();
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            _resources[0].Description = "Short summary...";
            _resources[0].DescriptionIsSummary = true;
            _resources[0].Narrators = null;
            var details = Recording(asin, "Volume 1");
            details.Description = "<p>First paragraph.</p>";
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Returns(details);

            var edition = Subject.GetAuthorInfo(AuthorId).Books.Value.Single(b => b.ForeignBookId == stored.ForeignBookId).Editions.Value.Single();

            edition.Overview.Should().Be(stored.Editions.Value.Single().Overview);
            edition.Narrators.Should().Equal("David Lee Huynh");
        }

        [Test]
        public void should_upgrade_a_catalog_extract_when_enrichment_has_a_longer_synopsis()
        {
            var details = Recording(_resources[0].Asin, "Volume 1");
            details.Description = "A much longer synopsis from recording details with more context than the catalog extract.";
            _resources[0].Description = "First paragraph.";
            _resources[0].Narrators = null;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var edition = Subject.GetAuthorInfo(AuthorId).Books.Value.Single(b => b.ForeignBookId == "audible:us:" + details.Asin).Editions.Value.Single();

            edition.Overview.Should().Be(details.Description);
        }

        [Test]
        public void should_upgrade_a_short_summary_when_missing_credits_require_a_detail_lookup()
        {
            var details = Recording(_resources[0].Asin, "Volume 1");
            _resources[0].Description = "Short...";
            _resources[0].DescriptionIsSummary = true;
            _resources[0].Narrators = null;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var book = Subject.GetAuthorInfo(AuthorId).Books.Value.Single(b => b.ForeignBookId == "audible:us:" + details.Asin);

            book.Editions.Value.Single().Overview.Should().Be("Recording description");
        }

        [TestCase("asin")]
        [TestCase("marketplace")]
        [TestCase("author")]
        public void should_reject_details_that_belong_to_another_recording_or_author(string mismatch)
        {
            var asin = _resources[0].Asin;
            var details = Recording(asin, "Volume 1");
            _resources[0].Description = null;
            if (mismatch == "asin")
            {
                details.Asin = "B000000001";
            }
            else if (mismatch == "marketplace")
            {
                details.Region = "uk";
            }
            else
            {
                details.Authors[0].Asin = "B000000001";
            }

            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Returns(details);

            Assert.Throws<BookInfoException>(() => Subject.GetAuthorInfo(AuthorId));
            _status.GetStatus().Should().ContainSingle(status => status.Operation == MetadataProviderOperation.AuthorRefresh && status.State == MetadataProviderState.IdentityReview);
        }

        [Test]
        public void should_not_enrich_an_unrelated_homonym_from_an_author_search()
        {
            _resources[0].Authors[0].Asin = "B000000001";
            _resources[0].Description = null;

            Subject.GetAuthorInfo(AuthorId).Books.Value.Should().ContainSingle();

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(_resources[0].Asin, "us"), Times.Never());
        }

        [Test]
        public void should_exclude_explicit_podcasts_from_search_and_bibliographies()
        {
            _resources[0].ContentType = "Podcast";
            _resources[0].ContentDeliveryType = "PodcastParent";

            Subject.SearchForNewBook("Yu Omiya", null).Should().ContainSingle();
            Subject.GetAuthorInfo(AuthorId).Books.Value.Should().ContainSingle();
            Assert.Throws<BookNotFoundException>(() => Subject.GetBookInfo("audible:us:" + _resources[0].Asin));
            Assert.Throws<BookInfoException>(() => AudiobookMetadataMapper.Book(_resources[0], "us"));
        }

        [Test]
        public void should_only_fetch_each_detail_source_once_for_a_recording_refresh()
        {
            var asin = _resources[0].Asin;
            var details = Recording(asin, "Volume 1");
            _resources[0].Narrators = null;
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(asin, "us")).Returns(details);

            var book = Subject.GetBookInfo("audible:us:" + asin).Item2;

            book.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(asin, "us"), Times.Once());
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(asin, "us"), Times.Once());
        }

        [Test]
        public void should_keep_successful_catalog_results_when_optional_enrichment_is_unavailable()
        {
            _resources[0].Description = null;
            _resources[0].Narrators = null;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(_resources[0].Asin, "us"))
                .Throws(new BookInfoException("Unavailable"));
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks("Yu Omiya", "us")).Returns(_resources);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(_resources[0].Asin, "us"))
                .Throws(new BookInfoException("Unavailable"));

            var author = Subject.GetAuthorInfo(AuthorId);

            author.Books.Value.Should().HaveCount(2);
            var edition = author.Books.Value.Single(b => b.ForeignBookId == "audible:us:" + _resources[0].Asin).Editions.Value.Single();
            edition.Overview.Should().BeNull();
            edition.Narrators.Should().BeEmpty();
        }

        [Test]
        public void should_merge_both_series_and_prefer_the_validated_audnexus_primary()
        {
            _resources.RemoveAt(1);
            _resources[0].SeriesPrimary = new AudnexusSeriesResource { Asin = "B0DMXTJ8WH", Name = "The Cosmere" };
            var details = Recording(_resources[0].Asin, "Detail title does not replace the catalog title");
            details.SeriesPrimary = new AudnexusSeriesResource { Asin = "B006K1RP8I", Name = "The Stormlight Archive", Position = "3.5" };
            details.SeriesSecondary = new AudnexusSeriesResource { Asin = "B0DMXTJ8WH", Name = "The Cosmere" };
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var author = Subject.GetAuthorInfo(AuthorId);
            var book = author.Books.Value.Single();

            book.Title.Should().Be("Volume 1");
            book.ForeignBookId.Should().Be("audible:us:" + details.Asin);
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
            book.SeriesLinks.Value.Should().HaveCount(2);
            book.SeriesLinks.Value.Single(link => link.IsPrimary).Series.Value.Title.Should().Be("The Stormlight Archive");
            book.SeriesLinks.Value.Single(link => link.IsPrimary).Position.Should().Be("3.5");
            author.Series.Value.Should().HaveCount(2);
        }

        [Test]
        public void should_keep_an_established_author_when_coauthor_credits_are_reordered()
        {
            _resources[0].Authors.Insert(0, new AudnexusContributorResource { Asin = "B000000099", Name = "Another Author", Role = "Author" });

            var author = Subject.GetAuthorInfo(AuthorId);
            var book = author.Books.Value.Single(item => item.ForeignBookId == "audible:us:" + _resources[0].Asin);

            author.Books.Value.Should().HaveCount(2);
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
            book.Editions.Value.Single().Contributors.Should().Contain(credit => credit.Asin == "B000000099" && credit.Role == "Author");
            book.Editions.Value.Single().Contributors.Should().Contain(credit => credit.Asin == "B0GVW7FF1Q" && credit.Role == "Author");
        }

        [Test]
        public void should_not_treat_an_editor_credit_as_evidence_of_authorship()
        {
            _resources[0].Authors[0].Role = "Editor";
            _resources[0].Authors.Insert(0, new AudnexusContributorResource { Asin = "B000000099", Name = "Another Author", Role = "Author" });

            var author = Subject.GetAuthorInfo(AuthorId);

            author.Books.Value.Should().ContainSingle(book => book.ForeignBookId == "audible:us:" + _resources[1].Asin);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(_resources[0].Asin, "us"), Times.Never());
        }

        [Test]
        public void should_not_reparent_an_existing_recording_during_a_coauthor_bibliography_refresh()
        {
            _resources.RemoveAt(1);
            var stored = AudiobookMetadataMapper.Book(_resources[0], "us");
            stored.Id = 42;
            _resources[0].Authors.Insert(0, new AudnexusContributorResource { Asin = "B000000099", Name = "Another Author", Role = "Author" });
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor("B000000099", "us"))
                .Returns(new AudnexusAuthorResource { Asin = "B000000099", Name = "Another Author", Region = "us" });

            var author = Subject.GetAuthorInfo("audible:us:B000000099");

            author.Books.Value.Should().BeEmpty();
            stored.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
            stored.ForeignBookId.Should().Be("audible:us:" + _resources[0].Asin);
        }

        [Test]
        public void should_reject_changed_author_identity_on_a_known_recording()
        {
            var stored = AudiobookMetadataMapper.Book(_resources[0], "us");
            stored.Id = 42;
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            _resources[0].Authors[0].Asin = "B000000099";
            _resources[0].Authors[0].Name = "Another Author";

            Assert.Throws<BookInfoException>(() => Subject.GetBookInfo(stored.ForeignBookId));
            stored.AuthorMetadata.Value.ForeignAuthorId.Should().Be(AuthorId);
            _status.GetStatus().Should().ContainSingle(status => status.Operation == MetadataProviderOperation.RecordingRefresh && status.State == MetadataProviderState.IdentityReview);
        }

        [Test]
        public void should_enrich_genres_when_catalog_synopsis_and_narrators_are_already_present()
        {
            _resources.RemoveAt(1);
            var details = Recording(_resources[0].Asin, "Volume 1");
            details.Genres = new List<AudnexusGenreResource> { new AudnexusGenreResource { Asin = "18580607011", Name = "Fantasy", Type = "tag" } };
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var book = Subject.GetAuthorInfo(AuthorId).Books.Value.Single();

            book.Genres.Should().Equal("Fantasy");
            book.Classifications.Should().ContainSingle(item => item.Asin == "18580607011" && item.Type == "tag");
        }

        [Test]
        public void should_prefer_a_full_summary_over_a_longer_stored_extract()
        {
            _resources.RemoveAt(1);
            var stored = AudiobookMetadataMapper.Book(_resources[0], "us");
            stored.Id = 42;
            stored.Editions.Value.Single().Overview = "A very long stored extract that still ends before explaining the actual story despite its many characters...";
            Mocker.GetMock<IBookService>().Setup(x => x.FindById(stored.ForeignBookId)).Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(42)).Returns(stored.Editions.Value);
            var details = Recording(_resources[0].Asin, "Volume 1");
            details.Summary = "<p>A complete and useful story synopsis.</p>";
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var book = Subject.GetAuthorInfo(AuthorId).Books.Value.Single();

            book.Editions.Value.Single().Overview.Should().Be("A complete and useful story synopsis.");
            book.ForeignBookId.Should().Be(stored.ForeignBookId);
        }

        [Test]
        public void should_revisit_complete_recordings_and_reuse_detail_results_until_the_cache_expires()
        {
            _resources.RemoveAt(1);
            var details = Recording(_resources[0].Asin, "Volume 1");
            details.Description = "A complete newer synopsis with additional useful context.";
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(details.Asin, "us")).Returns(details);

            var first = Subject.GetAuthorInfo(AuthorId).Books.Value.Single();
            var second = Subject.GetAuthorInfo(AuthorId).Books.Value.Single();

            first.Editions.Value.Single().Overview.Should().Be(details.Description);
            second.Editions.Value.Single().Overview.Should().Be(details.Description);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(details.Asin, "us"), Times.Once());

            foreach (var cache in Mocker.Resolve<ICacheManager>().Caches)
            {
                cache.Clear();
            }

            Subject.GetAuthorInfo(AuthorId);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(details.Asin, "us"), Times.Exactly(2));
        }

        [Test]
        public void should_not_retry_absent_optional_fields_or_404_details_on_every_catalog_refresh()
        {
            _resources.RemoveAt(1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks("Yu Omiya", "us")).Returns(_resources);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(_resources[0].Asin, "us")).Returns((AudnexusBookResource)null);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(_resources[0].Asin, "us")).Returns(_resources[0]);

            Subject.GetAuthorInfo(AuthorId);
            Subject.GetAuthorInfo(AuthorId);

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(_resources[0].Asin, "us"), Times.Once());
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(_resources[0].Asin, "us"), Times.Once());
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetChapters(_resources[0].Asin, "us"), Times.Once());
        }

        [Test]
        public void should_enrich_a_known_small_cover_even_when_other_recording_fields_are_complete()
        {
            var resource = _resources[0];
            resource.Image = "https://m.media-amazon.com/images/I/example._SL500_.jpg";
            resource.PublisherName = "Example Publisher";
            resource.Isbn = "9781603935470";
            resource.Subtitle = "Example subtitle";
            resource.Genres = new List<AudnexusGenreResource> { new AudnexusGenreResource { Name = "Fantasy" } };
            var details = Recording(resource.Asin, "Volume 1");
            details.Image = "https://m.media-amazon.com/images/I/example.jpg";
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(resource.Asin, "us")).Returns(details);

            var book = Subject.GetBookInfo("audible:us:" + resource.Asin).Item2;

            book.Editions.Value.Single().Images.Should().ContainSingle(image => image.Url == details.Image);
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(resource.Asin, "us"), Times.Once());
        }

        [Test]
        public void should_fetch_chapters_only_for_full_recording_lookups_and_reuse_them()
        {
            _resources.RemoveAt(1);
            var asin = _resources[0].Asin;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetChapters(asin, "us")).Returns(new AudnexusChapterResource
            {
                Asin = asin,
                Region = "us",
                RuntimeLengthMs = 90000,
                Chapters = new List<AudnexusChapter> { new AudnexusChapter { Title = "First chapter", Ordinal = 1, StartOffsetMs = 0, LengthMs = 90000 } }
            });

            Subject.SearchForNewBook("Yu Omiya", null);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetChapters(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            var first = Subject.GetBookInfo("audible:us:" + asin).Item2;
            var second = Subject.GetBookInfo("audible:us:" + asin).Item2;

            first.Editions.Value.Single().ChapterMetadata.Chapters.Should().ContainSingle(chapter => chapter.Title == "First chapter");
            second.Editions.Value.Single().ChapterMetadata.RuntimeLengthMs.Should().Be(90000);
            second.Editions.Value.Single().ChapterMetadata.Should().NotBeSameAs(first.Editions.Value.Single().ChapterMetadata);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetChapters(asin, "us"), Times.Once());
        }

        [TestCase("asin")]
        [TestCase("region")]
        [TestCase("timing")]
        public void should_ignore_invalid_optional_chapters_without_discarding_a_recording(string mismatch)
        {
            var asin = _resources[0].Asin;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetChapters(asin, "us")).Returns(new AudnexusChapterResource
            {
                Asin = mismatch == "asin" ? "B000000001" : asin,
                Region = mismatch == "region" ? "uk" : "us",
                Chapters = new List<AudnexusChapter> { new AudnexusChapter { Title = "First chapter", Ordinal = 1, StartOffsetMs = 0, LengthMs = mismatch == "timing" ? -1 : 90000 } }
            });

            var book = Subject.GetBookInfo("audible:us:" + asin).Item2;

            book.Editions.Value.Single().ChapterMetadata.Should().BeNull();
            book.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            book.ForeignBookId.Should().Be("audible:us:" + asin);
        }

        [Test]
        public void should_use_the_enabled_catalog_when_audnexus_recording_details_are_temporarily_unavailable()
        {
            var asin = _resources[0].Asin;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Throws(new BookInfoException("Transport unavailable"));
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(asin, "us")).Returns(_resources[0]);

            var first = Subject.GetBookInfo("audible:us:" + asin).Item2;
            var second = Subject.GetBookInfo("audible:us:" + asin).Item2;

            first.ForeignBookId.Should().Be("audible:us:" + asin);
            second.Editions.Value.Single().Narrators.Should().Equal("David Lee Huynh");
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(asin, "us"), Times.Once());
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(asin, "us"), Times.Once());

            foreach (var cache in Mocker.Resolve<ICacheManager>().Caches)
            {
                cache.Clear();
            }

            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Returns(_resources[0]);
            Subject.GetBookInfo("audible:us:" + asin);
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(asin, "us"), Times.Exactly(2));
        }

        [Test]
        public void should_report_provider_unavailability_instead_of_not_found_when_both_detail_sources_fail()
        {
            var asin = _resources[0].Asin;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Throws(new BookInfoException("Transport unavailable"));
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(asin, "us")).Throws(new BookInfoException("Agent unavailable"));

            var failure = Assert.Throws<BookInfoException>(() => Subject.GetBookInfo("audible:us:" + asin));

            failure.Message.Should().Contain("temporarily unavailable");
            Assert.Throws<BookInfoException>(() => Subject.GetBookInfo("audible:us:" + asin));
            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetBook(asin, "us"), Times.Once());
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(asin, "us"), Times.Once());
        }

        [Test]
        public void should_keep_primary_provider_failure_semantics_when_the_catalog_is_disabled()
        {
            var asin = _resources[0].Asin;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Throws(new BookInfoException("Transport unavailable"));

            Assert.Throws<BookInfoException>(() => Subject.GetBookInfo("audible:us:" + asin));

            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetBook(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_reject_a_fallback_that_returns_a_different_recording()
        {
            var asin = _resources[0].Asin;
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(asin, "us")).Returns((AudnexusBookResource)null);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetBook(asin, "us")).Returns(_resources[1]);

            Assert.Throws<BookInfoException>(() => Subject.GetBookInfo("audible:us:" + asin));

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetChapters(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_keep_a_known_author_and_working_catalog_when_its_optional_profile_is_unavailable()
        {
            GivenStoredAuthorProfile();
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor("B0GVW7FF1Q", "us")).Throws(new BookInfoException("Profile unavailable"));
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks("Yu Omiya", "us")).Returns(_resources);

            var author = Subject.GetAuthorInfo(AuthorId);

            author.ForeignAuthorId.Should().Be(AuthorId);
            author.Metadata.Value.Overview.Should().Be("Stored author biography");
            author.Books.Value.Should().HaveCount(2);
        }

        [Test]
        public void should_not_invent_an_author_identity_during_a_profile_outage()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor("B0GVW7FF1Q", "us")).Throws(new BookInfoException("Profile unavailable"));

            Assert.Throws<BookInfoException>(() => Subject.GetAuthorInfo(AuthorId));

            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetAuthorBooks(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        private static AuthorMetadata RichAuthorMetadata()
        {
            var metadata = AudiobookMetadataMapper.Author(
                new AudnexusAuthorResource
                {
                    Asin = "B0GVW7FF1Q", Name = "Yu Omiya", Region = "us", Description = "Stored author biography",
                    Image = "https://example.com/author.jpg"
                },
                "us");
            metadata.Genres.Add("Fantasy");
            metadata.SimilarAuthors.Add(new AudiobookPersonReference { Name = "Related Author", Asin = "B000000030" });
            metadata.Links.Add(new Links { Name = "Website", Url = "https://example.com/author" });
            return metadata;
        }

        private void GivenStoredAuthorProfile()
        {
            var metadata = RichAuthorMetadata();
            metadata.Id = 6;
            Mocker.GetMock<IAuthorService>().Setup(x => x.FindById(AuthorId))
                .Returns(new Author { Id = 5, AuthorMetadataId = 6, Metadata = metadata });
            Mocker.GetMock<IBookService>().Setup(x => x.GetBooksByAuthorMetadataId(6)).Returns(new List<Book>());
        }

        [Test]
        public void should_preserve_stored_author_details_when_a_profile_response_omits_them()
        {
            GivenStoredAuthorProfile();

            var metadata = Subject.GetAuthorInfo(AuthorId).Metadata.Value;

            metadata.Overview.Should().Be("Stored author biography");
            metadata.Images.Should().ContainSingle(image => image.Url == "https://example.com/author.jpg");
            metadata.Genres.Should().Equal("Fantasy");
            metadata.SimilarAuthors.Should().ContainSingle(person => person.Asin == "B000000030");
            metadata.Links.Should().Contain(link => link.Url == "https://example.com/author");
        }

        [Test]
        public void should_preserve_stored_author_details_in_an_individual_recording_refresh()
        {
            GivenStoredAuthorProfile();

            var metadata = Subject.GetBookInfo("audible:us:" + _resources[0].Asin).Item3.First();

            metadata.Overview.Should().Be("Stored author biography");
            metadata.Images.Should().ContainSingle(image => image.Url == "https://example.com/author.jpg");
            metadata.Genres.Should().Equal("Fantasy");
            metadata.SimilarAuthors.Should().ContainSingle(person => person.Asin == "B000000030");
            metadata.Links.Should().Contain(link => link.Url == "https://example.com/author");
        }

        [Test]
        public void should_preserve_cached_author_profile_details_when_a_recording_has_sparse_credits()
        {
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor("B0GVW7FF1Q", "us"))
                .Returns(new AudnexusAuthorResource
                {
                    Asin = "B0GVW7FF1Q", Name = "Yu Omiya", Region = "us", Description = "Cached author biography",
                    Image = "https://example.com/author.jpg"
                });
            Subject.GetAuthorInfo(AuthorId);

            var metadata = Subject.GetBookInfo("audible:us:" + _resources[0].Asin).Item3.First();

            metadata.Overview.Should().Be("Cached author biography");
            metadata.Images.Should().ContainSingle(image => image.Url == "https://example.com/author.jpg");
        }
    }
}
