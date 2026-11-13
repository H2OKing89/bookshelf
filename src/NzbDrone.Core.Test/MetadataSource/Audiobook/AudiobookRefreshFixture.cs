using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
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
        public void should_preserve_local_recording_without_contacting_catalog_on_direct_or_author_refresh()
        {
            var local = new Book { Id = 7, ForeignBookId = LocalRecordingId.NewId(), Title = "Kaiju: Battlefield Surgeon" };
            Subject.RefreshBookInfo(local).Should().BeFalse();
            Subject.RefreshBookInfo(local, new List<Book>(), new Author(), true).Should().BeFalse();
            Mocker.GetMock<IProvideBookInfo>().Verify(service => service.GetBookInfo(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(service => service.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMetadataTagService>().Verify(service => service.SyncTags(It.IsAny<List<Edition>>()), Times.Never());
        }

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

        [TestCase(true)]
        [TestCase(false)]
        public void should_download_selected_recording_covers_without_forcing_file_tags_or_fetching_an_author_catalog(bool metadataChanged)
        {
            var (local, remote) = GivenRecordingRefresh();
            if (!metadataChanged)
            {
                local.ReleaseDate = remote.ReleaseDate;
            }

            var refreshed = new Book
            {
                Id = local.Id,
                ForeignBookId = local.ForeignBookId,
                Editions = remote.Editions.Value
            };
            var refreshEditions = new Mock<IRefreshEditionService>();
            Mocker.SetConstant<IRefreshEditionService>(refreshEditions.Object);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBook(local.Id)).Returns(refreshed);

            Subject.RefreshBookInfo(local);

            Mocker.GetMock<IMapCoversToLocal>().Verify(service => service.EnsureBookCovers(refreshed), Times.Once());
            refreshEditions.Verify(
                service => service.RefreshEditionInfo(
                    It.IsAny<List<Edition>>(),
                    It.IsAny<List<Edition>>(),
                    It.IsAny<List<Tuple<Edition, Edition>>>(),
                    It.IsAny<List<Edition>>(),
                    It.IsAny<List<Edition>>(),
                    It.IsAny<List<Edition>>(),
                    false),
                Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Update(It.IsAny<List<BookFile>>()), Times.Never());
            Mocker.GetMock<IMetadataTagService>().Verify(service => service.SyncTags(It.IsAny<List<Edition>>()), Times.Never());
            Mocker.GetMock<IProvideAuthorInfo>().Verify(service => service.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_retain_a_regional_recording_and_its_established_author_during_refresh()
        {
            var resource = new AudnexusBookResource
            {
                Asin = "B0F14RFHS6", Region = "uk", Title = "Harry Potter and the Philosopher's Stone (Full-Cast Edition)",
                Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = "B000AP9A6K", Name = "J.K. Rowling" } }
            };
            var author = new Author
            {
                Id = 2, AuthorMetadataId = 3,
                Metadata = AudiobookMetadataMapper.Author(resource.Authors.Single(), "us")
            };
            author.Metadata.Value.Id = 3;
            var local = AudiobookMetadataMapper.Book(resource, "uk", author.Metadata.Value);
            var remote = AudiobookMetadataMapper.Book(resource, "uk", author.Metadata.Value);
            local.Id = 7;
            local.Author = author;
            local.AuthorMetadataId = 3;
            var edition = local.Editions.Value.Single();
            edition.Id = 9;
            edition.BookId = 7;
            remote.Editions.Value.Single().RuntimeLengthMinutes = 521;
            Mocker.GetMock<IProvideBookInfo>().Setup(provider => provider.GetBookInfo(local.ForeignBookId))
                .Returns(Tuple.Create(author.ForeignAuthorId, remote, new List<AuthorMetadata> { author.Metadata.Value }));
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBook(7)).Returns(local);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(7, It.IsAny<List<string>>()))
                .Returns(new List<Edition> { edition });
            Mocker.SetConstant<IRefreshEditionService>(Mocker.Resolve<RefreshEditionService>());

            Subject.RefreshBookInfo(local).Should().BeTrue();

            local.ForeignBookId.Should().Be("audible:uk:B0F14RFHS6");
            local.AuthorMetadata.Value.ForeignAuthorId.Should().Be("audible:us:B000AP9A6K");
            local.AuthorMetadataId.Should().Be(3);
            edition.AudibleRegion.Should().Be("uk");
            edition.Asin.Should().Be("B0F14RFHS6");
            edition.RuntimeLengthMinutes.Should().Be(521);
            Mocker.GetMock<IAddAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Update(It.IsAny<List<BookFile>>()), Times.Never());
        }

        [Test]
        public void should_persist_a_second_series_and_changed_order_without_touching_sibling_links()
        {
            var (local, remote) = GivenRecordingRefresh();
            var originalSeries = new Series { Id = 40, ForeignSeriesId = "audible:us:B000000001", Title = "Stored series title", WorkCount = 12, PrimaryWorkCount = 7 };
            var omittedSeries = new Series { Id = 42, ForeignSeriesId = "audible:us:B000000003", Title = "Preserved membership" };
            var sibling = new Book { Id = 99 };
            var siblingLink = Link(sibling, originalSeries, 9, true);
            siblingLink.Id = 3;
            var links = new List<SeriesBookLink>
            {
                Link(local, originalSeries, 1, true),
                Link(local, omittedSeries, 7, false),
                siblingLink
            };
            links[0].Id = 1;
            links[1].Id = 2;
            local.SeriesLinks = links.Where(link => link.BookId == local.Id).ToList();
            remote.SeriesLinks = new List<SeriesBookLink>
            {
                Link(remote, new Series { ForeignSeriesId = originalSeries.ForeignSeriesId, Title = "A one-book response must not rename the shared series" }, 2, false),
                Link(remote, new Series { ForeignSeriesId = "audible:us:B000000002", Title = "Second series" }, 3, true)
            };
            Mocker.GetMock<ISeriesService>().Setup(service => service.FindById(It.IsAny<List<string>>()))
                .Returns(new List<Series> { originalSeries, omittedSeries });
            Mocker.GetMock<ISeriesService>().Setup(service => service.InsertMany(It.IsAny<IList<Series>>()))
                .Callback<IList<Series>>(items => items.Single().Id = 41);
            Mocker.GetMock<ISeriesBookLinkService>().Setup(service => service.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns((List<int> bookIds) => links.Where(link => bookIds.Contains(link.BookId)).ToList());
            Mocker.GetMock<ISeriesBookLinkService>().Setup(service => service.InsertMany(It.IsAny<List<SeriesBookLink>>()))
                .Callback<List<SeriesBookLink>>(items => links.AddRange(items));
            Mocker.GetMock<ISeriesBookLinkService>().Setup(service => service.UpdateMany(It.IsAny<List<SeriesBookLink>>()))
                .Callback<List<SeriesBookLink>>(items => items.ForEach(item => links[links.FindIndex(link => link.Id == item.Id)] = item));

            Subject.RefreshBookInfo(local).Should().BeTrue();

            var bookLinks = links.Where(link => link.BookId == local.Id).ToList();
            bookLinks.Should().HaveCount(3);
            bookLinks.Single(link => link.SeriesId == 40).Position.Should().Be("2");
            bookLinks.Single(link => link.SeriesId == 40).IsPrimary.Should().BeFalse();
            bookLinks.Single(link => link.SeriesId == 41).Position.Should().Be("3");
            bookLinks.Single(link => link.SeriesId == 41).IsPrimary.Should().BeTrue();
            bookLinks.Single(link => link.SeriesId == 42).Position.Should().Be("7");
            links.Single(link => link.BookId == sibling.Id).Should().BeSameAs(siblingLink);
            siblingLink.Position.Should().Be("9");
            siblingLink.IsPrimary.Should().BeTrue();
            originalSeries.Title.Should().Be("Stored series title");
            originalSeries.WorkCount.Should().Be(12);
            originalSeries.PrimaryWorkCount.Should().Be(7);
            Mocker.GetMock<ISeriesService>().Verify(service => service.UpdateMany(It.IsAny<IList<Series>>()), Times.Never());
            Mocker.GetMock<ISeriesBookLinkService>().Verify(service => service.DeleteMany(It.IsAny<List<SeriesBookLink>>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(aggregator => aggregator.PublishEvent(It.IsAny<BookUpdatedEvent>()), Times.Once());
        }

        [Test]
        public void should_keep_stored_series_when_the_single_book_response_omits_memberships()
        {
            var (local, _) = GivenRecordingRefresh();
            var series = new Series { Id = 40, ForeignSeriesId = "audible:us:B000000001", Title = "Stored membership" };
            var stored = Link(local, series, 1, true);
            local.SeriesLinks = new List<SeriesBookLink> { stored };

            Subject.RefreshBookInfo(local).Should().BeTrue();

            local.SeriesLinks.Value.Should().ContainSingle().Which.Should().BeSameAs(stored);
            Mocker.GetMock<ISeriesBookLinkService>().Verify(service => service.GetLinksByBook(It.IsAny<List<int>>()), Times.Never());
            Mocker.GetMock<ISeriesBookLinkService>().Verify(service => service.DeleteMany(It.IsAny<List<SeriesBookLink>>()), Times.Never());
        }

        private (Book Local, Book Remote) GivenRecordingRefresh()
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
            Mocker.GetMock<IProvideBookInfo>().Setup(service => service.GetBookInfo(local.ForeignBookId))
                .Returns(Tuple.Create(author.ForeignAuthorId, remote, new List<AuthorMetadata> { remote.AuthorMetadata.Value }));
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBook(7)).Returns(local);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(7, It.IsAny<List<string>>()))
                .Returns(new List<Edition> { edition });
            Mocker.SetConstant<IRefreshEditionService>(Mocker.Resolve<RefreshEditionService>());
            return (local, remote);
        }

        private static SeriesBookLink Link(Book book, Series series, int position, bool primary) => new SeriesBookLink
        {
            Book = book,
            BookId = book.Id,
            Series = series,
            SeriesId = series.Id,
            Position = position.ToString(),
            SeriesPosition = position * 1000,
            IsPrimary = primary
        };
    }
}
