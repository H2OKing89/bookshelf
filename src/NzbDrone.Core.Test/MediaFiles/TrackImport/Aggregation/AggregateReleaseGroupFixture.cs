using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Aggregation
{
    [TestFixture]
    public class AggregateReleaseGroupFixture : CoreTest<AggregateReleaseGroup>
    {
        [TestCase("ChosenCrew", "ChosenCrew")]
        [TestCase(" ChosenCrew ", "ChosenCrew")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        public void should_preserve_manual_choice_through_repeated_augmentation(string choice, string expected)
        {
            var book = new LocalBook
            {
                ExistingFile = true,
                ReleaseGroupOverride = choice,
                DownloadClientBookInfo = new ParsedBookInfo { ReleaseGroup = "DownloadCrew" },
                FolderTrackInfo = new ParsedBookInfo { ReleaseGroup = "FolderCrew" },
                FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "FileCrew" }
            };

            Subject.Aggregate(book, true);
            book.ReleaseGroup.Should().Be(expected);

            book.DownloadClientBookInfo.ReleaseGroup = "NewParserGuess";
            Subject.Aggregate(book, true);

            book.ReleaseGroup.Should().Be(expected);
            book.ReleaseGroupOverride.Should().Be(expected);
        }

        [TestCase("DownloadCrew", "FolderCrew", "FileCrew", "DownloadCrew")]
        [TestCase(null, "FolderCrew", "FileCrew", "FolderCrew")]
        [TestCase(" ", "", "FileCrew", "FileCrew")]
        [TestCase(null, null, null, null)]
        public void should_keep_source_precedence_without_a_manual_choice(string downloadGroup, string folderGroup, string fileGroup, string expected)
        {
            var book = new LocalBook
            {
                DownloadClientBookInfo = new ParsedBookInfo { ReleaseGroup = downloadGroup },
                FolderTrackInfo = new ParsedBookInfo { ReleaseGroup = folderGroup },
                FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = fileGroup }
            };

            Subject.Aggregate(book, true);

            book.ReleaseGroup.Should().Be(expected);
            book.ReleaseGroupOverride.Should().BeNull();
        }

        [Test]
        public void should_allow_detection_when_no_override_or_source_objects_exist()
        {
            var book = new LocalBook();

            Subject.Aggregate(book, false);

            book.ReleaseGroup.Should().BeNull();
        }
    }
}
