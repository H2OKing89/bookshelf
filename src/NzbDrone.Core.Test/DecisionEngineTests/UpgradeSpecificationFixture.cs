using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]

    public class UpgradeSpecificationFixture : CoreTest<UpgradableSpecification>
    {
        public static object[] IsUpgradeTestCases =
        {
            new object[] { Quality.AZW3, 1, Quality.AZW3, 2, Quality.AZW3, true },
            new object[] { Quality.MP3, 1, Quality.MP3, 2, Quality.MP3, true },
            new object[] { Quality.MP3, 1, Quality.MP3, 1, Quality.MP3, false },
            new object[] { Quality.MP3, 1, Quality.AZW3, 2, Quality.MP3, false },
            new object[] { Quality.MP3, 1, Quality.AZW3, 2, Quality.MP3, false },
            new object[] { Quality.MP3, 1, Quality.MP3, 1, Quality.MP3, false }
        };

        private void GivenAutoDownloadPropers(ProperDownloadTypes type)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.DownloadPropersAndRepacks)
                  .Returns(type);
        }

        [Test]
        [TestCaseSource(nameof(IsUpgradeTestCases))]
        public void IsUpgradeTest(Quality current, int currentVersion, Quality newQuality, int newVersion, Quality cutoff, bool expected)
        {
            GivenAutoDownloadPropers(ProperDownloadTypes.PreferAndUpgrade);

            var profile = new QualityProfile
            {
                UpgradeAllowed = true,
                Items = Qualities.QualityFixture.GetDefaultQualities()
            };

            Subject.IsUpgradable(
                        profile,
                        new QualityModel(current, new Revision(version: currentVersion)),
                        new List<CustomFormat>(),
                        new QualityModel(newQuality, new Revision(version: newVersion)),
                        new List<CustomFormat>())
                   .Should().Be(expected);
        }

        [Test]
        public void should_return_true_if_proper_and_download_propers_is_do_not_download()
        {
            GivenAutoDownloadPropers(ProperDownloadTypes.DoNotUpgrade);

            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
            };

            Subject.IsUpgradable(
                        profile,
                        new QualityModel(Quality.MP3, new Revision(version: 1)),
                        new List<CustomFormat>(),
                        new QualityModel(Quality.MP3, new Revision(version: 2)),
                        new List<CustomFormat>())
                    .Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_proper_and_autoDownloadPropers_is_do_not_prefer()
        {
            GivenAutoDownloadPropers(ProperDownloadTypes.DoNotPrefer);

            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
            };

            Subject.IsUpgradable(
                        profile,
                        new QualityModel(Quality.MP3, new Revision(version: 1)),
                        new List<CustomFormat>(),
                        new QualityModel(Quality.MP3, new Revision(version: 2)),
                        new List<CustomFormat>())
                    .Should().BeFalse();
        }

        [TestCase(100, 109, 10, false, true)]
        [TestCase(100, 110, 10, true, false)]
        [TestCase(100, 101, 0, true, false)]
        [TestCase(100, 100, 0, false, false)]
        [TestCase(-100, -91, 10, false, true)]
        [TestCase(-100, -90, 10, true, false)]
        [TestCase(int.MinValue, int.MaxValue, int.MaxValue, true, false)]
        public void same_quality_upgrade_requires_strict_improvement_and_meets_configured_delta(int currentScore, int candidateScore, int minimum, bool accepted, bool belowMinimum)
        {
            var current = new CustomFormat { Id = 1 };
            var candidate = new CustomFormat { Id = 2 };
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                MinUpgradeFormatScore = minimum,
                FormatItems = new List<ProfileFormatItem>
                {
                    new ProfileFormatItem { Format = current, Score = currentScore },
                    new ProfileFormatItem { Format = candidate, Score = candidateScore }
                }
            };

            var quality = new QualityModel(Quality.MP3);
            Subject.IsUpgradable(profile, quality, new List<CustomFormat> { current }, quality, new List<CustomFormat> { candidate }, out var below)
                .Should().Be(accepted);
            below.Should().Be(belowMinimum);
        }

        [TestCase(ProperDownloadTypes.PreferAndUpgrade, true)]
        [TestCase(ProperDownloadTypes.DoNotPrefer, false)]
        public void a_revision_upgrade_bypasses_the_delta_only_when_propers_are_preferred(ProperDownloadTypes propers, bool accepted)
        {
            GivenAutoDownloadPropers(propers);
            var candidate = new CustomFormat { Id = 1 };
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                MinUpgradeFormatScore = 10,
                FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = candidate, Score = 9 } }
            };

            var currentQuality = new QualityModel(Quality.MP3, new Revision(version: 1));
            var candidateQuality = new QualityModel(Quality.MP3, new Revision(version: 2));
            Subject.IsUpgradable(profile, currentQuality, new List<CustomFormat>(), candidateQuality, new List<CustomFormat> { candidate }, out var below)
                .Should().Be(accepted);
            below.Should().Be(!accepted);
        }
    }
}
