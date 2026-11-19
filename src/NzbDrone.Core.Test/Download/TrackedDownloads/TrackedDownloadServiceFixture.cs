using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceFixture : CoreTest<TrackedDownloadService>
    {
        private TrackedDownload TrackCompletion(DownloadImportReceipt receipt)
        {
            var remote = new RemoteBook { Author = new Author { Id = 5 }, Books = new List<Book> { new Book { Id = 4 } } };
            var marker = new DownloadHistory { EventType = DownloadHistoryEventType.DownloadImported, DownloadId = "owned-download" };
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned-download")).Returns(new List<EntityHistory>());
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestDownloadHistoryItem("owned-download")).Returns(marker);
            Mocker.GetMock<IParsingService>().Setup(service => service.Map(It.IsAny<ParsedBookInfo>(), null)).Returns(remote);
            Mocker.GetMock<IDownloadImportCompletionService>().Setup(service => service.ReadReceipt(It.IsAny<TrackedDownload>(), marker)).Returns(receipt);
            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = new DownloadClientItem
            {
                DownloadId = "owned-download", Title = "Audio Author - Audio Book [2018 - FLAC]",
                OutputPath = new NzbDrone.Common.Disk.OsPath(System.IO.Path.Combine(TempFolder, "original")),
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1, Name = "owned" }
            };
            return Subject.TrackDownload(client, item);
        }

        [Test]
        public void a_legacy_or_invalid_completion_marker_should_restart_as_pending_for_review()
        {
            var tracked = TrackCompletion(null);

            tracked.Should().NotBeNull();
            tracked.State.Should().Be(TrackedDownloadState.ImportPending);
            tracked.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void a_validated_receipt_should_restore_completed_state_and_copy_intent_without_replaying_completion()
        {
            var receipt = new DownloadImportReceipt { KeepSourceFiles = true };

            var tracked = TrackCompletion(receipt);

            tracked.State.Should().Be(TrackedDownloadState.Imported);
            tracked.ImportReceipt.Should().BeSameAs(receipt);
            tracked.KeepSourceFiles.Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void cached_completion_should_not_follow_a_changed_client_or_output_path(bool changedClient)
        {
            var tracked = TrackCompletion(new DownloadImportReceipt());
            tracked.ImportItem = tracked.DownloadItem.Clone();
            var changed = tracked.DownloadItem.Clone();
            if (!changedClient)
            {
                changed.OutputPath = new NzbDrone.Common.Disk.OsPath(System.IO.Path.Combine(TempFolder, "new-output"));
            }

            var client = new DownloadClientDefinition { Id = changedClient ? 2 : 1, Protocol = DownloadProtocol.Torrent };

            var refreshed = Subject.TrackDownload(client, changed);

            refreshed.State.Should().Be(TrackedDownloadState.ImportPending);
            refreshed.ImportItem.Should().BeNull();
            refreshed.ImportReceipt.Should().BeNull();
            refreshed.Status.Should().Be(TrackedDownloadStatus.Warning);
            refreshed.DownloadClient.Should().Be(client.Id);
        }

        private void GivenDownloadHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns(new List<EntityHistory>()
                {
                    new EntityHistory()
                    {
                         DownloadId = "35238",
                         SourceTitle = "Audio Author - Audio Book [2018 - FLAC]",
                         AuthorId = 5,
                         BookId = 4,
                    }
                });
        }

        [Test]
        public void should_track_downloads_using_the_source_title_if_it_cannot_be_found_using_the_download_title()
        {
            GivenDownloadHistory();

            var remoteBook = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "Audio Book",
                    AuthorName = "Audio Author"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.BookTitle == "Audio Book" && i.AuthorName == "Audio Author"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteBook);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "The torrent release folder",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Id.Should().Be(5);
            trackedDownload.RemoteBook.Books.First().Id.Should().Be(4);
        }

        [Test]
        public void should_unmap_tracked_download_if_book_deleted()
        {
            GivenDownloadHistory();

            var remoteBook = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "Audio Book",
                    AuthorName = "Audio Author"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.BookTitle == "Audio Book" && i.AuthorName == "Audio Author"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteBook);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "Audio Author - Audio Book [2018 - FLAC]",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            // get a tracked download in place
            var trackedDownload = Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            // simulate deletion - book no longer maps
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.BookTitle == "Audio Book" && i.AuthorName == "Audio Author"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns(default(RemoteBook));

            // handle deletion event
            Subject.Handle(new BookInfoRefreshedEvent(remoteBook.Author, new List<Book>(), new List<Book>(), remoteBook.Books));

            // verify download has null remote book
            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteBook.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_episodes()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "TV Series"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Subject.Handle(new BookInfoRefreshedEvent(remoteEpisode.Author, new List<Book>(), new List<Book>(), remoteEpisode.Books));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteBook.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_series()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "TV Series",
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Subject.Handle(new AuthorDeletedEvent(remoteEpisode.Author, true, true));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteBook.Should().BeNull();
        }
    }
}
