using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class AudiobookFileNameBuilderFixture : CoreTest<FileNameBuilder>
    {
        private const string ImporterFormat = "{Book NamingSeries}/{Book NamingTitle}{ (Edition Year)}{ (Author Name)}{ [Book Asin]}/{Book NamingTitle}{ (Edition Year)}{ (Author Name)}{ [Book Asin]}{ (PartNumber:00)}";
        private const string ShortFormat = "{Book NamingSeries}/{Book SeriesPosition:00 - }{Book WorkTitle}{ [Book Asin]}/{Author Name} - {Book WorkTitle}{ (PartNumber:00)}";
        private Author _author;
        private Book _book;
        private Edition _edition;
        private BookFile _file;
        private List<AudiobookMetadataOverride> _overrides;
        private List<AudiobookMetadataCleanupRule> _rules;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Name = "pirate aba" };
            _book = new Book
            {
                Title = "Memory's Flame", ForeignBookId = "audible:us:B0HGBVHDM2", AuthorMetadata = _author.Metadata.Value,
                SeriesLinks = new List<SeriesBookLink>
                {
                    new SeriesBookLink { Series = new Series { Title = "The Wandering Inn" }, Position = "20", IsPrimary = true }
                }
            };
            _edition = new Edition
            {
                Book = _book, Title = _book.Title, Subtitle = "The Wandering Inn, Book 20", Monitored = true,
                Asin = "B0HGBVHDM2", AudibleRegion = "us", Narrators = new List<string> { "Erin Bennett" },
                Language = "eng", Publisher = "Example Publisher", ReleaseDate = new DateTime(2026, 11, 3)
            };
            _book.Editions = new List<Edition> { _edition };
            _file = new BookFile { Quality = new QualityModel(Quality.M4B), Path = "/source/original.m4b", Part = 1, PartCount = 1 };
            _overrides = new List<AudiobookMetadataOverride>();
            _rules = AudiobookMetadataCleanupRuleSeed.Read();
            _rules.ForEach(rule => rule.Enabled = true);
            Mocker.GetMock<IAudiobookMetadataCleanupRuleService>().Setup(service => service.All()).Returns(_rules);
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(repository => repository.All()).Returns(_overrides);
            Mocker.SetConstant<IAudiobookMetadataOverrideService>(Mocker.Resolve<AudiobookMetadataOverrideService>());
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.Get(It.IsAny<Quality>()))
                .Returns<Quality>(quality => Quality.DefaultQualityDefinitions.Single(definition => definition.Quality == quality));
        }

        private string Build(string format)
        {
            var config = new NamingConfig
            {
                RenameBooks = true, ReplaceIllegalCharacters = true, ColonReplacementFormat = ColonReplacementFormat.Smart,
                StandardBookFormat = format
            };
            return Subject.BuildBookFileName(_author, _edition, _file, config, new List<CustomFormat>());
        }

        [Test]
        public void should_offer_both_presets_for_the_same_recording()
        {
            Build(ImporterFormat).Should().Be(("The Wandering Inn/The Wandering Inn vol_20 Memory's Flame (2026) (pirate aba) [B0HGBVHDM2]/" +
                "The Wandering Inn vol_20 Memory's Flame (2026) (pirate aba) [B0HGBVHDM2]").AsOsAgnostic());
            Build(ShortFormat).Should().Be("The Wandering Inn/20 - Memory's Flame [B0HGBVHDM2]/pirate aba - Memory's Flame".AsOsAgnostic());
        }

        [TestCase(ImporterFormat)]
        [TestCase(ShortFormat)]
        public void should_keep_multipart_destinations_distinct(string format)
        {
            _file.PartCount = 10;
            var first = Build(format);
            _file.Part = 2;
            var second = Build(format);

            first.Should().EndWith(" (01)");
            second.Should().EndWith(" (02)").And.NotBe(first);
            FileNameBuilder.BookTitleRegex.IsMatch(format).Should().BeTrue();
            FileNameBuilder.PartRegex.IsMatch(format).Should().BeTrue();
        }

        [Test]
        public void should_use_primary_series_instead_of_an_incidental_series()
        {
            _book.SeriesLinks.Value.Insert(0, new SeriesBookLink
            {
                Series = new Series { Title = "An Incidental Series" }, Position = "1", SeriesPosition = -1
            });

            Build("{Book Series} - {Book SeriesPosition:00} - {Book SeriesTitle}").Should()
                .Be("The Wandering Inn - 20 - The Wandering Inn #20");
        }

        [Test]
        public void should_expose_recording_metadata_and_omit_empty_wrappers()
        {
            Build("{Book Asin} - {Book Narrators} - {Book Language} - {Book Publisher}").Should()
                .Be("B0HGBVHDM2 - Erin Bennett - eng - Example Publisher");
            _edition.Asin = null;
            _edition.Narrators.Clear();

            Build("{Book WorkTitle}{ [Book Asin]}{ (Book Narrators)}").Should().Be("Memory's Flame");
        }

        [Test]
        public void should_use_structured_subtitle_and_retain_legacy_title_splitting_as_fallback()
        {
            _edition.Subtitle = "An Independently Supplied Subtitle";
            Build("{Book Subtitle}").Should().Be("An Independently Supplied Subtitle");
            _edition.Subtitle = null;
            _edition.Title = "Title: Legacy Subtitle";

            Build("{Book Subtitle}").Should().Be("Legacy Subtitle");
        }

        [Test]
        public void should_use_the_files_edition_even_when_another_edition_is_monitored()
        {
            _book.SeriesLinks.Value[0].Series.Value.Title = "Mushoku Tensei: Jobless Reincarnation";
            _book.SeriesLinks.Value[0].Position = "1";
            _edition.Title = "Mushoku Tensei";
            _edition.Subtitle = null;
            _edition.Monitored = false;
            _book.Editions.Value.Add(new Edition
            {
                Title = _edition.Title, Monitored = true, Subtitle = "Jobless Reincarnation (Light Novel), Vol. 1"
            });

            Build("{Book DisplayTitle}").Should().Be("Mushoku Tensei");
        }

        [Test]
        public void should_apply_editable_title_and_narrator_corrections_without_rewriting_source_metadata()
        {
            _overrides.Add(new AudiobookMetadataOverride
            {
                Kind = "title", Enabled = true, Region = "us", Asin = _edition.Asin,
                OriginalTitle = _edition.Title, ReplacementTitle = "A Corrected Work Title"
            });
            _overrides.Add(new AudiobookMetadataOverride
            {
                Kind = "narrator", Enabled = true, Region = "us", Name = "Verified Narrator",
                Aliases = new List<string> { "Erin Bennett" }
            });

            Build("{Book DisplayTitle} - {Book Narrators}").Should().Be("A Corrected Work Title - Verified Narrator");
            _edition.Title.Should().Be("Memory's Flame");
            _book.Title.Should().Be("Memory's Flame");
            _edition.Narrators.Should().Equal("Erin Bennett");
        }

        [Test]
        public void should_use_shared_cleanup_without_changing_published_naming_tokens()
        {
            _edition.Title = "The Primal Hunter 4: A LitRPG Adventure";
            _edition.Subtitle = "Light Novel";
            _book.SeriesLinks.Value[0].Series.Value.Title = "The Primal Hunter (Light Novel)";
            _book.SeriesLinks.Value[0].Position = "4";

            Build("{Book DisplayTitle}").Should().Be("The Primal Hunter #4");
            Build("{Book NamingTitle}").Should().Be("The Primal Hunter vol_04");
            Build("{Book NamingSeries}").Should().Be("The Primal Hunter");
            Build("{Book NamingSubtitle}").Should().BeEmpty();
            Build("{Book Series}").Should().Be("The Primal Hunter (Light Novel)");
            Build("{Book Subtitle}").Should().Be("Light Novel");
            Build("{Book Title}").Should().Contain("A LitRPG Adventure");
            _edition.Title.Should().Be("The Primal Hunter 4: A LitRPG Adventure");
            _edition.Subtitle.Should().Be("Light Novel");
        }

        [Test]
        public void should_honor_disabled_rules_in_every_normalized_naming_token()
        {
            _rules.ForEach(rule => rule.Enabled = false);
            _edition.Title = "An Independent Work: A LitRPG Adventure";
            _edition.Subtitle = "Light Novel";

            Build("{Book DisplayTitle}").Should().Contain("A LitRPG Adventure");
            Build("{Book NamingTitle}").Should().Contain("A LitRPG Adventure").And.Contain("Light Novel");
            Build("{Book NamingSubtitle}").Should().Be("Light Novel");
        }

        [Test]
        public void should_preserve_exact_protected_fields_through_naming()
        {
            _edition.Title = "An Independent Work: A LitRPG Adventure";
            _edition.Subtitle = "Light Novel";
            _overrides.Add(new AudiobookMetadataOverride
            {
                Kind = "titleprotection", Enabled = true, Region = "us", Asin = _edition.Asin,
                OriginalTitle = _edition.Title, OriginalSubtitle = _edition.Subtitle
            });

            Build("{Book DisplayTitle}").Should().Be("An Independent Work - A LitRPG Adventure");
            Build("{Book NamingTitle}").Should().Be("An Independent Work - A LitRPG Adventure - Light Novel");
            Build("{Book NamingSubtitle}").Should().Be("Light Novel");
        }

        [Test]
        public void should_retain_a_protected_subtitle_in_standalone_naming()
        {
            _book.SeriesLinks.Value.Clear();
            _edition.Subtitle = "Light Novel";
            _overrides.Add(new AudiobookMetadataOverride
            {
                Kind = "titleprotection", Enabled = true, Region = "us", Asin = _edition.Asin,
                OriginalSubtitle = _edition.Subtitle
            });

            Build("{Book NamingTitle}").Should().Be("Memory's Flame - Light Novel");
            Build("{Book WorkTitle}").Should().Be("Memory's Flame - Light Novel");
        }

        [Test]
        public void should_retain_a_protected_subtitle_when_a_work_title_cannot_be_split_from_its_series()
        {
            _edition.Title = "Sword Art Online: Progressive";
            _edition.Subtitle = "Light Novel";
            _book.SeriesLinks.Value[0].Series.Value.Title = "Sword Art Online";
            _book.SeriesLinks.Value[0].Position = "2";
            _overrides.Add(new AudiobookMetadataOverride
            {
                Kind = "titleprotection", Enabled = true, Region = "us", Asin = _edition.Asin,
                OriginalSubtitle = _edition.Subtitle
            });

            Build("{Book NamingTitle}").Should().Be("Sword Art Online - Progressive - Light Novel");
            Build("{Book WorkTitle}").Should().Be("Sword Art Online - Progressive - Light Novel");
        }

        [Test]
        public void should_preserve_meaningful_repeated_hyphens_in_a_work_title()
        {
            _edition.Title = "86--EIGHTY-SIX (Light Novel)";
            _edition.Subtitle = null;
            _book.SeriesLinks.Value.Clear();

            Build("{Book DisplayTitle}").Should().Be("86--EIGHTY-SIX");
            Build("{Book WorkTitle}").Should().Be("86--EIGHTY-SIX");
        }

        [TestCase("{Book DisplayTitle}{ (PartNumber)}")]
        [TestCase("{Book NamingTitle}{ (PartNumber)}")]
        [TestCase("{Book WorkTitle}{ (PartNumber)}")]
        public void should_recognize_audiobook_title_tokens_in_naming_validation(string format)
        {
            FileNameBuilder.BookTitleRegex.IsMatch(format).Should().BeTrue();
        }
    }
}
