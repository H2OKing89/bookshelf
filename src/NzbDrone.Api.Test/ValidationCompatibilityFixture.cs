using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using Readarr.Api.V1.Author;
using Readarr.Api.V1.Config;
using Readarr.Api.V1.Profiles.Delay;
using Readarr.Api.V1.Profiles.Quality;
using CoreRules = NzbDrone.Core.Validation.RuleBuilderExtensions;
using HttpRules = Readarr.Http.Validation.RuleBuilderExtensions;

namespace NzbDrone.Api.Test
{
    [SetCulture("en-US")]
    [SetUICulture("en-US")]
    public class ValidationCompatibilityFixture
    {
        [TestCase(null, "{url}")]
        [TestCase("", "")]
        [TestCase("not-a-url", "not-a-url")]
        public void custom_rule_preserves_failure_payload(string value, string messageValue)
        {
            var validator = new InlineValidator<Sample>();
            validator.RuleFor(x => x.Text).IsValidUrl().AsWarning();

            var failure = validator.Validate(new Sample { Text = value }).Errors.Should().ContainSingle().Subject;
            AssertFailure(failure, "Text", "UrlValidator", $"Invalid Url: '{messageValue}'", value);
            failure.CustomState.Should().BeSameAs(NzbDroneValidationState.Warning);
        }

        [TestCase(null, "NotNullValidator,NotEmptyValidator")]
        [TestCase("", "NotEmptyValidator")]
        [TestCase(" ", "NotEmptyValidator")]
        [TestCase("value", "")]
        public void not_blank_preserves_null_and_empty_distinction(string value, string expectedCodes)
        {
            var validator = new InlineValidator<Sample>();
            HttpRules.NotBlank(validator.RuleFor(x => x.Text));

            var failures = validator.Validate(new Sample { Text = value }).Errors;
            failures.Select(x => x.ErrorCode).Should().Equal(expectedCodes.Split(',', System.StringSplitOptions.RemoveEmptyEntries));
            failures.All(x => x.ErrorMessage == "'Text' must not be empty." && x.PropertyName == "Text").Should().BeTrue();
        }

        [TestCase(0, true)]
        [TestCase(9, false)]
        [TestCase(10, true)]
        [TestCase(120, true)]
        [TestCase(121, false)]
        public void rss_interval_retains_disabled_zero_and_boundary_values(int value, bool valid)
        {
            var validator = new InlineValidator<Sample>();
            HttpRules.IsValidRssSyncInterval(validator.RuleFor(x => x.Number));
            var result = validator.Validate(new Sample { Number = value });

            result.IsValid.Should().Be(valid);
            if (!valid)
            {
                AssertFailure(result.Errors.Single(), "Number", "RssSyncIntervalValidator", "Must be between 10 and 120 or 0 to disable", value);
            }
        }

        [Test]
        public void built_in_comparison_and_port_predicate_preserve_zero_failures()
        {
            var validator = new InlineValidator<Sample>();
            CoreRules.ValidPort(validator.RuleFor(x => x.Number));
            CoreRules.ValidId(validator.RuleFor(x => x.Number));
            CoreRules.IsZero(validator.RuleFor(x => x.Number));
            var errors = validator.Validate(new Sample()).Errors;

            errors.Should().HaveCount(3);
            AssertFailure(errors[0], "Number", "InclusiveBetweenValidator", "'Number' must be between 1 and 65535. You entered 0.", 0);
            AssertFailure(errors[1], "Number", "PredicateValidator", "The specified condition was not met for 'Number'.", 0);
            AssertFailure(errors[2], "Number", "GreaterThanValidator", "'Number' must be greater than '0'.", 0);
        }

        [Test]
        public void explicit_stop_cascade_does_not_query_filesystem_for_invalid_path()
        {
            var disk = new Mock<IDiskProvider>(MockBehavior.Strict);
            var validator = new InlineValidator<Sample>();
            validator.RuleFor(x => x.Text).Cascade(CascadeMode.Stop).IsValidPath().SetValidator(new PathExistsValidator(disk.Object));

            var failure = validator.Validate(new Sample()).Errors.Should().ContainSingle().Subject;
            AssertFailure(failure, "Text", "PathValidator", "Invalid Path: '{path}'", null);
            disk.VerifyNoOtherCalls();
        }

