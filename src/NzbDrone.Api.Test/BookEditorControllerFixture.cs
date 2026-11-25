using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;
using Readarr.Api.V1.Books;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class BookEditorControllerFixture
    {
        [Test]
        public void bulk_monitoring_only_updates_monitoring_and_returns_current_identity()
        {
            var books = new Mock<IBookService>();
            var controller = new BookEditorController(books.Object, Mock.Of<IManageCommandQueue>());
            var ids = new List<int> { 10 };
            var identity = LocalRecordingId.NewId();
            Book StoredBook(string title, int authorId, bool monitored) => new Book
            {
                Id = 10, ForeignBookId = identity, Title = title, Author = new Author { Id = authorId }, Monitored = monitored,
                Editions = new List<Edition> { new Edition { BookId = 10, ForeignEditionId = identity, Title = title, Monitored = true } }
            };
            books.SetupSequence(service => service.GetBooks(ids))
                .Returns(new List<Book> { StoredBook("Old local title", 21, false) })
                .Returns(new List<Book> { StoredBook("Corrected local title", 22, true) });

            var result = (AcceptedObjectResult)controller.SaveAll(new BookEditorResource { BookIds = ids, Monitored = true });

            books.Verify(service => service.SetMonitored(ids, true), Times.Once());
            books.Verify(service => service.UpdateMany(It.IsAny<List<Book>>()), Times.Never());
            var saved = ((List<BookResource>)result.Value).Should().ContainSingle().Which;
            saved.Title.Should().Be("Corrected local title");
            saved.AuthorId.Should().Be(22);
            saved.Monitored.Should().BeTrue();
            saved.ForeignBookId.Should().Be(identity);
        }
    }
}
