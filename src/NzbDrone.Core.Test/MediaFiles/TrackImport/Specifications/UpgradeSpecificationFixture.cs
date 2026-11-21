using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class UpgradeSpecificationFixture : CoreTest<UpgradeSpecification>
    {
        private LocalBook _item;
        private BookFile _existing;
        private List<BookFile> _files;
        private CustomFormat _preferred;
        private LocalBookCustomFormatScores _candidate;

        [SetUp]
        public void Setup()
        {
            _preferred = new CustomFormat { Id = 1, Name = "Preferred" };
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = _preferred, Score = 100 } }
            };
            var author = new Author { Id = 1, QualityProfile = profile };
            _existing = new BookFile { Id = 30, EditionId = 20, Part = 1, Quality = new QualityModel(Quality.MP3), Author = author };
            _files = new List<BookFile> { _existing };
            _item = new LocalBook
            {
                Path = "incoming.mp3", Part = 1, Quality = new QualityModel(Quality.MP3),
                Author = author, Edition = new Edition { Id = 20, BookId = 10 },
                Book = new Book { Id = 10, BookFiles = _files }
            };
            _candidate = new LocalBookCustomFormatScores
            {
                OriginalFormats = new List<CustomFormat>(), IntendedFormats = new List<CustomFormat>(), OriginalScore = 0, IntendedScore = 0
            };
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(service => service.ParseCustomFormat(_existing))
                .Returns(new List<CustomFormat> { _preferred });
            Mocker.GetMock<ILocalBookCustomFormatCalculationService>().Setup(service => service.Calculate(_item, It.IsAny<DownloadClientItem>()))
                .Returns(() => _candidate);
            Mocker.GetMock<IConfigService>().Setup(service => service.DownloadPropersAndRepacks).Returns(ProperDownloadTypes.PreferAndUpgrade);
        }

        [TestCase(0, false)]
        [TestCase(99, false)]
        [TestCase(100, true)]
        [TestCase(101, true)]
        public void same_quality_should_never_decrease_the_existing_custom_format_score(int score, bool accepted)
        {
            _candidate.IntendedScore = score;

            var result = Subject.IsSatisfiedBy(_item, null);

            result.Accepted.Should().Be(accepted);
            if (!accepted)
            {
                result.Code.Should().Be(RejectionCode.CustomFormatDowngrade);
            }
        }

        [TestCase(100)]
        [TestCase(150)]
        public void a_preference_lost_after_renaming_should_have_a_specific_explanation(int original)
        {
            _candidate.OriginalScore = original;
            _candidate.IntendedScore = 0;

            var result = Subject.IsSatisfiedBy(_item, null);

            result.Code.Should().Be(RejectionCode.CustomFormatLostAfterRename);
            result.Reason.Should().Contain("after renaming").And.Contain(original.ToString());
        }

        [TestCase(-200, false)]
        [TestCase(-100, true)]
        [TestCase(-50, true)]
        public void negative_format_scores_should_keep_the_same_ordering(int newScore, bool accepted)
        {
            _item.Author.QualityProfile.Value.FormatItems[0].Score = -100;
            _candidate.OriginalScore = -200;
            _candidate.IntendedScore = newScore;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().Be(accepted);
        }

        [TestCase(109, false)]
        [TestCase(110, true)]
        public void score_improvement_is_measured_after_renaming(int intended, bool accepted)
        {
            _item.Author.QualityProfile.Value.MinUpgradeFormatScore = 10;
            _candidate.OriginalScore = 120;
            _candidate.IntendedScore = intended;

            var decision = Subject.IsSatisfiedBy(_item, null);

            decision.Accepted.Should().Be(accepted);
            if (!accepted)
            {
                decision.Code.Should().Be(RejectionCode.CustomFormatUpgradeBelowMinimum);
            }
        }

        [Test]
        public void a_preferred_revision_remains_an_upgrade_at_the_same_format_score()
        {
            _item.Author.QualityProfile.Value.MinUpgradeFormatScore = 10;
            _item.Quality = new QualityModel(Quality.MP3, new Revision(version: 2));
            _candidate.IntendedScore = 100;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void no_existing_files_should_not_require_a_quality_profile_or_filename_preview()
        {
            _files.Clear();
            _item.Author.QualityProfile = null;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().BeTrue();

            Mocker.GetMock<ILocalBookCustomFormatCalculationService>().Verify(service => service.Calculate(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [TestCase(21)]
        [TestCase(0)]
        public void other_or_unproven_recording_identities_are_not_replacement_targets(int incomingEdition)
        {
            _item.Edition.Id = incomingEdition;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().BeTrue();

            Mocker.GetMock<ICustomFormatCalculationService>().Verify(service => service.ParseCustomFormat(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void a_higher_quality_keeps_precedence_over_custom_format_score()
        {
            _item.Quality = new QualityModel(Quality.FLAC);

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().BeTrue();

            Mocker.GetMock<ILocalBookCustomFormatCalculationService>().Verify(service => service.Calculate(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void a_better_custom_format_cannot_override_a_quality_downgrade()
        {
            _existing.Quality = new QualityModel(Quality.FLAC);
            _candidate.IntendedScore = 1000;

            Subject.IsSatisfiedBy(_item, null).Code.Should().Be(RejectionCode.QualityNotUpgrade);
        }

        [Test]
        public void a_preferred_proper_revision_cannot_be_downgraded()
        {
            _existing.Quality = new QualityModel(Quality.MP3, new Revision(version: 2));
            _candidate.IntendedScore = 1000;

            Subject.IsSatisfiedBy(_item, null).Code.Should().Be(RejectionCode.RevisionNotUpgrade);
        }

        [Test]
        public void ignoring_propers_still_requires_custom_format_preservation()
        {
            Mocker.GetMock<IConfigService>().Setup(service => service.DownloadPropersAndRepacks).Returns(ProperDownloadTypes.DoNotPrefer);
            _existing.Quality = new QualityModel(Quality.MP3, new Revision(version: 2));

            Subject.IsSatisfiedBy(_item, null).Code.Should().Be(RejectionCode.CustomFormatDowngrade);
        }

        [Test]
        public void a_new_proper_cannot_conceal_a_same_quality_custom_format_downgrade()
        {
            _item.Quality = new QualityModel(Quality.MP3, new Revision(version: 2));

            Subject.IsSatisfiedBy(_item, null).Code.Should().Be(RejectionCode.CustomFormatDowngrade);
        }

        [Test]
        public void reidentifying_an_existing_file_does_not_create_a_format_replacement()
        {
            _item.ExistingFile = true;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().BeTrue();

            Mocker.GetMock<ILocalBookCustomFormatCalculationService>().Verify(service => service.Calculate(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [TestCase(1, 150, false)]
        [TestCase(2, 150, false)]
        [TestCase(1, 250, true)]
        [TestCase(2, 250, true)]
        public void every_new_part_must_preserve_each_replaced_part_without_averaging(int newPart, int score, bool accepted)
        {
            var stronger = new CustomFormat { Id = 2, Name = "Stronger" };
            _item.Author.QualityProfile.Value.FormatItems.Add(new ProfileFormatItem { Format = stronger, Score = 200 });
            var second = new BookFile { Id = 31, EditionId = 20, Part = 2, Quality = new QualityModel(Quality.MP3) };
            _files.Add(second);
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(service => service.ParseCustomFormat(second)).Returns(new List<CustomFormat> { stronger });
            _candidate.IntendedScore = score;
            _item.Part = newPart;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().Be(accepted);

            Mocker.GetMock<ILocalBookCustomFormatCalculationService>().Verify(service => service.Calculate(_item, null), Times.Once());
        }

        [Test]
        public void an_alternate_marketplace_or_narration_does_not_lend_a_score_to_the_target_recording()
        {
            var other = new BookFile { Id = 32, EditionId = 21, Part = 1, Quality = new QualityModel(Quality.FLAC) };
            _files.Add(other);
            _candidate.IntendedScore = 100;

            Subject.IsSatisfiedBy(_item, null).Accepted.Should().BeTrue();

            Mocker.GetMock<ICustomFormatCalculationService>().Verify(service => service.ParseCustomFormat(other), Times.Never());
            Mocker.GetMock<ICustomFormatCalculationService>().Verify(service => service.ParseCustomFormat(_existing), Times.Once());
            Mocker.GetMock<ICustomFormatCalculationService>().Verify(service => service.ParseCustomFormat(It.IsAny<BookFile>(), It.IsAny<Author>()), Times.Never());
        }
    }
}
