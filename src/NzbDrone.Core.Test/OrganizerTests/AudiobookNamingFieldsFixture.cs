using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class AudiobookNamingFieldsFixture
    {
        private static Edition Edition(string title, string subtitle, string series, string position)
        {
            var book = new Book { Title = title, SeriesLinks = new List<SeriesBookLink>() };
            if (series != null)
            {
                book.SeriesLinks.Value.Add(new SeriesBookLink { IsPrimary = true, Series = new Series { Title = series }, Position = position });
            }

            return new Edition { Title = title, Subtitle = subtitle, Book = book };
        }

        private static AudiobookNamingFields Fields(Edition edition, string title)
        {
            var rules = AudiobookMetadataCleanupRuleSeed.Read();
            foreach (var rule in rules)
            {
                rule.Enabled = true;
            }

            var projection = AudiobookMetadataCleanup.Project(edition.Book?.Value, edition, rules, title != edition.Title ? title : null);
            return AudiobookNamingFields.From(edition, projection);
        }

        [TestCase("Memory's Flame", "The Wandering Inn, Book 20", "The Wandering Inn", "20", "The Wandering Inn vol_20 Memory's Flame", "Memory's Flame")]
        [TestCase("Sword Art Online #1", "Aincrad", "Sword Art Online", "1", "Sword Art Online vol_01 Aincrad", "Aincrad")]
        [TestCase("Sword Art Online (Light Novel), Vol. 1", "Aincrad", "Sword Art Online", "1", "Sword Art Online vol_01 Aincrad", "Aincrad")]
        [TestCase("The Primal Hunter #4", "A LitRPG Adventure", "The Primal Hunter", "4", "The Primal Hunter vol_04", "The Primal Hunter #4")]
        [TestCase("Primal Hunter 4", null, "The Primal Hunter", "4", "The Primal Hunter vol_04", "The Primal Hunter #4")]
        [TestCase("Series #1: A Distinct Work", null, "Series", "1", "Series vol_01 A Distinct Work", "A Distinct Work")]
        [TestCase("Series #01.0: A Distinct Work", null, "Series", "1", "Series vol_01 A Distinct Work", "A Distinct Work")]
        [TestCase("Amongst the Fallen", null, "The Beginning After the End", "8.5", "The Beginning After the End vol_08.5 Amongst the Fallen", "Amongst the Fallen")]
        [TestCase("Series, Books 1-3.5", null, "Series", "1-3.5", "Series vol_01-03.5", "Series #1-3.5")]
        [TestCase("Re:ZERO", null, "Re:ZERO", "2", "Re:ZERO vol_02", "Re:ZERO #2")]
        [TestCase("Re:ZERO: Starting Life in Another World", null, "Other Series", "2", "Other Series vol_02 Re:ZERO: Starting Life in Another World", "Re:ZERO: Starting Life in Another World")]
        public void should_render_confirmed_series_fields_without_losing_work_titles(string title, string subtitle, string series, string position, string expectedTitle, string expectedWork)
        {
            var edition = Edition(title, subtitle, series, position);

            var fields = Fields(edition, title);

            fields.Title.Should().Be(expectedTitle);
            fields.WorkTitle.Should().Be(expectedWork);
            edition.Title.Should().Be(title);
            edition.Subtitle.Should().Be(subtitle);
            edition.Book.Value.SeriesLinks.Value[0].Position.Should().Be(position);
        }

        [TestCase("The Primal Hunter 9", null, "The Primal Hunter", "4")]
        [TestCase("The Primal Hunter #4", "The Primal Hunter, Book 9", "The Primal Hunter", "4")]
        [TestCase("Season of Storms", null, "The Witcher", null)]
        [TestCase("An Unnumbered Companion", null, "Series", "Companion")]
        [TestCase("A Standalone", "A Separate Subtitle", null, null)]
        [TestCase("Sword Art Online: Progressive", null, "Sword Art Online", "2")]
        public void should_retain_titles_when_position_or_structural_separation_is_uncertain(string title, string subtitle, string series, string position)
        {
            var fields = Fields(Edition(title, subtitle, series, position), title);

            fields.Title.Should().Be(title);
            fields.WorkTitle.Should().Be(title);
        }

        [TestCase("Main: Useful Subtitle", "Useful Subtitle", "Main: Useful Subtitle")]
        [TestCase("Main - Useful Subtitle", "Useful Subtitle", "Main - Useful Subtitle")]
        [TestCase("The Art of War", "War", "The Art of War: War")]
        [TestCase("Pre-War", "War", "Pre-War: War")]
        [TestCase("Main", "A LitRPG Adventure", "Main")]
        [TestCase("Main", "A LitRPG Adventure in Space", "Main: A LitRPG Adventure in Space")]
        public void should_deduplicate_only_complete_subtitle_components(string title, string subtitle, string expectedWork)
        {
            var fields = Fields(Edition(title, subtitle, "Series", "1"), title);

            fields.Title.Should().Be("Series vol_01 " + expectedWork);
            fields.WorkTitle.Should().Be(expectedWork);
        }

        [Test]
        public void should_prefer_primary_series_over_legacy_order()
        {
            var edition = Edition("A Work", null, "Primary", "2");
            edition.Book.Value.SeriesLinks.Value.Insert(0, new SeriesBookLink { SeriesPosition = -1, Series = new Series { Title = "Secondary" }, Position = "9" });

            Fields(edition, "A Work").Title.Should().Be("Primary vol_02 A Work");
        }

        [Test]
        public void should_fall_back_to_legacy_series_order()
        {
            var edition = Edition("A Work", null, "First", "2");
            edition.Book.Value.SeriesLinks.Value[0].IsPrimary = false;
            edition.Book.Value.SeriesLinks.Value[0].SeriesPosition = 1;
            edition.Book.Value.SeriesLinks.Value.Add(new SeriesBookLink { SeriesPosition = 0, Series = new Series { Title = "Legacy Primary" }, Position = "3" });

            Fields(edition, "A Work").Title.Should().Be("Legacy Primary vol_03 A Work");
        }

        [Test]
        public void should_use_the_supplied_corrected_display_title()
        {
            var edition = Edition("Series, Vol. 18", null, "Series", "18");

            Fields(edition, "Corrected Work").Title.Should().Be("Series vol_18 Corrected Work");
            edition.Title.Should().Be("Series, Vol. 18");
        }

        [Test]
        public void should_not_append_a_duplicate_original_title_after_a_correction()
        {
            var edition = Edition("Provider typo", "Provider typo", "Series", "1");

            Fields(edition, "Corrected Work").Title.Should().Be("Series vol_01 Corrected Work");
            edition.Subtitle.Should().Be("Provider typo");
        }

        [Test]
        public void should_not_repeat_a_series_name_split_across_source_title_and_subtitle()
        {
            var edition = Edition("Mushoku Tensei", "Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation", "1");

            var fields = Fields(edition, "Mushoku Tensei: Jobless Reincarnation #1");

            fields.Title.Should().Be("Mushoku Tensei: Jobless Reincarnation vol_01");
            fields.WorkTitle.Should().Be("Mushoku Tensei: Jobless Reincarnation #1");
        }

        [Test]
        public void should_return_non_null_tokens_when_optional_metadata_is_absent()
        {
            var fields = Fields(new Edition(), null);

            fields.Title.Should().BeEmpty();
            fields.WorkTitle.Should().BeEmpty();
            AudiobookNamingFields.FormatPosition(null, null).Should().BeEmpty();
        }

        [TestCase("3", "00", "03")]
        [TestCase("12", "00", "12")]
        [TestCase("3", "000", "003")]
        [TestCase("8.50", "00", "08.50")]
        [TestCase("0.5", "00", "0.5")]
        [TestCase("1-3.5", "00", "01-03.5")]
        [TestCase("0.5-2.5", "00", "0.5-02.5")]
        [TestCase("1-1", "00", "01-01")]
        [TestCase("1-2", "000", "001-002")]
        [TestCase("Companion", "00", "Companion")]
        [TestCase("1,2", "00", "1,2")]
        [TestCase("1-2-3", "00", "1-2-3")]
        [TestCase("1", "0.0", "1")]
        [TestCase("1", "D2", "1")]
        [TestCase("1", "", "1")]
        [TestCase("1", null, "1")]
        [TestCase(null, "00", "")]
        public void should_format_explicit_positions_without_inventing_structure(string position, string format, string expected)
        {
            AudiobookNamingFields.FormatPosition(position, format).Should().Be(expected);
        }
    }
}