        [Test]
        public void injected_validator_does_not_share_rule_message_or_warning_state()
        {
            var rule = new GuidValidator();
            var first = new InlineValidator<Sample>();
            first.RuleFor(x => x.Text).SetValidator(rule).WithMessage("Custom message").AsWarning();
            var second = new InlineValidator<AuthorResource>();
            second.RuleFor(x => x.ForeignAuthorId).SetValidator(rule);

            var firstFailure = first.Validate(new Sample { Text = "invalid" }).Errors.Single();
            var secondFailure = second.Validate(new AuthorResource { ForeignAuthorId = "invalid" }).Errors.Single();
            AssertFailure(firstFailure, "Text", "GuidValidator", "Custom message", "invalid");
            firstFailure.CustomState.Should().BeSameAs(NzbDroneValidationState.Warning);
            AssertFailure(secondFailure, "ForeignAuthorId", "GuidValidator", "String is not a valid Guid", "invalid");
            secondFailure.CustomState.Should().BeNull();
        }

        [TestCase(0, true)]
        [TestCase(7, true)]
        [TestCase(8, false)]
        public void optional_provider_references_preserve_zero_and_missing_id_messages(int value, bool valid)
        {
            var quality = new Mock<IQualityProfileService>();
            var metadata = new Mock<IMetadataProfileService>();
            var client = new Mock<IDownloadClientFactory>();
            quality.Setup(x => x.Exists(7)).Returns(true);
            metadata.Setup(x => x.Exists(7)).Returns(true);
            client.Setup(x => x.Exists(7)).Returns(true);
            var validator = new InlineValidator<Sample>();
            validator.RuleFor(x => x.Number).SetValidator(new QualityProfileExistsValidator(quality.Object));
            validator.RuleFor(x => x.Number).SetValidator(new MetadataProfileExistsValidator(metadata.Object));
            validator.RuleFor(x => x.Number).SetValidator(new DownloadClientExistsValidator(client.Object));

            var result = validator.Validate(new Sample { Number = value });
            result.IsValid.Should().Be(valid);
            if (!valid)
            {
                result.Errors.Should().HaveCount(3);
                AssertFailure(result.Errors[0], "Number", "QualityProfileExistsValidator", "Quality Profile does not exist", value);
                AssertFailure(result.Errors[1], "Number", "MetadataProfileExistsValidator", "Metadata profile does not exist", value);
                AssertFailure(result.Errors[2], "Number", "DownloadClientExistsValidator", "Download Client does not exist", value);
            }

            var times = value == 0 ? Times.Never() : Times.Once();
            quality.Verify(x => x.Exists(value), times);
            metadata.Verify(x => x.Exists(value), times);
            client.Verify(x => x.Exists(value), times);
        }

        [Test]
        public void author_path_uses_owner_id_for_both_author_and_media_management_rules()
        {
            var authors = new Mock<IAuthorService>();
            var existingPath = System.IO.Path.GetFullPath("owned-author");
            authors.Setup(x => x.AllAuthorPaths()).Returns(new Dictionary<int, string> { { 7, existingPath } });
            var rule = new AuthorPathValidator(authors.Object);
            var author = new InlineValidator<AuthorResource>();
            author.RuleFor(x => x.Path).SetValidator(rule.For<AuthorResource>(x => x.Id));
            var media = new InlineValidator<MediaManagementConfigResource>();
            media.RuleFor(x => x.RecycleBin).SetValidator(rule.For<MediaManagementConfigResource>(x => x.Id));

            author.Validate(new AuthorResource { Id = 7, Path = existingPath }).IsValid.Should().BeTrue();
            author.Validate(new AuthorResource { Path = null }).IsValid.Should().BeTrue();
            var expectedMessage = $"Path '{existingPath}' is already configured for another author";
            AssertFailure(author.Validate(new AuthorResource { Id = 8, Path = existingPath }).Errors.Single(), "Path", "AuthorPathValidator", expectedMessage, existingPath);
            AssertFailure(media.Validate(new MediaManagementConfigResource { RecycleBin = existingPath }).Errors.Single(), "RecycleBin", "AuthorPathValidator", expectedMessage, existingPath);
        }

