using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Books;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class BookChaptersFixture : TestBase<BookController>
    {
        [Test]
        public void should_return_only_the_selected_recordings_stored_chapters()
        {
            var selected = new AudiobookChapterMetadata
            {
                RuntimeLengthMs = 62000,
                Chapters = new List<AudiobookChapter> { new AudiobookChapter { Ordinal = 1, Title = "Chapter one", LengthMs = 62000 } }
            };
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(42)).Returns(new List<Edition>
            {
                new Edition { BookId = 42, Monitored = false, ChapterMetadata = new AudiobookChapterMetadata() },
                new Edition { BookId = 42, Monitored = true, ChapterMetadata = selected }
            });

            Subject.Chapters(42).Value.Should().BeSameAs(selected);

            Mocker.GetMock<IEditionService>().Verify(service => service.GetEditionsByBook(42), Times.Once());
        }

        [Test]
        public void should_return_an_empty_list_when_the_recording_has_no_chapters()
        {
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(42))
                .Returns(new List<Edition> { new Edition { BookId = 42, Monitored = true } });

            var response = Subject.Chapters(42);

            response.Result.Should().BeNull();
            response.Value.Chapters.Should().BeEmpty();
            response.Value.RuntimeLengthMs.Should().BeNull();
        }

        [Test]
        public void should_return_not_found_when_no_recording_is_selected()
        {
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(42)).Returns(new List<Edition>());

            Subject.Chapters(42).Result.Should().BeOfType<NotFoundResult>();
        }
    }
}
