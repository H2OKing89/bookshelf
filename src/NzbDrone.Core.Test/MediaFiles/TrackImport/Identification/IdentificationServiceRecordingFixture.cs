using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class IdentificationServiceRecordingFixture : CoreTest<IdentificationService>
    {
        private static LocalBook LocalRecording(string asin)
        {
            return new LocalBook
            {
                Path = Path.Combine(Path.GetTempPath(), $"Book [{asin}].m4b"),
                FileTrackInfo = new ParsedTrackInfo
                {
                    Authors = new List<string> { "Author" },
                    BookTitle = "Book"
                }
            };
        }

        private static Edition Recording(string asin)
        {
            var author = new Author { Id = 1, Name = "Author" };
            var book = new Book
            {
                Id = 2,
                Title = "Book",
                ForeignBookId = $"audible:us:{asin}",
                Author = author,
                AuthorMetadata = author.Metadata
            };
            var edition = new Edition
            {
                Id = 3,
                BookId = book.Id,
                Book = book,
                Title = "Book",
                Asin = asin,
                ForeignEditionId = book.ForeignBookId
            };
            book.Editions = new List<Edition> { edition };
            return edition;
        }

        private void GivenCandidate(Edition edition, params BookFile[] existingFiles)
        {
            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), true))
                .Returns(new List<CandidateEdition>
                {
                    new CandidateEdition(edition) { ExistingFiles = new List<BookFile>(existingFiles) }
                });
        }

        private List<LocalEdition> Identify(List<LocalBook> tracks, IdentificationOverrides idOverrides = null)
        {
            return Subject.Identify(tracks, idOverrides ?? new IdentificationOverrides(), new ImportDecisionMakerConfig
            {
                SingleRelease = true,
                IncludeExisting = true,
                AddNewAuthors = true,
                KeepAllEditions = true
            });
        }

        [TestCase("B0BXV7C8PX", "B0HLLTHK38")]
        [TestCase("B0CCFTQW7R", "B0HLLTHK38")]
        [TestCase("B0F14RFHS6", "B0F14RPXHR")]
        public void should_not_identify_a_file_as_a_different_recording_even_with_an_override(string fileAsin, string wrongAsin)
        {
            var track = LocalRecording(fileAsin);
            var wrong = Recording(wrongAsin);
            GivenCandidate(wrong);

            var result = Identify(new List<LocalBook> { track }, new IdentificationOverrides { Edition = wrong, Book = wrong.Book.Value });

            result.Should().ContainSingle().Which.Edition.Should().BeNull();
            track.Edition.Should().BeNull();
            track.Book.Should().BeNull();
        }

        [TestCase("B0BXV7C8PX", "B0CCFTQW7R")]
        [TestCase("B0F14RPXHR", "B0F14RFHS6")]
        public void should_not_pull_a_different_existing_recording_into_the_match(string matchedAsin, string wrongAsin)
        {
            var track = LocalRecording(matchedAsin);
            var wrongTrack = LocalRecording(wrongAsin);
            var edition = Recording(matchedAsin);
            GivenCandidate(edition, new BookFile { Path = wrongTrack.Path, EditionId = edition.Id });
            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(wrongTrack.FileTrackInfo);

            var result = Identify(new List<LocalBook> { track });

            result.Should().ContainSingle().Which.Edition.Should().BeSameAs(edition);
            result[0].LocalBooks.Should().ContainSingle().Which.Should().BeSameAs(track);
            result[0].ExistingTracks.Should().BeEmpty();
            track.PartCount.Should().Be(1);
        }

        [Test]
        public void should_reject_mixed_recordings_in_a_forced_single_release()
        {
            GivenCandidate(Recording("B0BXV7C8PX"));

            var result = Identify(new List<LocalBook> { LocalRecording("B0BXV7C8PX"), LocalRecording("B0CCFTQW7R") });

            result.Should().ContainSingle().Which.Edition.Should().BeNull();
            result[0].LocalBooks.Should().OnlyContain(x => x.Edition == null && x.Book == null);
        }
    }
}
