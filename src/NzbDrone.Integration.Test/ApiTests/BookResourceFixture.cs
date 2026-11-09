using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using Readarr.Api.V1.Books;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class BookResourceFixture
    {
        [Test]
        public void should_expose_metadata_warnings_without_changing_saved_dates_or_recording_identity()
        {
            var releaseDate = new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc);
            var book = new Book { Title = "Main", ForeignBookId = "audible:us:B012345678", ReleaseDate = releaseDate };
            var edition = new Edition
            {
                Title = "Main", Asin = "B012345678", ForeignEditionId = "audible:us:B012345678",
                Monitored = true, Book = book, ReleaseDate = releaseDate
            };
            book.Editions = new List<Edition> { edition };
            var warnings = new List<string> { "Review the provider release date." };
            var overrides = new Mock<IAudiobookMetadataOverrideService>();
            overrides.Setup(x => x.Project(book, edition, null))
                .Returns(new AudiobookMetadataProjection { Title = "Main", DisplayTitle = "Main", Warnings = warnings });

            var resource = book.ToResource(overrides.Object);

            resource.MetadataWarnings.Should().Equal(warnings);
            resource.ReleaseDate.Should().Be(releaseDate);
            resource.ForeignBookId.Should().Be(book.ForeignBookId);
            resource.ForeignEditionId.Should().Be(edition.ForeignEditionId);
            book.ReleaseDate.Should().Be(releaseDate);
            edition.ReleaseDate.Should().Be(releaseDate);
            edition.Asin.Should().Be("B012345678");
        }

        [Test]
        public void should_return_an_empty_warning_list_when_no_review_is_needed()
        {
            var book = new Book { Title = "Main", ReleaseDate = new DateTime(2020, 1, 1) };
            book.Editions = new List<Edition> { new Edition { Title = "Main", Monitored = true, Book = book } };

            book.ToResource().MetadataWarnings.Should().BeEmpty();
        }
    }
}
