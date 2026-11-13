using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class LocalRecordingServiceFixture : CoreTest<LocalRecordingService>
    {
        private BookFile _file;
        private AudioTag _tag;

        [SetUp]
        public void Setup()
        {
            _file = new BookFile { Id = 10, Path = "/books/Kaiju/01.mp3", DateAdded = new DateTime(2022, 1, 1), SceneName = "original", Part = 1 };
            _tag = new AudioTag
            {
                Title = "Chapter 1", Book = "Kaiju: Battlefield Surgeon", BookAuthors = new[] { "Matt Dinniman" },
                Performers = Array.Empty<string>(), Narrators = new[] { "Jeff Hays" }, Track = 1, Duration = TimeSpan.FromMinutes(12), Year = 2022
            };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { _file });
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IAudioTagService>().Setup(tags => tags.ReadAudioTag(It.IsAny<string>())).Returns(_tag);
            var author = new Author { Id = 116, AuthorMetadataId = 116, Name = "Matt Dinniman" };
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindByName("Matt Dinniman")).Returns(author);
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthor(116)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBook(It.IsAny<int>())).Returns(new Book());
        }

        [Test]
        public void should_create_distinct_local_identity_preserving_file_provenance()
        {
            Book savedBook = null;
            Edition savedEdition = null;
            Mocker.GetMock<ILocalRecordingRepository>().Setup(repo => repo.Create(It.IsAny<Book>(), It.IsAny<Edition>(), It.IsAny<List<BookFile>>()))
                .Callback<Book, Edition, List<BookFile>>((book, edition, files) =>
                {
                    savedBook = book;
                    savedEdition = edition;
                });

            Subject.Create(new LocalRecording { FileIds = new List<int> { 10 }, Title = "Kaiju: Battlefield Surgeon", AuthorId = 116, Narrators = new List<string> { "Jeff Hays" } });

            LocalRecordingId.IsLocal(savedBook.ForeignBookId).Should().BeTrue();
            savedEdition.ForeignEditionId.Should().Be(savedBook.ForeignBookId);
            savedEdition.Asin.Should().BeNull();
            savedEdition.Narrators.Should().Equal("Jeff Hays");
            savedEdition.RuntimeLengthMinutes.Should().Be(12);
            savedBook.Monitored.Should().BeFalse();
            savedBook.AddOptions.AddType.Should().Be(BookAddType.Manual);
            _file.Id.Should().Be(10);
            _file.DateAdded.Should().Be(new DateTime(2022, 1, 1));
            _file.SceneName.Should().Be("original");
        }

        [Test]
        public void should_recheck_mapping_before_create()
        {
            Subject.Preview(new List<int> { 10 });
            _file.EditionId = 123;
            Assert.Throws<NzbDroneClientException>(() => Subject.Create(new LocalRecording { FileIds = new List<int> { 10 }, Title = "Kaiju", AuthorId = 116 }));
            Mocker.GetMock<ILocalRecordingRepository>().Verify(repo => repo.Create(It.IsAny<Book>(), It.IsAny<Edition>(), It.IsAny<List<BookFile>>()), Times.Never());
        }

        [Test]
        public void should_reject_a_filename_asin()
        {
            _file.Path = "/books/Kaiju/Kaiju [B0FXY6DVJS].mp3";
            Assert.Throws<NzbDroneClientException>(() => Subject.Preview(new List<int> { 10 }));
        }

        [Test]
        public void should_reject_duplicate_ids_and_missing_files()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.Preview(new List<int> { 10, 10 }));
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FileExists(_file.Path)).Returns(false);
            Assert.Throws<NzbDroneClientException>(() => Subject.Preview(new List<int> { 10 }));
        }

        [Test]
        public void should_order_multiple_discs_and_use_album_title()
        {
            var second = new BookFile { Id = 11, Path = "/books/Kaiju/Disc 2/01.mp3" };
            _file.Path = "/books/Kaiju/Disc 1/01.mp3";
            _tag.Disc = 1;
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { second, _file });
            Mocker.GetMock<IAudioTagService>().Setup(tags => tags.ReadAudioTag(second.Path)).Returns(new AudioTag
            {
                Title = "Chapter 2", Book = _tag.Book, BookAuthors = _tag.BookAuthors, Performers = Array.Empty<string>(), Disc = 2, Track = 1
            });

            var preview = Subject.Preview(new List<int> { 10, 11 });
            preview.Title.Should().Be("Kaiju: Battlefield Surgeon");
            preview.AuthorId.Should().Be(116);
            Subject.Create(preview);
            _file.Part.Should().Be(1);
            second.Part.Should().Be(2);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_reject_mixed_author_or_narrator_credits(bool changeAuthor)
        {
            var second = new BookFile { Id = 11, Path = "/books/Kaiju/02.mp3" };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { _file, second });
            Mocker.GetMock<IAudioTagService>().Setup(tags => tags.ReadAudioTag(second.Path)).Returns(new AudioTag
            {
                Book = _tag.Book, Performers = Array.Empty<string>(),
                BookAuthors = changeAuthor ? new[] { "Someone Else" } : _tag.BookAuthors,
                Narrators = changeAuthor ? _tag.Narrators : new[] { "Another Narrator" }
            });
            Assert.Throws<NzbDroneClientException>(() => Subject.Preview(new List<int> { 10, 11 }));
        }

        [Test]
        public void should_reject_missing_file_ids_with_a_client_error()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.Preview(new List<int> { 10, 999 }));
        }

        [Test]
        public void should_reject_mixing_recording_folders()
        {
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile>
            {
                _file, new BookFile { Id = 11, Path = "/books/Other/01.mp3" }
            });
            Assert.Throws<NzbDroneClientException>(() => Subject.Preview(new List<int> { 10, 11 }));
        }
    }
}
