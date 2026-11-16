using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Test.Common;
using Readarr.Api.V1.ManualImport;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class ManualImportReleaseGroupFixture : TestBase<ManualImportController>
    {
        [Test]
        public void should_return_resolved_formats_and_the_authors_profile_score()
        {
            var preferred = new CustomFormat { Id = 1, Name = "Preferred group" };
            var penalty = new CustomFormat { Id = 2, Name = "Other condition" };
            var unmatched = new CustomFormat { Id = 3, Name = "Unmatched" };
            var item = new ManualImportItem
            {
                Id = 10,
                Path = "/downloads/book.m4b",
                ReleaseGroup = "Audio-Crew",
                ReleaseGroupOverride = "Audio-Crew",
                Author = new Author
                {
                    Metadata = new AuthorMetadata { Name = "An author" },
                    QualityProfile = new QualityProfile
                    {
                        FormatItems = new List<ProfileFormatItem>
                        {
                            new ProfileFormatItem { Format = preferred, Score = 100 },
                            new ProfileFormatItem { Format = penalty, Score = -20 },
                            new ProfileFormatItem { Format = unmatched, Score = 500 }
                        }
                    }
                },
                CustomFormats = new List<CustomFormat> { preferred, penalty }
            };

            var result = Json.Deserialize<ManualImportResource>(item.ToResource().ToJson());

            result.ReleaseGroup.Should().Be("Audio-Crew");
            result.ReleaseGroupOverride.Should().Be("Audio-Crew");
            result.CustomFormats.Select(format => format.Id).Should().Equal(1, 2);
            result.CustomFormats.Select(format => format.Name).Should().Equal("Preferred group", "Other condition");
            result.CustomFormatScore.Should().Be(80);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Audio-Crew")]
        public void should_preserve_detected_versus_explicit_group_choices_in_preview_json(string choice)
        {
            var item = new ManualImportItem
            {
                ReleaseGroup = choice ?? "DetectedCrew",
                ReleaseGroupOverride = choice
            };

            var result = Json.Deserialize<ManualImportResource>(item.ToResource().ToJson());

            result.ReleaseGroup.Should().Be(choice ?? "DetectedCrew");
            result.ReleaseGroupOverride.Should().Be(choice);
            result.CustomFormats.Should().BeEmpty();
            result.CustomFormatScore.Should().Be(0);
        }

        [TestCase(null, null, "DetectedCrew")]
        [TestCase("", "", "")]
        [TestCase("   ", "", "")]
        [TestCase("  Audio-Crew  ", "Audio-Crew", "Audio-Crew")]
        public void should_pass_explicit_group_choices_to_reprocessing_and_return_them(string group, string expectedChoice, string expectedGroup)
        {
            ManualImportItem captured = null;
            Mocker.GetMock<IManualImportService>()
                .Setup(service => service.UpdateItems(It.IsAny<List<ManualImportItem>>()))
                .Returns((List<ManualImportItem> items) =>
                {
                    captured = items.Single();
                    captured.ReleaseGroup = captured.ReleaseGroupOverride ?? "DetectedCrew";
                    return items;
                });

            var response = Subject.UpdateItems(new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource
                {
                    Id = 42,
                    Path = "/downloads/book.m4b",
                    ReleaseGroup = group
                }
            });

            var accepted = response.Should().BeOfType<AcceptedResult>().Subject;
            var result = accepted.Value.Should().BeOfType<List<ManualImportResource>>().Subject.Single();
            captured.Id.Should().Be(42);
            captured.Path.Should().Be("/downloads/book.m4b");
            captured.ReleaseGroupOverride.Should().Be(expectedChoice);
            result.ReleaseGroup.Should().Be(expectedGroup);
            result.ReleaseGroupOverride.Should().Be(expectedChoice);
        }
    }
}