        [Test]
        public void delay_rule_allows_own_tags_and_rejects_other_owner_with_original_code()
        {
            var profiles = new Mock<IDelayProfileService>();
            profiles.Setup(x => x.All()).Returns(new List<DelayProfile> { new DelayProfile { Id = 7, Tags = new HashSet<int> { 5 } } });
            var validator = new InlineValidator<DelayProfileResource>();
            validator.RuleFor(x => x.Tags).SetValidator(new DelayProfileTagInUseValidator(profiles.Object).For<DelayProfileResource>(x => x.Id));
            var tags = new HashSet<int> { 5 };

            validator.Validate(new DelayProfileResource { Tags = null }).IsValid.Should().BeTrue();
            validator.Validate(new DelayProfileResource { Tags = new HashSet<int>() }).IsValid.Should().BeTrue();
            validator.Validate(new DelayProfileResource { Id = 7, Tags = tags }).IsValid.Should().BeTrue();
            AssertFailure(validator.Validate(new DelayProfileResource { Id = 8, Tags = tags }).Errors.Single(), "Tags", "DelayProfileTagInUseValidator", "One or more tags is used in another profile", tags);
        }

        [TestCase(null, true)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        public void empty_collection_preserves_optional_null_and_generic_error_code(int? length, bool valid)
        {
            var validator = new InlineValidator<Sample>();
            HttpRules.EmptyCollection(validator.RuleFor(x => x.Collection));
            var collection = length.HasValue ? Enumerable.Range(0, length.Value).ToArray() : null;
            var result = validator.Validate(new Sample { Collection = collection });
            result.IsValid.Should().Be(valid);
            if (!valid)
            {
                AssertFailure(result.Errors.Single(), "Collection", "EmptyCollectionValidator`1", "Collection Must Be Empty", collection);
            }
        }

        [Test]
        public void null_quality_items_preserve_all_failure_codes_and_order()
        {
            var validator = new InlineValidator<QualityProfileResource>();
            validator.RuleFor(x => x.Items).ValidItems();
            validator.RuleFor(x => x.Cutoff).ValidCutoff();

            var errors = validator.Validate(new QualityProfileResource()).Errors;
            errors.Select(x => x.ErrorCode).Should().Equal(
                "NotEmptyValidator",
                "AllowedValidator`1",
                "QualityNameValidator`1",
                "GroupItemValidator`1",
                "ItemGroupIdValidator`1",
                "UniqueIdValidator`1",
                "UniqueQualityIdValidator`1",
                "AllQualitiesValidator`1",
                "ItemGroupNameValidator`1",
                "ValidCutoffValidator`1");
            AssertFailure(errors.Last(), "Cutoff", "ValidCutoffValidator`1", "Cutoff must be an allowed quality or group", 0);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void cutoff_respects_explicit_allowed_flag(bool allowed)
        {
            var validator = new InlineValidator<QualityProfileResource>();
            validator.RuleFor(x => x.Cutoff).ValidCutoff();
            var resource = new QualityProfileResource
            {
                Cutoff = Quality.MP3.Id,
                Items = new List<QualityProfileQualityItemResource> { new QualityProfileQualityItemResource { Quality = Quality.MP3, Allowed = allowed } }
            };
            var result = validator.Validate(resource);
            result.IsValid.Should().Be(allowed);
            if (!allowed)
            {
                AssertFailure(result.Errors.Single(), "Cutoff", "ValidCutoffValidator`1", "Cutoff must be an allowed quality or group", Quality.MP3.Id);
            }
        }

        private static void AssertFailure(ValidationFailure failure, string property, string code, string message, object attemptedValue)
        {
            failure.PropertyName.Should().Be(property);
            failure.ErrorCode.Should().Be(code);
            failure.ErrorMessage.Should().Be(message);
            failure.AttemptedValue.Should().Be(attemptedValue);
            failure.Severity.Should().Be(Severity.Error);
        }

        public class Sample
        {
            public string Text { get; set; }
            public int Number { get; set; }
            public IEnumerable<int> Collection { get; set; }
        }
    }
}
