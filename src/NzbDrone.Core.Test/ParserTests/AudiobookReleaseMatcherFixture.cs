using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class AudiobookReleaseMatcherFixture : CoreTest
    {
        private static Book Recording(string asin, string title, string position, string narrator = "Reader One")
        {
            return AudiobookMetadataMapper.Book(new AudnexusBookResource
            {
                Asin = asin, Region = "us", Title = title,
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "Example Author" } },
                Narrators = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = narrator } },
                SeriesPrimary = new AudnexusSeriesResource { Name = "Example Series", Position = position }
            }, "us");
        }

        private static ReleaseInfo Release(string title, string position, string narrator = "Reader One") => new ReleaseInfo
        {
            Book = title,
            Author = "Example Author",
            Narrators = new List<string> { narrator },
            BookSeries = new List<ReleaseBookSeries> { new ReleaseBookSeries { Name = "Example Series", Position = position } }
        };

        [Test]
        public void should_use_series_positions_to_disambiguate_identical_provider_titles()
        {
            var first = Recording("B000000001", "Example Series", "1");
            var second = Recording("B000000002", "Example Series", "2");

            AudiobookReleaseMatcher.Match(Release("Example Series, Vol. 2", "2.0"), new List<Book> { first, second }, out _)
                .Should().Equal(second);
        }

        [Test]
        public void should_match_companion_titles_without_turning_them_into_volume_one()
        {
            var first = Recording("B000000001", "Example Series", "1");
            var companion = Recording("B000000003", "Example Series: Recollections (Light Novel)", null);

            AudiobookReleaseMatcher.Match(Release("Example Series - Recollections", "27"), new List<Book> { first, companion }, out _)
                .Should().Equal(companion);
        }

        [TestCase("Example Series, Vol. 1", "2")]
        [TestCase("Example Series, Vol. 2", "1")]
        [TestCase("Example Series, Vol. 1-2", "1-2")]
        public void should_reject_conflicting_numbers_and_collections(string title, string position)
        {
            var books = new List<Book>
            {
                Recording("B000000001", "Example Series, Vol. 1", "1"),
                Recording("B000000002", "Example Series, Vol. 2", "2")
            };

            AudiobookReleaseMatcher.Match(Release(title, position), books, out _).Should().BeEmpty();
        }

        [Test]
        public void should_not_approve_a_pack_when_only_one_generic_book_title_is_in_the_library()
        {
            var first = Recording("B000000001", "Example Series", "1");

            AudiobookReleaseMatcher.Match(Release("Example Series", "01-16, 13.5"), new List<Book> { first }, out var rejection)
                .Should().BeEmpty();
            rejection.Should().Contain("pack");
        }

        [Test]
        public void should_resolve_different_recordings_using_structured_narrators()
        {
            var first = Recording("B000000001", "Example Series, Book 1", "1", "Reader One");
            var second = Recording("B000000002", "Example Series, Book 1", "1", "Reader Two");

            AudiobookReleaseMatcher.Match(Release("Example Series", "1", "Reader Two"), new List<Book> { first, second }, out _)
                .Should().Equal(second);
        }

        [Test]
        public void should_not_choose_the_requested_recording_when_narration_is_ambiguous()
        {
            var books = new List<Book>
            {
                Recording("B000000001", "Example Series, Book 1", "1", "Reader One"),
                Recording("B000000002", "Example Series, Book 1", "1", "Reader Two")
            };
            var release = Release("Example Series", "1");
            release.Narrators.Clear();

            AudiobookReleaseMatcher.Match(release, books, out var rejection).Should().BeEmpty();
            rejection.Should().Contain("Ambiguous");
        }

        [Test]
        public void should_reject_a_conflicting_narrator()
        {
            var book = Recording("B000000001", "Example Series, Book 1", "1", "Reader One");

            AudiobookReleaseMatcher.Match(Release("Example Series", "1", "Another Reader"), new List<Book> { book }, out _)
                .Should().BeEmpty();
        }

        [Test]
        public void should_use_verified_aliases_without_changing_provider_narrator_names()
        {
            var book = Recording("B000000001", "Example Series, Book 1", "1", "Reader One");
            var release = Release("Example Series", "1", "Reader Alias");

            AudiobookReleaseMatcher.Match(release, new List<Book> { book }, out _, name => name == "Reader Alias" ? "Reader One" : name)
                .Should().Equal(book);
            release.Narrators.Should().Equal("Reader Alias");
            book.Editions.Value[0].Narrators.Should().Equal("Reader One");
        }

        [Test]
        public void should_not_assume_an_abridged_release_is_unabridged()
        {
            var book = Recording("B000000001", "Example Series, Book 1", "1");
            book.Editions.Value[0].Disambiguation = "unabridged";

            AudiobookReleaseMatcher.Match(Release("Example Series (Abridged)", "1"), new List<Book> { book }, out _)
                .Should().BeEmpty();
        }

        [Test]
        public void should_match_a_standalone_title_without_fuzzy_prefix_matching()
        {
            var first = Recording("B000000001", "A Distinctive Story", null);
            var second = Recording("B000000002", "A Distinctive Story: Recollections", null);
            var release = Release("A Distinctive Story", null);
            release.BookSeries.Clear();

            AudiobookReleaseMatcher.Match(release, new List<Book> { first, second }, out _).Should().Equal(first);
        }
    }
}
