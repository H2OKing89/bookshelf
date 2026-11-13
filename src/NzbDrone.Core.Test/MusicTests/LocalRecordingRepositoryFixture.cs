using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class LocalRecordingRepositoryFixture : DbTest<LocalRecordingRepository, BookFile>
    {
        private Book Book() => new Book { Author = new Author { Id = 1 }, AuthorMetadataId = 1, ForeignBookId = LocalRecordingId.NewId(), Title = "Kaiju", TitleSlug = Guid.NewGuid().ToString(), CleanTitle = "kaiju", Added = DateTime.UtcNow };
        private Edition Edition() => new Edition { ForeignEditionId = LocalRecordingId.NewId(), Title = "Kaiju", TitleSlug = Guid.NewGuid().ToString(), Monitored = true };
        private BookFile File(string path) => new BookFile { Path = path, Modified = DateTime.UtcNow, DateAdded = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), SceneName = "original", ReleaseGroup = "group", Size = 12, Part = 1, Quality = new QualityModel(Quality.MP3) };

        [SetUp]
        public void Setup()
        {
            Db.Insert(new Author { AuthorMetadataId = 1, Path = "/books/Matt Dinniman", CleanName = "mattdinniman", Added = DateTime.UtcNow });
        }

        [Test]
        public void should_link_files_and_preserve_import_history_in_one_transaction()
        {
            var file = File("/books/Kaiju/01.mp3");
            Storage.Insert(file);
            var book = Book();
            var edition = Edition();
            Subject.Create(book, edition, new List<BookFile> { StoredModel });
            StoredModel.EditionId.Should().Be(edition.Id);
            StoredModel.Id.Should().Be(file.Id);
            StoredModel.DateAdded.Should().Be(file.DateAdded);
            StoredModel.SceneName.Should().Be("original");
            StoredModel.ReleaseGroup.Should().Be("group");
            Db.All<Book>().Single().Id.Should().Be(book.Id);
        }

        [Test]
        public void should_rollback_books_editions_and_first_file_if_another_file_was_claimed()
        {
            var first = File("/books/Kaiju/01.mp3");
            var second = File("/books/Kaiju/02.mp3");
            Storage.InsertMany(new List<BookFile> { first, second });
            var selected = Storage.All().OrderBy(file => file.Id).ToList();
            second.EditionId = 99;
            Storage.Update(second);
            Assert.Throws<NzbDroneClientException>(() => Subject.Create(Book(), Edition(), selected));
            Db.All<Book>().Should().BeEmpty();
            Db.All<Edition>().Should().BeEmpty();
            Storage.Get(first.Id).EditionId.Should().Be(0);
            Storage.Get(second.Id).EditionId.Should().Be(99);
        }
    }
}
