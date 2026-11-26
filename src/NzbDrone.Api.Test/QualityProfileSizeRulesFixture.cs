using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Profiles.Quality;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class QualityProfileSizeRulesFixture : TestBase<QualityProfileController>
    {
        [Test]
        public void grouped_sizes_should_round_trip_without_borrowing_the_group_identity()
        {
            var audio = SizeItem(Quality.M4B);
            var ebook = SizeItem(Quality.EPUB);
            var profile = Profile(new QualityProfileQualityItem
            {
                Id = 1000,
                Name = "Owned group",
                Items = new List<QualityProfileQualityItem> { audio, ebook }
            });

            var resource = STJson.Deserialize<QualityProfileResource>(STJson.ToJson(profile.ToResource()));
            resource.Items[0].SizeUnit.Should().BeNull();
            resource.Items[0].Items[0].SizeUnit.Should().Be("MiB/hour");
            resource.Items[0].Items[1].SizeUnit.Should().Be("MiB");
            var roundTrip = resource.ToModel();
            roundTrip.Items[0].Items.Should().BeEquivalentTo(profile.Items[0].Items);
        }

        [Test]
        public void older_requests_should_preserve_rules_after_a_quality_moves_into_a_group_and_zero_should_clear()
        {
            var saved = Profile(SizeItem(Quality.M4B));
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.Get(saved.Id)).Returns(saved);
            var leaf = new QualityProfileQualityItem { Allowed = true, Quality = Quality.M4B, PreferredSize = 0 };
            var updated = Profile(new QualityProfileQualityItem
            {
                Id = 1000,
                Name = "Owned group",
                Items = new List<QualityProfileQualityItem> { leaf }
            });

            Subject.Update(updated.ToResource());

            Mocker.GetMock<IQualityProfileService>().Verify(
                service => service.Update(It.Is<QualityProfile>(profile =>
                    profile.Items[0].Items[0].MinSize == 20 && profile.Items[0].Items[0].PreferredSize == 0 &&
                    profile.Items[0].Items[0].MaxSize == 100)),
                Times.Once());
        }

        [TestCase(-1, 0, 0)]
        [TestCase(100, 50, 0)]
        [TestCase(100, 0, 50)]
        [TestCase(0, 50, 40)]
        [TestCase(0, 0, 1000001)]
        public void invalid_limits_should_not_be_saved(int minimum, int preferred, int maximum)
        {
            var item = new QualityProfileQualityItem { Quality = Quality.M4B, MinSize = minimum, PreferredSize = preferred, MaxSize = maximum };
            var resource = Profile(item).ToResource();

            Action create = () => Subject.Create(resource);

            create.Should().Throw<ValidationException>();
            Mocker.GetMock<IQualityProfileService>().Verify(service => service.Add(It.IsAny<QualityProfile>()), Times.Never());
        }

        [Test]
        public void a_partial_bound_must_be_valid_against_the_saved_values()
        {
            var saved = Profile(SizeItem(Quality.M4B));
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.Get(saved.Id)).Returns(saved);
            var resource = Profile(new QualityProfileQualityItem { Quality = Quality.M4B, MaxSize = 24 }).ToResource();

            Action update = () => Subject.Update(resource);

            update.Should().Throw<ValidationException>();
            Mocker.GetMock<IQualityProfileService>().Verify(service => service.Update(It.IsAny<QualityProfile>()), Times.Never());
        }

        private static QualityProfile Profile(QualityProfileQualityItem item)
        {
            return new QualityProfile { Id = 2, Name = "Owned profile", Items = new List<QualityProfileQualityItem> { item } };
        }

        private static QualityProfileQualityItem SizeItem(Quality quality)
        {
            return new QualityProfileQualityItem { Quality = quality, Allowed = true, MinSize = 20, PreferredSize = 50, MaxSize = 100 };
        }
    }
}
