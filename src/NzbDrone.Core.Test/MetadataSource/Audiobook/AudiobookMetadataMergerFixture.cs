using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookMetadataMergerFixture
    {
        private static Book Recording(DateTime? date = null)
        {
            return AudiobookMetadataMapper.Book(
                new AudnexusBookResource
                {
                    Asin = "B0B75NY8F2",
                    Region = "us",
                    Title = "A recording",
                    ReleaseDate = date,
                    Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "Example Author", Asin = "B000000001" } }
                },
                "us");
        }

        private static void SeriesLink(Book book, string asin, string name, string position, bool primary)
        {
            var series = new Series { ForeignSeriesId = "audible:us:" + asin, Title = name };
            book.SeriesLinks.Value.Add(new SeriesBookLink
            {
                Book = book,
                Series = series,
                Position = position,
                SeriesPosition = position == null ? 0 : (int)(decimal.Parse(position, System.Globalization.CultureInfo.InvariantCulture) * 1000),
                IsPrimary = primary
            });
        }

        [Test]
        public void should_keep_secondary_links_and_positions_when_catalog_data_are_sparse()
        {
            var incoming = Recording();
            var stored = Recording();
            SeriesLink(incoming, "B0DMXTJ8WH", "The Cosmere", null, true);
            SeriesLink(stored, "B006K1RP8I", "The Stormlight Archive", "3.5", true);
            SeriesLink(stored, "B0DMXTJ8WH", "The Cosmere", null, false);

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value, true);

            incoming.SeriesLinks.Value.Should().HaveCount(2);
            incoming.SeriesLinks.Value.Single(link => link.IsPrimary).Series.Value.Title.Should().Be("The Stormlight Archive");
            incoming.SeriesLinks.Value.Single(link => link.IsPrimary).Position.Should().Be("3.5");
            incoming.SeriesLinks.Value.Should().OnlyContain(link => link.Book.Value == incoming);
            incoming.SeriesLinks.Value[0].Position = "Changed locally";
            stored.SeriesLinks.Value[0].Position.Should().Be("3.5");
        }

        [Test]
        public void should_not_merge_same_named_series_with_different_explicit_identifiers()
        {
            var incoming = Recording();
            var supplemental = Recording();
            SeriesLink(incoming, "B000000010", "Different order", "2", true);
            SeriesLink(supplemental, "B000000011", "Different order", "5", true);

            AudiobookMetadataMerger.MergeSeries(incoming, supplemental, true);

            incoming.SeriesLinks.Value.Should().HaveCount(2);
            incoming.SeriesLinks.Value.Single(link => link.IsPrimary).Series.Value.ForeignSeriesId.Should().Be("audible:us:B000000011");
            incoming.AuthorMetadata.Value.ForeignAuthorId.Should().Be("audible:us:B000000001");
            incoming.ForeignBookId.Should().Be("audible:us:B0B75NY8F2");
        }

        [Test]
        public void should_preserve_rich_metadata_and_upgrade_unambiguous_name_only_credits_without_aliasing()
        {
            var incoming = Recording();
            var stored = Recording();
            var edition = incoming.Editions.Value.Single();
            var previous = stored.Editions.Value.Single();
            edition.Contributors = new List<AudiobookContributor> { new AudiobookContributor { Name = "Example Reader", Role = "Narrator" } };
            previous.Contributors = new List<AudiobookContributor> { new AudiobookContributor { Name = "Example Reader", Role = "Narrator", Asin = "B000000020" } };
            previous.CopyrightYear = 2021;
            previous.IsAdult = false;
            previous.LiteratureType = "fiction";
            previous.ChapterMetadata = new AudiobookChapterMetadata
            {
                RuntimeLengthMs = 90000,
                Chapters = new List<AudiobookChapter> { new AudiobookChapter { Title = "Chapter", Ordinal = 1, StartOffsetMs = 0, LengthMs = 90000 } }
            };
            stored.Classifications = new List<AudiobookClassification> { new AudiobookClassification { Name = "Fantasy", Asin = "18580607011", Type = "tag" } };

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            edition.Contributors.Should().ContainSingle(item => item.Asin == "B000000020" && item.Role == "Narrator");
            edition.CopyrightYear.Should().Be(2021);
            edition.IsAdult.Should().BeFalse();
            edition.LiteratureType.Should().Be("fiction");
            incoming.Classifications.Should().ContainSingle(item => item.Type == "tag" && item.Asin == "18580607011");
            edition.ChapterMetadata.Chapters.Should().ContainSingle();
            edition.ChapterMetadata.Should().NotBeSameAs(previous.ChapterMetadata);
            edition.ChapterMetadata.Chapters[0].Title = "Changed locally";
            previous.ChapterMetadata.Chapters[0].Title.Should().Be("Chapter");
            edition.Contributors[0].Name = "Changed locally";
            previous.Contributors[0].Name.Should().Be("Example Reader");
        }

        [Test]
        public void should_keep_conflicting_contributor_identifiers_separate()
        {
            var incoming = Recording();
            var stored = Recording();
            incoming.Editions.Value.Single().Contributors = new List<AudiobookContributor>
            {
                new AudiobookContributor { Name = "Same Name", Role = "Author", Asin = "B000000021" }
            };
            stored.Editions.Value.Single().Contributors = new List<AudiobookContributor>
            {
                new AudiobookContributor { Name = "Same Name", Role = "Author", Asin = "B000000022" }
            };

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            incoming.Editions.Value.Single().Contributors.Should().HaveCount(2);
            incoming.Editions.Value.Single().Contributors.Select(item => item.Asin).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_keep_all_narrators_in_both_legacy_names_and_typed_credits()
        {
            var incoming = Recording();
            var details = Recording();
            incoming.Editions.Value.Single().Narrators = new List<string> { "First Reader - narrator" };
            details.Editions.Value.Single().Narrators = new List<string> { "First Reader", "Second Reader" };
            details.Editions.Value.Single().Contributors = new List<AudiobookContributor>
            {
                new AudiobookContributor { Name = "First Reader", Role = "Narrator", Asin = "B000000040" },
                new AudiobookContributor { Name = "Second Reader", Role = "Narrator", Asin = "B000000041" }
            };

            AudiobookMetadataMerger.PreserveMissing(incoming, details, details.Editions.Value);

            incoming.Editions.Value.Single().Narrators.Should().Equal("First Reader", "Second Reader");
            incoming.Editions.Value.Single().Contributors.Where(credit => credit.Role == "Narrator").Should().HaveCount(2);
        }

        [Test]
        public void should_merge_similar_authors_by_identity_without_losing_ids_or_sharing_mutable_references()
        {
            var incoming = new List<AudiobookPersonReference> { new AudiobookPersonReference { Name = "Related Author" } };
            var stored = new List<AudiobookPersonReference> { new AudiobookPersonReference { Name = "Related Author", Asin = "B000000030" } };

            var merged = AudiobookMetadataMerger.MergePeopleReferences(incoming, stored);

            merged.Should().ContainSingle(person => person.Asin == "B000000030");
            merged[0].Name = "Changed locally";
            incoming[0].Name.Should().Be("Related Author");
            stored[0].Name.Should().Be("Related Author");

            incoming[0].Asin = "B000000031";
            AudiobookMetadataMerger.MergePeopleReferences(incoming, stored).Should().HaveCount(2);
        }

        [Test]
        public void should_not_copy_recording_metadata_across_marketplaces_or_asins()
        {
            var incoming = Recording();
            var other = Recording();
            other.ForeignBookId = "audible:uk:B0B75NY8F2";
            other.Editions.Value.Single().CopyrightYear = 2021;
            other.Genres.Add("Other marketplace genre");
            SeriesLink(other, "B006K1RP8I", "Other marketplace series", "1", true);

            AudiobookMetadataMerger.PreserveMissing(incoming, other, other.Editions.Value);

            incoming.Genres.Should().BeEmpty();
            incoming.SeriesLinks.Value.Should().BeEmpty();
            incoming.Editions.Value.Single().CopyrightYear.Should().BeNull();
        }

        [Test]
        public void should_keep_a_confirmed_recording_date_when_new_data_only_have_a_placeholder()
        {
            var confirmed = new DateTime(2027, 2, 3);
            var incoming = Recording(new DateTime(2050, 12, 30));
            var stored = Recording(confirmed);

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            incoming.ReleaseDate.Should().Be(confirmed);
            incoming.Editions.Value.Single().ReleaseDate.Should().Be(confirmed);
            incoming.Editions.Value.Single().SourceReleaseDate.Should().Be(confirmed);
            incoming.Editions.Value.Single().ReleaseDateIsPlaceholder.Should().BeFalse();
        }

        [Test]
        public void should_clear_a_legacy_placeholder_date_but_preserve_its_source_value()
        {
            var placeholder = new DateTime(2050, 12, 30);
            var incoming = Recording();
            var stored = Recording();
            stored.ReleaseDate = placeholder;
            stored.Editions.Value.Single().ReleaseDate = placeholder;

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            incoming.ReleaseDate.Should().BeNull();
            incoming.Editions.Value.Single().ReleaseDate.Should().BeNull();
            incoming.Editions.Value.Single().SourceReleaseDate.Should().Be(placeholder);
            incoming.Editions.Value.Single().ReleaseDateIsPlaceholder.Should().BeTrue();
        }

        [Test]
        public void should_replace_a_placeholder_with_a_confirmed_date_without_rejecting_other_future_dates()
        {
            var future = new DateTime(2051, 1, 1);
            var incoming = Recording(future);
            var stored = Recording(new DateTime(2050, 12, 30));

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            incoming.ReleaseDate.Should().Be(future);
            incoming.Editions.Value.Single().SourceReleaseDate.Should().Be(future);
            incoming.Editions.Value.Single().ReleaseDateIsPlaceholder.Should().BeFalse();
        }
    }
}
