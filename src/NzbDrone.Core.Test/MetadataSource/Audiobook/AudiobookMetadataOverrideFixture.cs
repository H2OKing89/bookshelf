using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookMetadataOverrideFixture : CoreTest<AudiobookMetadataOverrideService>
    {
        private List<AudiobookMetadataOverride> _rows;

        [SetUp]
        public void Setup()
        {
            _rows = AudiobookMetadataOverrideSeed.Read();
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].Id = i + 1;
            }

            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(r => r.All()).Returns(() => _rows.ToList());
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(r => r.Insert(It.IsAny<AudiobookMetadataOverride>()))
                .Returns((AudiobookMetadataOverride item) =>
                {
                    item.Id = _rows.Max(row => row.Id) + 1;
                    _rows.Add(item);
                    return item;
                });
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(r => r.Update(It.IsAny<AudiobookMetadataOverride>()))
                .Returns((AudiobookMetadataOverride item) =>
                {
                    _rows[_rows.FindIndex(row => row.Id == item.Id)] = item;
                    return item;
                });
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(r => r.Delete(It.IsAny<int>()))
                .Callback((int id) => _rows.RemoveAll(row => row.Id == id));
        }

        private static AudiobookMetadataOverride NewAuthor() => new AudiobookMetadataOverride
        {
            Kind = "author", Region = "us", Asin = "B000000001", Name = "Example Author",
            AlternateAsins = new List<string> { "B000000002" }, Aliases = new List<string> { "別名" },
            SourceUrl = "https://publisher.example/author", Reason = "Publisher confirms both profiles."
        };

        [Test]
        public void should_use_database_author_mappings_and_invalidate_them_after_disable_or_delete()
        {
            Subject.CanonicalAuthorId("audible:us:B074QPMWRL").Should().Be("audible:us:B0DSLB5CG5");
            var edit = Json.Deserialize<AudiobookMetadataOverride>(Subject.Find(1).ToJson());
            edit.Enabled = false;
            Subject.Save(edit);

            Subject.CanonicalAuthorId("audible:us:B074QPMWRL").Should().Be("audible:us:B074QPMWRL");
            var restarted = new AudiobookMetadataOverrideService(Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Object, new CacheManager());
            restarted.CanonicalAuthorId("audible:us:B074QPMWRL").Should().Be("audible:us:B074QPMWRL");
            restarted.Delete(1);
            restarted.All().Should().NotContain(row => row.Asin == "B0DSLB5CG5");
        }

        [Test]
        public void should_support_new_mappings_without_a_program_change()
        {
            var item = NewAuthor();
            Subject.Save(item);

            Subject.CanonicalAuthorId("audible:us:B000000002").Should().Be("audible:us:B000000001");
            Subject.FindAuthor(new AudnexusContributorResource { Name = "別名" }, "us").Name.Should().Be("Example Author");
            Subject.FindAuthor(new AudnexusContributorResource { Asin = "B000000003", Name = "別名" }, "us").Should().BeNull();
            Subject.FindAuthor(new AudnexusContributorResource { Asin = "B000000002", Name = "Example Author" }, "jp").Should().BeNull();
            item.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void should_not_change_cached_mappings_when_an_edit_fails_validation()
        {
            var item = Subject.Find(1);
            item.Name = null;
            item.AlternateAsins.Add("B000000009");

            Assert.Throws<ValidationException>(() => Subject.Save(item));

            Subject.Find(1).Name.Should().Be("Kumanano");
            Subject.CanonicalAuthorId("audible:us:B000000009").Should().Be("audible:us:B000000009");
        }

        [Test]
        public void should_retain_provider_spellings_when_a_mapping_uses_a_different_display_name()
        {
            var item = NewAuthor();
            Subject.Save(item);
            var contributor = new AudnexusContributorResource { Asin = item.Asin, Name = "Provider Spelling" };
            var metadata = AudiobookMetadataMapper.Author(contributor, "us", Subject.FindAuthor(contributor, "us"));

            metadata.Name.Should().Be("Example Author");
            metadata.Aliases.Should().Contain("Provider Spelling");
            Subject.AuthorSearchNames(metadata, "us").Should().Contain("Provider Spelling");
            contributor.Name.Should().Be("Provider Spelling");
        }

        [Test]
        public void should_normalize_only_enabled_verified_narrator_aliases_in_their_marketplace()
        {
            var item = new AudiobookMetadataOverride
            {
                Kind = "narrator", Region = "us", Name = "Reader One", Aliases = new List<string> { "Reader Alias" },
                SourceUrl = "https://narrator.example/credits", Reason = "Narrator confirms both names."
            };
            Subject.Save(item);

            Subject.NarratorName("Reader Alias", "us").Should().Be("Reader One");
            Subject.NarratorName("Reader Alias", "jp").Should().Be("Reader Alias");
            Subject.NarratorName("Another Reader", "us").Should().Be("Another Reader");
            item.Enabled = false;
            Subject.Save(item);
            Subject.NarratorName("Reader Alias", "us").Should().Be("Reader Alias");
        }

        [Test]
        public void should_reject_overlapping_narrator_identities()
        {
            var original = new AudiobookMetadataOverride
            {
                Kind = "narrator", Region = "us", Name = "Reader One", Aliases = new List<string> { "Reader Alias" },
                SourceUrl = "https://narrator.example/credits", Reason = "Narrator confirms both names."
            };
            Subject.Save(original);
            var other = new AudiobookMetadataOverride
            {
                Kind = "narrator", Region = "us", Name = "Different Reader", Aliases = new List<string> { "Reader Alias" },
                SourceUrl = "https://narrator.example/credits", Reason = "Conflicting identity."
            };

            Assert.Throws<ValidationException>(() => Subject.Save(other));
        }

        [Test]
        public void should_have_no_hardcoded_fallback_when_the_database_is_empty()
        {
            _rows.Clear();

            Subject.CanonicalAuthorId("audible:us:B074QPMWRL").Should().Be("audible:us:B074QPMWRL");
            Subject.CanonicalAuthorId("audible:us:B076N4Q46Q").Should().Be("audible:us:B076N4Q46Q");
            Subject.FindAuthor(new AudnexusContributorResource { Name = "くまなの" }, "us").Should().BeNull();
        }

        [TestCase("asin")]
        [TestCase("name")]
        [TestCase("chain")]
        public void should_reject_overlapping_author_claims_and_mapping_chains(string conflict)
        {
            var item = NewAuthor();
            if (conflict == "asin")
            {
                item.AlternateAsins.Add("B074QPMWRL");
            }
            else if (conflict == "name")
            {
                item.Aliases.Add("くまなの");
            }
            else
            {
                item.Asin = "B074QPMWRL";
            }

            Assert.Throws<ValidationException>(() => Subject.Save(item));
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Verify(r => r.Insert(It.IsAny<AudiobookMetadataOverride>()), Times.Never());
        }

        [TestCase("region")]
        [TestCase("asin")]
        [TestCase("source")]
        [TestCase("reason")]
        public void should_require_a_valid_identity_and_evidence(string missing)
        {
            var item = NewAuthor();
            if (missing == "region")
            {
                item.Region = "unsupported";
            }
            else if (missing == "asin")
            {
                item.Asin = "invalid";
            }
            else if (missing == "source")
            {
                item.SourceUrl = null;
            }
            else
            {
                item.Reason = " ";
            }

            Assert.Throws<ValidationException>(() => Subject.Save(item));
        }

        [Test]
        public void should_apply_saved_title_corrections_without_overwriting_raw_metadata()
        {
            var resource = new AudnexusBookResource
            {
                Asin = "B000000004", Region = "us", Title = "Provider typo",
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "Example" } },
                SeriesPrimary = new AudnexusSeriesResource { Name = "Example Series", Position = "1" }
            };
            var book = AudiobookMetadataMapper.Book(resource, "us");
            var item = new AudiobookMetadataOverride
            {
                Kind = "title", Region = "us", Asin = resource.Asin,
                OriginalTitle = resource.Title, ReplacementTitle = "Example Series, Vol. 1",
                SourceUrl = "https://publisher.example/book", Reason = "Publisher and series identify volume 1."
            };
            Subject.DisplayTitle(book).Should().Be("Provider typo");
            Subject.Save(item);

            Subject.DisplayTitle(book).Should().Be("Example Series #1");
            book.Title.Should().Be("Provider typo");
            book.Editions.Value.Single().Title.Should().Be("Provider typo");
            resource.Title = "Provider changed its title";
            var updated = AudiobookMetadataMapper.Book(resource, "us");
            Subject.DisplayTitle(updated).Should().Be("Provider changed its title");
            Subject.Delete(item.Id);
            Subject.DisplayTitle(book).Should().Be("Provider typo");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_apply_explicit_title_overrides_to_standalone_and_numbered_books(bool hasSeries)
        {
            var resource = new AudnexusBookResource
            {
                Asin = "B000000004", Region = "us", Title = "Example Series, Vol. 1",
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "Example" } },
                SeriesPrimary = hasSeries ? new AudnexusSeriesResource { Name = "Example Series", Position = "1" } : null
            };
            var book = AudiobookMetadataMapper.Book(resource, "us");
            Subject.Save(new AudiobookMetadataOverride
            {
                Kind = "title", Region = "us", Asin = resource.Asin,
                OriginalTitle = resource.Title, ReplacementTitle = "A Corrected Distinctive Title",
                SourceUrl = "https://publisher.example/book", Reason = "Verified publisher title."
            });

            Subject.DisplayTitle(book).Should().Be("A Corrected Distinctive Title");
            book.Title.Should().Be(resource.Title);
        }
    }
}
