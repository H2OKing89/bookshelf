using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookAuthorDisplayNameFixture
    {
        private const string AuthorAsin = "B00QPOM2M4";
        private const string AuthorId = "audible:us:" + AuthorAsin;

        [TestCase("Mitizou", "道造", "B0D9LJTQFX", "B0GSC3K8XT")]
        [TestCase("Miya Kazuki", "香月美夜", AuthorAsin, "B0DFHMW24T")]
        public void should_use_observed_english_recording_credit_for_the_same_author(string credit, string profileName, string authorAsin, string recordingAsin)
        {
            var profile = new AuthorMetadata { ForeignAuthorId = "audible:us:" + authorAsin, Name = profileName };
            var recording = Recording(credit);
            recording.Asin = recordingAsin;
            recording.Authors[0].Asin = authorAsin;

            AudiobookAuthorIdentity.PreferDisplayName(profile, recordings: new[] { recording });

            profile.Name.Should().Be(credit);
            profile.SortName.Should().Be(credit);
            profile.NameLastFirst.Should().Be(credit);
            profile.SortNameLastFirst.Should().Be(credit);
            profile.Aliases.Should().ContainSingle().Which.Should().Be(profileName);
        }

        [Test]
        public void should_keep_established_latin_name_and_remember_native_profile_after_refresh()
        {
            var profile = Metadata("香月美夜");
            var previous = Metadata("Miya Kazuki");

            AudiobookAuthorIdentity.PreferDisplayName(profile, previous);

            profile.Name.Should().Be("Miya Kazuki");
            profile.Aliases.Should().Contain("香月美夜");
        }

        [Test]
        public void should_recover_one_observed_alias_for_the_exact_identity()
        {
            var profile = Metadata("香月美夜");
            var previous = Metadata("香月美夜");
            previous.Aliases.Add("Miya Kazuki");

            AudiobookAuthorIdentity.PreferDisplayName(profile, previous);

            profile.Name.Should().Be("Miya Kazuki");
            profile.Aliases.Should().ContainSingle().Which.Should().Be("香月美夜");
        }

        [Test]
        public void should_prefer_explicit_verified_override_including_a_native_name()
        {
            var profile = Metadata("Profile Name");
            var identity = new AudiobookAuthorIdentity("us", AuthorAsin, "香月美夜", Array.Empty<string>(), Array.Empty<string>());

            AudiobookAuthorIdentity.PreferDisplayName(profile, Metadata("Miya Kazuki"), identity, new[] { Recording("Miya Kazuki") });

            profile.Name.Should().Be("香月美夜");
            profile.Aliases.Should().Contain(new[] { "Profile Name", "Miya Kazuki" });
        }

        [TestCase("audible:us:B000000002")]
        [TestCase("audible:jp:" + AuthorAsin)]
        public void should_not_use_a_previous_name_or_alias_from_another_identity(string previousId)
        {
            var profile = Metadata("香月美夜");
            var previous = new AuthorMetadata { ForeignAuthorId = previousId, Name = "Miya Kazuki", Aliases = new List<string> { "Kazuki Miya" } };

            AudiobookAuthorIdentity.PreferDisplayName(profile, previous);

            profile.Name.Should().Be("香月美夜");
            profile.Aliases.Should().BeEmpty();
        }

        [TestCase("japanese", "us", AuthorAsin, "Author")]
        [TestCase(null, "us", AuthorAsin, "Author")]
        [TestCase("english", "jp", AuthorAsin, "Author")]
        [TestCase("english", "us", "B000000002", "Author")]
        [TestCase("english", "us", AuthorAsin, "Translator")]
        public void should_not_use_an_unrelated_or_non_english_credit(string language, string region, string asin, string role)
        {
            var profile = Metadata("香月美夜");
            var recording = Recording("Miya Kazuki");
            recording.Language = language;
            recording.Region = region;
            recording.Authors[0].Asin = asin;
            recording.Authors[0].Role = role;

            AudiobookAuthorIdentity.PreferDisplayName(profile, recordings: new[] { recording });

            profile.Name.Should().Be("香月美夜");
            profile.Aliases.Should().BeEmpty();
        }

        [Test]
        public void should_not_apply_an_override_for_a_different_identity()
        {
            var profile = Metadata("香月美夜");
            var identity = new AudiobookAuthorIdentity("us", "B000000002", "Other Author", Array.Empty<string>(), Array.Empty<string>());

            AudiobookAuthorIdentity.PreferDisplayName(profile, identity: identity);

            profile.Name.Should().Be("香月美夜");
        }

        [Test]
        public void should_not_guess_between_multiple_latin_aliases()
        {
            var profile = Metadata("香月美夜");
            profile.Aliases.AddRange(new[] { "Miya Kazuki", "Kazuki Miya" });

            AudiobookAuthorIdentity.PreferDisplayName(profile);

            profile.Name.Should().Be("香月美夜");
        }

        [Test]
        public void should_not_guess_between_conflicting_english_recording_credits()
        {
            var profile = Metadata("香月美夜");

            AudiobookAuthorIdentity.PreferDisplayName(profile, recordings: new[] { Recording("Miya Kazuki"), Recording("Kazuki Miya") });

            profile.Name.Should().Be("香月美夜");
            profile.Aliases.Should().Contain(new[] { "Miya Kazuki", "Kazuki Miya" });
        }

        [Test]
        public void should_leave_japanese_only_names_unchanged()
        {
            var profile = Metadata("香月美夜");

            AudiobookAuthorIdentity.PreferDisplayName(profile, recordings: new[] { Recording("香月美夜") });

            profile.Name.Should().Be("香月美夜");
            profile.Aliases.Should().BeEmpty();
        }

        [Test]
        public void should_not_infer_an_asin_identity_from_a_name_id()
        {
            var profile = new AuthorMetadata { ForeignAuthorId = "audible:us:name:%E9%A6%99%E6%9C%88%E7%BE%8E%E5%A4%9C", Name = "香月美夜" };

            AudiobookAuthorIdentity.PreferDisplayName(profile, recordings: new[] { Recording("Miya Kazuki") });

            profile.Name.Should().Be("香月美夜");
            profile.ForeignAuthorId.Should().StartWith("audible:us:name:");
        }

        [TestCase("Miya Kazuki", true)]
        [TestCase("Émile Zola", true)]
        [TestCase("Łukasz", true)]
        [TestCase("G. R. R. Martin", true)]
        [TestCase("pirateaba", true)]
        [TestCase("香月美夜", false)]
        [TestCase("Miya 香月", false)]
        [TestCase("Александр", false)]
        [TestCase("123", false)]
        [TestCase(null, false)]
        public void should_detect_observed_latin_names_without_transliterating(string name, bool latin)
        {
            AudiobookAuthorIdentity.IsLatinName(name).Should().Be(latin);
        }

        private static AuthorMetadata Metadata(string name) => new AuthorMetadata { ForeignAuthorId = AuthorId, Name = name };

        private static AudnexusBookResource Recording(string name) => new AudnexusBookResource
        {
            Asin = "B0DFHMW24T", Region = "us", Language = "english", Title = "Ascendance of a Bookworm: Part 1, Volume 2",
            Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = AuthorAsin, Name = name } }
        };
    }
}
