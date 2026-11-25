using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Tags;
using Readarr.Api.V1.Calendar;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class CalendarFeedControllerFixture
    {
        [Test]
        public void recording_release_remains_an_all_day_event_with_stable_identity_and_tag_filter()
        {
            var author = new Author { Id = 7, Name = "Owned Author", Tags = new HashSet<int> { 4 } };
            var excludedAuthor = new Author { Id = 8, Name = "Excluded Author" };
            var recording = new Book
            {
                Id = 21,
                Author = author,
                Title = "Published title",
                ReleaseDate = new DateTime(2026, 12, 16, 22, 45, 0, DateTimeKind.Utc),
                Genres = new List<string> { "Fantasy", "Adventure" },
                Editions = new List<Edition>
                {
                    new Edition { Id = 30, Monitored = false, Overview = "Other edition" },
                    new Edition { Id = 31, Monitored = true, Overview = "Selected recording overview" }
                }
            };
            var excluded = new Book
            {
                Id = 22,
                Author = excludedAuthor,
                Title = "Excluded recording",
                ReleaseDate = new DateTime(2026, 12, 17),
                Editions = new List<Edition> { new Edition { Id = 32, Monitored = true, Overview = "Excluded overview" } }
            };
            var books = new Mock<IBookService>();
            books.Setup(service => service.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), true))
                .Returns(new List<Book> { recording, excluded });
            var authors = new Mock<IAuthorService>();
            authors.Setup(service => service.GetAuthor(author.Id)).Returns(author);
            authors.Setup(service => service.GetAuthor(excludedAuthor.Id)).Returns(excludedAuthor);
            var tags = new Mock<ITagService>();
            tags.Setup(service => service.GetTag("owned-tag")).Returns(new Tag { Id = 4 });
            var display = new Mock<IAudiobookMetadataOverrideService>();
            display.Setup(service => service.DisplayTitle(recording, It.IsAny<string>(), It.IsAny<Edition>())).Returns("Reviewed recording title");
            var series = new Mock<ISeriesBookLinkService>();
            series.Setup(service => service.GetLinksByBook(It.IsAny<List<int>>())).Returns(new List<SeriesBookLink>());
            var controller = new CalendarFeedController(books.Object, authors.Object, tags.Object, display.Object, series.Object);

            var result = (ContentResult)controller.GetCalendarFeed(7, 28, "owned-tag", true);

            result.ContentType.Should().Be("text/calendar");
            result.Content.Should().Contain("PRODID:")
                .And.Contain("X-WR-CALNAME:Readarr Book Schedule")
                .And.Contain("UID:Readarr_book_21")
                .And.Contain("DTSTART;VALUE=DATE:20261216")
                .And.Contain("DTEND;VALUE=DATE:20261217")
                .And.Contain("SUMMARY:Owned Author - Reviewed recording title")
                .And.Contain("DESCRIPTION:Selected recording overview")
                .And.Contain("Fantasy")
                .And.Contain("Adventure")
                .And.NotContain("UID:Readarr_book_22")
                .And.NotContain("Other edition")
                .And.NotContain("20261216T");
            books.Verify(service => service.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), true), Times.Once());
        }
    }
}
