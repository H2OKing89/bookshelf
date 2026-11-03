using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookRefreshFixture : CoreTest<RefreshBookService>
    {
        [Test]
        public void should_update_recording_date_and_editions_without_fetching_a_bibliography()
        {
            var resource = new AudnexusBookResource
            {
                Asin = "B0H8F9D4GD", Region = "us", Title = "Volume 1", ReleaseDate = new DateTime(2026, 7, 10),
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = "B0GVW7FF1Q", Name = "Yu Omiya" } },
                Narrators = new List<AudnexusContributorResource> { new AudnexusContributorResource { Name = "David Lee Huynh" } }
            };
            var local = AudiobookMetadataMapper.Book(resource, "us");
            var remote = AudiobookMetadataMapper.Book(resource, "us");
            local.Id = 7;
            local.ReleaseDate = new DateTime(2026, 1, 1);
            local.AuthorMetadataId = 8;
            local.AuthorMetadata.Value.Id = 8;
            var author = local.Author.Value;
            author.AuthorMetadataId = 8;
            var edition = local.Editions.Value.Single();
            edition.Id = 9;
            edition.BookId = 7;
            remote.Editions.Value.Single().RuntimeLengthMinutes = 360;
            Mocker.GetMock<IProvideBookInfo>().Setup(x => x.GetBookInfo(local.ForeignBookId))
                .Returns(Tuple.Create(author.ForeignAuthorId, remote, new List<AuthorMetadata> { remote.AuthorMetadata.Value }));
            Mocker.GetMock<IAuthorService>().Setup(x => x.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(x => x.GetBook(7)).Returns(local);
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsForRefresh(7, It.IsAny<List<string>>()))
                .Returns(new List<Edition> { edition });
            Mocker.SetConstant<IRefreshEditionService>(Mocker.Resolve<RefreshEditionService>());

            Subject.RefreshBookInfo(local).Should().BeTrue();

            local.ReleaseDate.Should().Be(resource.ReleaseDate);
            edition.RuntimeLengthMinutes.Should().Be(360);
            edition.Narrators.Should().Equal("David Lee Huynh");
            Mocker.GetMock<IProvideAuthorInfo>().Verify(x => x.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_retain_recording_when_it_is_delisted_or_regionally_unavailable()
        {
            var book = new Book { Id = 7, ForeignBookId = "audible:us:B0H8F9D4GD" };
            Mocker.GetMock<IProvideBookInfo>().Setup(x => x.GetBookInfo(book.ForeignBookId))
                .Throws(new BookNotFoundException(book.ForeignBookId));

            Subject.RefreshBookInfo(book).Should().BeFalse();

            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
