using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Books;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class BookControllerRecordingIdentityFixture : TestBase<BookController>
    {
        private BookFile _file;

        private Book GivenBook(string identity)
        {
            var author = new Author
            {
                Id = 3,
                AuthorMetadataId = 3,
                Metadata = new AuthorMetadata { ForeignAuthorId = "audible:us:B000AP9A6K", Name = "Author" }
            };
            var book = new Book { Id = 23, ForeignBookId = identity, Title = "Selected recording", Author = author, AuthorMetadata = author.Metadata };
            var edition = new Edition { Id = 23, BookId = 23, ForeignEditionId = identity, Title = book.Title, Monitored = true, Book = book };
            book.Editions = new List<Edition> { edition };
            _file = new BookFile { Id = 587, EditionId = 42, Path = "/books/UK [B0F14RFHS6]/Book.m4b" };
            Mocker.GetMock<IBookService>().Setup(x => x.GetBook(23)).Returns(book);
            Mocker.GetMock<IMediaFileService>().Setup(x => x.GetFilesByBook(23)).Returns(new List<BookFile> { _file });
            return book;
        }

        [TestCase("audible:us:B0F14RPXHR")]
        [TestCase("audible:uk:B0F14RFHS6")]
        public void changing_audiobook_monitoring_should_not_reassign_any_file_identity(string identity)
        {
            var book = GivenBook(identity);

            Subject.UpdateBook(new BookResource { Id = 23, Monitored = true });

            book.Monitored.Should().BeTrue();
            _file.EditionId.Should().Be(42);
            Mocker.GetMock<IMediaFileService>().Verify(x => x.GetFilesByBook(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void should_preserve_legacy_edition_selection_behavior_for_non_native_books()
        {
            GivenBook("12345");

            Subject.UpdateBook(new BookResource { Id = 23, Monitored = true });

            _file.EditionId.Should().Be(23);
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Update(_file), Times.Once());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_allow_catalog_additions_without_queuing_an_author_rescan(bool doRefresh)
        {
            var book = GivenBook("audible:uk:B0F14RFHS6");
            Mocker.GetMock<IAddBookService>().Setup(x => x.AddBook(It.IsAny<Book>(), doRefresh)).Returns(book);

            Subject.AddBook(new BookResource { ForeignBookId = book.ForeignBookId }, doRefresh);

            Mocker.GetMock<IAddBookService>().Verify(x => x.AddBook(It.Is<Book>(b => b.ForeignBookId == book.ForeignBookId), doRefresh), Times.Once());
        }
    }
}
