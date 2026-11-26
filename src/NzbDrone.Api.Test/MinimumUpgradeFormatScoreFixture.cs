using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Profiles.Quality;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class MinimumUpgradeFormatScoreFixture : TestBase<MinimumUpgradeFormatScoreFixture.TestQualityProfileController>
    {
        [TestCase(null, true)]
        [TestCase(0, true)]
        [TestCase(10, true)]
        [TestCase(-1, false)]
        public void validates_a_nonnegative_optional_score_improvement(int? improvement, bool valid)
        {
            Mocker.GetMock<ICustomFormatService>().Setup(formats => formats.All()).Returns(new List<CustomFormat>());
            var resource = Profile().ToResource();
            resource.MinUpgradeFormatScore = improvement;

            var result = Subject.ValidateProfile(resource);

            result.Errors.Any(error => error.PropertyName == nameof(resource.MinUpgradeFormatScore)).Should().Be(!valid);
        }

        [TestCase("{}", 17)]
        [TestCase("{\"minUpgradeFormatScore\":null}", 17)]
        [TestCase("{\"minUpgradeFormatScore\":0}", 0)]
        [TestCase("{\"minUpgradeFormatScore\":10}", 10)]
        public void update_preserves_an_omitted_value_and_allows_explicit_reset(string json, int expected)
        {
            var existing = Profile();
            var resource = existing.ToResource();
            resource.MinUpgradeFormatScore = STJson.Deserialize<QualityProfileResource>(json).MinUpgradeFormatScore;
            var service = Mocker.GetMock<IQualityProfileService>();
            service.Setup(profiles => profiles.Get(existing.Id)).Returns(existing);

            Subject.Update(resource);

            service.Verify(
                profiles => profiles.Update(It.Is<QualityProfile>(profile => profile.Id == existing.Id &&
                    profile.MinUpgradeFormatScore == expected)),
                Times.Once());
        }

        [Test]
        public void legacy_create_defaults_to_zero_and_current_values_roundtrip()
        {
            var resource = Profile().ToResource();
            STJson.Deserialize<QualityProfileResource>(STJson.ToJson(resource)).ToModel().MinUpgradeFormatScore.Should().Be(17);

            resource.MinUpgradeFormatScore = null;
            resource.ToModel().MinUpgradeFormatScore.Should().Be(0);
        }

        private static QualityProfile Profile()
        {
            return new QualityProfile
            {
                Id = 2,
                Name = "Owned Profile",
                Cutoff = Quality.MP3.Id,
                UpgradeAllowed = true,
                MinUpgradeFormatScore = 17,
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Allowed = true, Quality = Quality.MP3 } }
            };
        }

        public class TestQualityProfileController : QualityProfileController
        {
            public TestQualityProfileController(IQualityProfileService qualityProfileService, ICustomFormatService formatService)
                : base(qualityProfileService, formatService)
            {
            }

            public ValidationResult ValidateProfile(QualityProfileResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
