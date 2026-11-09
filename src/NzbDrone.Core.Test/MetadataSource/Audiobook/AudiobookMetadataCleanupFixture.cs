using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookMetadataCleanupFixture
    {
        private static Edition Recording(string title, string subtitle = null, string series = "Series", string position = "1")
        {
            var book = new Book { Title = title, SeriesLinks = new List<SeriesBookLink>() };
            if (series != null)
            {
                book.SeriesLinks.Value.Add(new SeriesBookLink { IsPrimary = true, Series = new Series { Title = series }, Position = position });
            }

            var edition = new Edition { Title = title, Subtitle = subtitle, Book = book, Monitored = true };
            book.Editions = new List<Edition> { edition };
            return edition;
        }

        private static List<AudiobookMetadataCleanupRule> Rules()
        {
            var rules = AudiobookMetadataCleanupRuleSeed.Read();
            for (var index = 0; index < rules.Count; index++)
            {
                rules[index].Id = index + 1;
                rules[index].Enabled = true;
            }

            return rules;
        }

        private static AudiobookMetadataProjection Project(Edition edition) => AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules());

        [Test]
        [SetCulture("ar-SA")]
        public void should_warn_about_distant_provider_dates_without_changing_metadata()
        {
            var edition = Recording("Main");
            var releaseDate = new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc);
            edition.ReleaseDate = releaseDate;
            edition.Book.Value.ReleaseDate = releaseDate;
            edition.Asin = "B012345678";

            var result = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), today: new DateTime(2026, 11, 8));

            result.Warnings.Should().Equal("The provider release date (2050-12-30) is more than 10 years away. Verify it with the publisher before relying on it.");
            result.Changes.Should().BeEmpty();
            edition.ReleaseDate.Should().Be(releaseDate);
            edition.Book.Value.ReleaseDate.Should().Be(releaseDate);
            edition.Asin.Should().Be("B012345678");
            edition.Monitored.Should().BeTrue();
        }

        [TestCase(-1, false)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        public void should_warn_only_after_ten_calendar_years(int daysAfterThreshold, bool expected)
        {
            var today = new DateTime(2026, 11, 8);
            var edition = Recording("Main");
            edition.ReleaseDate = today.AddYears(10).AddDays(daysAfterThreshold).AddHours(12);

            var result = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), today: today);

            result.Warnings.Any().Should().Be(expected);
        }

        [Test]
        public void should_not_warn_about_missing_release_dates()
        {
            var edition = Recording("Main");

            AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), today: new DateTime(2026, 11, 8))
                .Warnings.Should().BeEmpty();
        }

        [Test]
        public void should_retain_the_date_warning_when_title_cleanup_finds_a_series_conflict()
        {
            var edition = Recording("Series, Book 2", series: "Series", position: "1");
            edition.Book.Value.ReleaseDate = new DateTime(2050, 12, 30);

            var result = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), today: new DateTime(2026, 11, 8));

            result.Warnings.Should().Contain(warning => warning.Contains("provider release date (2050-12-30)"));
            result.Warnings.Should().Contain(warning => warning.Contains("conflicts with the catalog series position"));
            edition.Book.Value.ReleaseDate.Should().Be(new DateTime(2050, 12, 30));
        }

        [TestCase("Main: A LitRPG Adventure", "Main", "LitRPG")]
        [TestCase("Main - A Fantasy LitRPG Adventure", "Main", "LitRPG")]
        [TestCase("Main (LIGHT NOVEL)", "Main", "Light Novel")]
        [TestCase("Main: A Novel", "Main", null)]
        [TestCase("Main: A Slice-of-Life Harem LitRPG", "Main", "LitRPG")]
        [TestCase("Main: A Slice of Life Harem", "Main", null)]
        [TestCase("Main: The International Best Seller", "Main", null)]
        public void should_remove_only_complete_recognized_labels(string title, string expected, string label)
        {
            var edition = Recording(title);
            var result = Project(edition);

            result.Title.Should().Be(expected);
            result.DisplayTitle.Should().Be(expected);
            result.MetadataLabels.Should().Equal(label == null ? new string[0] : new[] { label });
            result.Changes.Should().Contain(change => change.Field == "title" && change.OriginalText == title && change.RuleId.HasValue);
            edition.Title.Should().Be(title);
            edition.Book.Value.Title.Should().Be(title);
        }

        [TestCase("The Light Novel Club")]
        [TestCase("LitRPG Design Handbook")]
        [TestCase("Main: A LitRPG Adventure in Space")]
        [TestCase("Sword Art Online: Progressive")]
        [TestCase("Re:ZERO")]
        [TestCase("86--EIGHTY-SIX")]
        [TestCase("A Light Novel Journey")]
        public void should_preserve_meaningful_titles_and_unknown_phrases(string title)
        {
            var result = Project(Recording(title));

            result.Title.Should().Be(title);
            result.DisplayTitle.Should().Be(title);
            result.Changes.Should().BeEmpty();
            result.MetadataLabels.Should().BeEmpty();
        }

        [TestCase("Light Novel", "Light Novel")]
        [TestCase("a  litrpg adventure", "LitRPG")]
        [TestCase("A Sci-Fi Light Novel", "Light Novel")]
        [TestCase("A Slice of Life Harem LitRPG", "LitRPG")]
        public void should_clear_promotional_subtitles_without_falling_back_to_the_raw_value(string subtitle, string label)
        {
            var edition = Recording("A Distinctive Work", subtitle);
            var result = Project(edition);

            result.Subtitle.Should().BeEmpty();
            result.Title.Should().Be("A Distinctive Work");
            result.MetadataLabels.Should().Equal(label);
            edition.Subtitle.Should().Be(subtitle);
        }

        [Test]
        public void should_deduplicate_labels_but_keep_each_source_field_as_evidence()
        {
            var result = Project(Recording("Main (Light Novel)", "A Light Novel", "Series (Light Novel)"));

            result.Series.Should().Be("Series");
            result.MetadataLabels.Should().Equal("Light Novel");
            result.Changes.Select(change => change.Field).Should().Contain(new[] { "title", "subtitle", "series" });
        }

        [TestCase("A Novel")]
        [TestCase("Light Novel")]
        [TestCase("(Light Novel)")]
        public void should_retain_a_title_when_cleanup_would_empty_it(string title)
        {
            var result = Project(Recording(title));

            result.Title.Should().Be(title);
            result.MetadataLabels.Should().BeEmpty();
            result.Changes.Should().BeEmpty();
            result.Warnings.Should().NotBeEmpty();
        }

        [Test]
        public void should_honor_disabled_rules_in_every_field()
        {
            var edition = Recording("Main (Light Novel)", "A LitRPG Adventure", "Series (Light Novel)");
            var result = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, AudiobookMetadataCleanupRuleSeed.Read());

            result.Title.Should().Be(edition.Title);
            result.Subtitle.Should().Be(edition.Subtitle);
            result.Series.Should().Be("Series (Light Novel)");
            result.MetadataLabels.Should().BeEmpty();
        }

        [TestCase("Series, Book 1", "1")]
        [TestCase("Series Book 1", "1")]
        [TestCase("Series, Volumes 1-3.5", "1-3.5")]
        [TestCase("Series, Volume 8.50", "8.5")]
        [TestCase("Series", "1")]
        public void should_remove_confirmed_catalog_citations_before_promotional_labels(string citation, string position)
        {
            var result = Project(Recording("Main: A LitRPG Adventure (" + citation + ")", null, "Series", position));

            result.Title.Should().Be("Main");
            result.MetadataLabels.Should().Equal("LitRPG");
            result.Changes.Should().Contain(change => change.RemovedText.Contains(citation) && !change.RuleId.HasValue);
        }

        [TestCase("Series, Book 9", "1")]
        [TestCase("Series, Books 1-3.5", "1-3")]
        [TestCase("Other Series, Book 1", "1")]
        public void should_preserve_unconfirmed_citations_and_their_context(string citation, string position)
        {
            var title = "Main: A LitRPG Adventure (" + citation + ")";
            var result = Project(Recording(title, null, "Series", position));

            result.Title.Should().Be(title);
            result.MetadataLabels.Should().BeEmpty();
            result.Warnings.Should().NotBeEmpty();
        }

        [Test]
        public void should_remove_confirmed_repeated_position_without_losing_the_catalog_position()
        {
            var result = Project(Recording("Series, Vol. 5", "Light Novel, Vol. 5", "Series", "5"));

            result.Title.Should().Be("Series, Vol. 5");
            result.DisplayTitle.Should().Be("Series #5");
            result.Subtitle.Should().BeEmpty();
            result.Position.Should().Be("5");
            result.MetadataLabels.Should().Equal("Light Novel");
        }

        [TestCase("Series 9: A LitRPG Adventure")]
        [TestCase("Series (Light Novel), Vol. 9")]
        public void should_retain_source_title_fields_on_explicit_position_conflict(string title)
        {
            var result = Project(Recording(title, "A LitRPG Adventure", "Series", "1"));

            result.Title.Should().Be(title);
            result.Subtitle.Should().Be("A LitRPG Adventure");
            result.DisplayTitle.Should().Be(title);
            result.Warnings.Should().NotBeEmpty();
        }

        [Test]
        public void should_preserve_protected_fields_through_display_formatting()
        {
            var edition = Recording("Series (Light Novel), Vol. 1", "A LitRPG Adventure");
            var result = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), null, true, true);

            result.Title.Should().Be(edition.Title);
            result.DisplayTitle.Should().Be(edition.Title);
            result.Subtitle.Should().Be(edition.Subtitle);
            result.TitleProtected.Should().BeTrue();
            result.SubtitleProtected.Should().BeTrue();
            result.MetadataLabels.Should().BeEmpty();
        }

        [Test]
        public void should_keep_authoritative_corrected_labels_but_allow_safe_series_formatting()
        {
            var edition = Recording("Provider typo");
            var corrected = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), "Main: A LitRPG Adventure");
            var structural = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, Rules(), "Series, Vol. 1");

            corrected.DisplayTitle.Should().Be("Main: A LitRPG Adventure");
            corrected.MetadataLabels.Should().BeEmpty();
            structural.DisplayTitle.Should().Be("Series #1");
        }

        [TestCase("Main: A Useful Subtitle")]
        [TestCase("Main - A Useful Subtitle")]
        public void should_show_an_exact_repeated_subtitle_only_once(string title)
        {
            var result = Project(Recording(title, "A Useful Subtitle"));

            result.Title.Should().Be(title);
            result.Subtitle.Should().BeEmpty();
        }

        [Test]
        public void should_keep_performance_qualifiers_and_unconfirmed_volume_text()
        {
            var result = Project(Recording("Fourth Wing (Dramatized Adaptation)", "Light Novel, Vol. 9", "The Empyrean", "1"));

            result.Title.Should().Be("Fourth Wing (Dramatized Adaptation)");
            result.Subtitle.Should().Be("Light Novel, Vol. 9");
        }

        [Test]
        public void should_not_reinterpret_a_named_work_tail_as_a_volume_subtitle()
        {
            var result = Project(Recording("Sword Art Online: Progressive", null, "Sword Art Online", "2"));

            result.DisplayTitle.Should().Be("Sword Art Online: Progressive");
        }

        [Test]
        public void should_allow_absent_metadata_in_projection_previews()
        {
            var result = AudiobookMetadataCleanup.Project(null, null, Rules());

            result.Title.Should().BeEmpty();
            result.Subtitle.Should().BeEmpty();
            result.MetadataLabels.Should().BeEmpty();
        }

        [TestCase("Kuma Kuma Kuma Bear (Light Novel) Vol. 1", null)]
        [TestCase("Kuma Kuma Kuma Bear, Vol. 1", "Light Novel")]
        [TestCase("Kuma Kuma Kuma Bear, Vol. 1: Light Novel", null)]
        public void should_clean_a_bare_series_label_only_when_independently_corroborated(string title, string subtitle)
        {
            var edition = Recording(title, subtitle, "Kuma Kuma Kuma Bear Light Novel", "1");

            var result = Project(edition);

            result.Series.Should().Be("Kuma Kuma Kuma Bear");
            result.DisplayTitle.Should().Be("Kuma Kuma Kuma Bear #1");
            result.Position.Should().Be("1");
            result.MetadataLabels.Should().Equal("Light Novel");
            result.Changes.Should().Contain(change => change.Field == "series" && change.RuleId.HasValue &&
                change.OriginalText == "Kuma Kuma Kuma Bear Light Novel" && change.RemovedText == "Light Novel");
            edition.Book.Value.SeriesLinks.Value[0].Series.Value.Title.Should().Be("Kuma Kuma Kuma Bear Light Novel");
        }

        [TestCase("A Different Work", null)]
        [TestCase("The Light Novel Club", "A Useful Subtitle")]
        public void should_retain_bare_series_suffixes_without_an_independent_complete_label(string title, string subtitle)
        {
            var result = Project(Recording(title, subtitle, "A Real Light Novel", "1"));

            result.Series.Should().Be("A Real Light Novel");
            result.MetadataLabels.Should().BeEmpty();
            result.Changes.Should().BeEmpty();
        }

        [Test]
        public void should_retain_a_corroborated_bare_series_suffix_when_its_rule_is_disabled()
        {
            var edition = Recording("Kuma Kuma Kuma Bear (Light Novel) Vol. 1", null, "Kuma Kuma Kuma Bear Light Novel", "1");

            var result = AudiobookMetadataCleanup.Project(edition.Book.Value, edition, AudiobookMetadataCleanupRuleSeed.Read());

            result.Series.Should().Be("Kuma Kuma Kuma Bear Light Novel");
            result.MetadataLabels.Should().BeEmpty();
        }
    }
}
