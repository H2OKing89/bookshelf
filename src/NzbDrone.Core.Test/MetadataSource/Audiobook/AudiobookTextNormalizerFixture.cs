using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookTextNormalizerFixture : CoreTest
    {
        [TestCase("B09MWP4277", "The Primal Hunter #1")]
        [TestCase("B0BK9W82PQ", "The Primal Hunter #4")]
        [TestCase("B0CLNJQQD7", "The Primal Hunter #7")]
        [TestCase("B0D6H2N1YL", "The Primal Hunter #9")]
        public void should_build_consistent_labels_without_changing_published_titles(string asin, string expected)
        {
            var fixture = JObject.Parse(ReadAllText($"Files/MetadataSource/Audiobook/{asin}.json"));
            var resource = Json.Deserialize<AudnexusBookResource>(fixture["audnexus"].ToString());
            var book = AudiobookMetadataMapper.Book(resource, "us");
            var originalId = book.ForeignBookId;

            AudiobookTextNormalizer.DisplayTitle(book).Should().Be(expected);

            book.Title.Should().Be(resource.Title);
            book.Editions.Value.Single().Title.Should().Be(resource.Title);
            book.ForeignBookId.Should().Be(originalId);
        }

        [TestCase("B0H8F9D4GD", "1")]
        [TestCase("B0HMVRXW3C", "2")]
        public void should_use_yu_omiya_series_positions_despite_title_variations(string asin, string position)
        {
            var fixture = JObject.Parse(ReadAllText($"Files/MetadataSource/Audiobook/{asin}.json"));
            var resource = Json.Deserialize<AudnexusBookResource>(fixture["audnexus"].ToString());
            var book = AudiobookMetadataMapper.Book(resource, "us");
            AudiobookTextNormalizer.DisplayTitle(book).Should().Be(resource.SeriesPrimary.Name + " #" + position);
            book.Title.Should().Be(resource.Title);
        }

        [TestCase("The Primal Hunter 4: The Final Battle", "4", "The Primal Hunter #4: The Final Battle")]
        [TestCase("A Completely Different Story", "4", "A Completely Different Story")]
        [TestCase("The Primal Hunter 9", "4", "The Primal Hunter 9")]
        [TestCase("The Primal Hunter", "1-3", "The Primal Hunter")]
        [TestCase("The Primal Hunter", "1,2", "The Primal Hunter")]
        [TestCase("The Primal Hunter 0.5", "0.50", "The Primal Hunter #0.5")]
        [TestCase("The Primal Hunter", "0", "The Primal Hunter #0")]
        [TestCase("The Primal Hunter", null, "The Primal Hunter")]
        public void should_keep_distinctive_titles_and_avoid_guessing_positions(string title, string position, string expected)
        {
            var fixture = JObject.Parse(ReadAllText("Files/MetadataSource/Audiobook/B0BK9W82PQ.json"));
            var resource = Json.Deserialize<AudnexusBookResource>(fixture["audnexus"].ToString());
            resource.Title = title;
            resource.SeriesPrimary.Position = position;
            var book = AudiobookMetadataMapper.Book(resource, "us");
            AudiobookTextNormalizer.DisplayTitle(book).Should().Be(expected);
        }

        [Test]
        public void should_normalize_text_fields_while_preserving_recording_and_author_identities()
        {
            var resource = new AudnexusBookResource
            {
                Asin = "B0H8F9D4GD", Region = "us", Title = "Original Published Title",
                Language = " English ", PublisherName = " Seven Seas Entertainment,Seven Seas Siren ",
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = "B0GVW7FF1Q", Name = "  Yu\u00a0 Omiya  " } },
                Narrators = new List<AudnexusContributorResource>
                {
                    new AudnexusContributorResource { Name = " David  Lee Huynh " },
                    new AudnexusContributorResource { Name = "david lee huynh" },
                    new AudnexusContributorResource { Name = " " }
                },
                Genres = new List<AudnexusGenreResource> { new AudnexusGenreResource { Name = " Fantasy " }, new AudnexusGenreResource { Name = "fantasy" } }
            };
            var book = AudiobookMetadataMapper.Book(resource, "us");
            var edition = book.Editions.Value.Single();
            book.ForeignBookId.Should().Be("audible:us:B0H8F9D4GD");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be("audible:us:B0GVW7FF1Q");
            book.AuthorMetadata.Value.Name.Should().Be("Yu Omiya");
            book.AuthorMetadata.Value.NameLastFirst.Should().Be("Yu Omiya");
            edition.Publisher.Should().Be("Seven Seas Entertainment, Seven Seas Siren");
            edition.Language.Should().Be("eng");
            edition.Narrators.Should().Equal("David Lee Huynh");
            book.Genres.Should().Equal("Fantasy");
            resource.PublisherName.Should().Be(" Seven Seas Entertainment,Seven Seas Siren ");
            resource.Authors[0].Name.Should().Be("  Yu\u00a0 Omiya  ");
        }

        [TestCase("Zogarth", "Zogarth")]
        [TestCase("Jose\u0301  Example", "José Example")]
        public void should_preserve_pen_names_and_normalize_unicode_without_inverting_names(string input, string expected)
        {
            AudiobookTextNormalizer.Text(input).Should().Be(expected);
        }
    }
}
