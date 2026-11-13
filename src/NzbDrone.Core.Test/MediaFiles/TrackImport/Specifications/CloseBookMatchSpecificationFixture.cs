using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class CloseBookMatchSpecificationFixture : CoreTest<CloseBookMatchSpecification>
    {
        private LocalEdition Match(string fileTitle, string fileAuthor, string catalogTitle, string catalogAuthor, string extension = ".mp3", bool localRecording = true)
        {
            var recordingId = localRecording && extension != ".epub" ? "local:recording:4eec203cec2c49f9b9e4361bdb378e1a" : "audible:us:B0FXY6DVJS";
            var metadata = new AuthorMetadata { Name = catalogAuthor };
            var book = new Book
            {
                Title = catalogTitle,
                ForeignBookId = recordingId,
                AuthorMetadata = metadata,
                Author = new Author { Metadata = metadata },
                SeriesLinks = new List<SeriesBookLink>()
            };
            var edition = new Edition
            {
                Title = catalogTitle,
                Book = book,
                Asin = LocalRecordingId.IsLocal(recordingId) ? null : "B0FXY6DVJS",
                ForeignEditionId = recordingId,
                Format = extension == ".epub" ? "ebook" : "Audiobook",
                Language = "eng",
                ReleaseDate = new DateTime(2020, 1, 1),
                Publisher = "Publisher"
            };
            var track = new LocalBook
            {
                Path = "/library/recording" + extension,
                FileTrackInfo = new ParsedTrackInfo
                {
                    BookTitle = fileTitle,
                    Authors = fileAuthor == null ? new List<string>() : new List<string> { fileAuthor },
                    Language = "eng",
                    Year = 2020,
                    Publisher = "Publisher"
                }
            };
            var result = new LocalEdition(new List<LocalBook> { track }) { Edition = edition };
            Score(result);
            return result;
        }

        private static void Score(LocalEdition item)
        {
            item.Distance = DistanceCalculator.BookDistance(item.LocalBooks, item.Edition);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_require_recording_confirmation_without_an_asin_even_when_title_and_author_match(bool newDownload)
        {
            var item = Match("Kaiju: Battlefield Surgeon", "Matt Dinniman", "Kaiju: Battlefield Surgeon", "Matt Dinniman", localRecording: false);
            item.NewDownload = newDownload;
            item.Distance.NormalizedDistance().Should().BeLessThan(0.50);

            var result = Subject.IsSatisfiedBy(item, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Contain("recording identity confirmation");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_let_matching_metadata_outweigh_a_different_audiobook_title(bool newDownload)
        {
            var item = Match("Kaiju: Battlefield Surgeon", "Matt Dinniman", "A Parade of Horribles", "Matt Dinniman");
            item.NewDownload = newDownload;
            item.Distance.NormalizedDistance().Should().BeLessThan(0.50);

            var result = Subject.IsSatisfiedBy(item, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Contain("title");
        }

        [Test]
        public void should_not_match_the_same_title_to_a_different_author()
        {
            var item = Match("The Visitor", "Lee Child", "The Visitor", "Lori Wick");
            item.Distance.NormalizedDistance().Should().BeLessThan(0.50);

            var result = Subject.IsSatisfiedBy(item, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Contain("author");
        }

        [TestCase("Kaiju Battlefield Surgeon", "Kaiju: Battlefield Surgeon")]
        [TestCase("The Silver Chair (Unabridged)", "The Silver Chair")]
        [TestCase("Kaiju: Battlefield Surgon", "Kaiju: Battlefield Surgeon")]
        public void should_allow_close_title_variants_without_an_asin(string fileTitle, string catalogTitle)
        {
            var item = Match(fileTitle, "Example Author", catalogTitle, "Example Author");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_consider_the_library_title_alongside_the_published_title()
        {
            var item = Match("Unital Ring III", "Reki Kawahara", "Sword Art Online 24 (light novel)", "Reki Kawahara");
            item.Edition.Book.Value.Title = "Unital Ring III";
            Score(item);

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_saved_author_alias()
        {
            var item = Match("Accomplishments of the Duke's Daughter", "Reia", "Accomplishments of the Duke's Daughter", "澪亜");
            item.Edition.Book.Value.AuthorMetadata.Value.Aliases.Add("Reia");
            Score(item);

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_reversed_author_names()
        {
            var item = Match("Sword Art Online 10", "Kawahara, Reki", "Sword Art Online 10", "Reki Kawahara");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_series_and_number_title_variants()
        {
            var item = Match("The Chronicles of Narnia Book 6 The Silver Chair", "C. S. Lewis", "The Silver Chair", "C. S. Lewis");
            item.Edition.Book.Value.SeriesLinks = new List<SeriesBookLink>
            {
                new SeriesBookLink { Series = new Series { Title = "The Chronicles of Narnia" }, Position = "6" }
            };
            Score(item);

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [TestCase(null, "Example Author", "title")]
        [TestCase("Book", null, "author")]
        [TestCase(null, null, "title")]
        public void should_require_missing_audio_identity_information_to_be_reviewed(string title, string author, string reason)
        {
            var item = Match(title, author, "Book", "Example Author");

            var result = Subject.IsSatisfiedBy(item, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Contain(reason);
        }

        [Test]
        public void should_keep_exact_asin_matching_despite_unreliable_descriptive_tags()
        {
            var item = Match("Unreliable title", "Unreliable author", "A Parade of Horribles", "Matt Dinniman", localRecording: false);
            item.LocalBooks[0].FileTrackInfo.Asin = item.Edition.Asin;
            Score(item);

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_leave_ebook_weighted_matching_unchanged()
        {
            var item = Match("Kaiju: Battlefield Surgeon", "Matt Dinniman", "A Parade of Horribles", "Matt Dinniman", ".epub");
            item.Distance.NormalizedDistance().Should().BeLessThan(0.50);

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_allow_a_local_recording_with_a_close_confirmed_title()
        {
            var item = Match("Kaiju Battlefield Surgon", "Matt Dinniman", "Kaiju: Battlefield Surgeon", "Matt Dinniman");
            item.Edition.Asin = null;
            item.Edition.ForeignEditionId = "local:recording:4eec203c-ec2c-49f9-b9e4-361bdb378e1a";
            item.Edition.Book.Value.ForeignBookId = item.Edition.ForeignEditionId;
            Score(item);

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }
    }
}
