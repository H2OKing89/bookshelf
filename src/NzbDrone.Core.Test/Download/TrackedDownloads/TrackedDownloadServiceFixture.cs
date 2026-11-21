using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceFixture : CoreTest<TrackedDownloadService>
    {
        [SetUp]
        public void SetRetryPolicy()
        {
            Mocker.SetConstant<IImportRetryService>(new ImportRetryService(TimeProvider.System));
        }

        private TrackedDownload TrackCompletion(DownloadImportReceipt receipt)
        {
            var remote = new RemoteBook { Author = new Author { Id = 5 }, Books = new List<Book> { new Book { Id = 4 } } };
            var marker = new DownloadHistory { EventType = DownloadHistoryEventType.DownloadImported, DownloadId = "owned-download" };
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned-download", 1)).Returns(new List<EntityHistory>());
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestDownloadHistoryItem("owned-download", 1)).Returns(marker);
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
        public void a_legacy_or_invalid_completion_marker_should_restart_as_blocked_for_review()
        {
            var tracked = TrackCompletion(null);

            tracked.Should().NotBeNull();
            tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
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

        [Test]
        public void cached_completion_should_not_follow_a_changed_output_path()
        {
            var tracked = TrackCompletion(new DownloadImportReceipt());
            tracked.ImportItem = tracked.DownloadItem.Clone();
            tracked.ImportCompletionAnnounced = true;
            var changed = tracked.DownloadItem.Clone();
            changed.OutputPath = new NzbDrone.Common.Disk.OsPath(System.IO.Path.Combine(TempFolder, "new-output"));

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };

            var refreshed = Subject.TrackDownload(client, changed);

            refreshed.State.Should().Be(TrackedDownloadState.ImportBlocked);
            refreshed.ImportItem.Should().BeNull();
            refreshed.ImportReceipt.Should().BeNull();
            refreshed.ImportCompletionAnnounced.Should().BeFalse();
            refreshed.Status.Should().Be(TrackedDownloadStatus.Warning);
            refreshed.DownloadClient.Should().Be(client.Id);
        }

        [Test]
        public void tracked_download_should_restore_only_its_own_grab_language_and_audio_evidence()
        {
            var remote = new RemoteBook
            {
                Author = new Author { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                Release = new ReleaseInfo
                {
                    MediaInfo = new MediaInfoModel { Source = "MyAnonaMouse", Language = "English", AudioFormat = "FLAC" }
                }
            };
            var grab = new EntityHistory { EventType = EntityHistoryEventType.Grabbed };
            RecordingLanguageEvidence.WriteHistory(grab.Data, new[] { Language.French, Language.Unknown }, 2);
            grab.Data[AudioFormatEvidence.HistoryKey] = new AudioFormatEvidence
            {
                Origin = AudioEvidenceOrigin.Release,
                Version = AudioFormatEvidence.SnapshotVersion,
                Codec = AudioCodecKind.Opus
            }.ToStored();
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("shared-download", 2))
                .Returns(new List<EntityHistory> { grab });
            Mocker.GetMock<IParsingService>().Setup(service => service.Map(It.IsAny<ParsedBookInfo>(), null)).Returns(remote);
            var item = new DownloadClientItem
            {
                DownloadId = "shared-download",
                Title = "Audio Author - Audio Book [2018 - FLAC]",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 2, Name = "owned" }
            };

            var tracked = Subject.TrackDownload(new DownloadClientDefinition { Id = 2 }, item);

            RecordingLanguageEvidence.ForRelease(tracked.RemoteBook).Should().Equal(Language.French, Language.Unknown);
            tracked.RemoteBook.SelectedEditionLanguageId.Should().Be(2);
            tracked.RemoteBook.Release.MediaInfo.Language.Should().Be("English");
            AudioFormatEvidence.ForRelease(tracked.RemoteBook).Codec.Should().Be(AudioCodecKind.Opus);
            tracked.RemoteBook.Release.MediaInfo.AudioFormat.Should().Be("FLAC");
            Mocker.GetMock<IHistoryService>().Verify(service => service.FindByDownloadId("shared-download", 2), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void incomplete_history_should_restore_as_blocked_and_survive_unreadable_diagnostics(bool invalidJson)
        {
            var marker = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadImportIncomplete,
                Data = new Dictionary<string, string> { ["statusMessages"] = invalidJson ? "invalid" : "[{\"title\":\"Part 1\",\"messages\":[\"Review matching\"]}]" }
            };
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.GetLatestDownloadHistoryItem("35238", 1)).Returns(marker);
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("35238", 1)).Returns(new List<EntityHistory>());
            var item = new DownloadClientItem
            {
                DownloadId = "35238", Title = "Audio Author - Audio Book [2018 - FLAC]",
                Status = DownloadItemStatus.Completed, DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1 }
            };

            var tracked = Subject.TrackDownload(new DownloadClientDefinition { Id = 1 }, item);

            tracked.Should().NotBeNull();
            tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
            tracked.ImportRetryAfterUtc.Should().BeAfter(DateTime.UtcNow);
            tracked.StatusMessages.Should().NotBeEmpty();
        }

        [Test]
        public void saving_settings_should_allow_a_blocked_import_to_be_rechecked_with_fresh_mapping()
        {
            var tracked = TrackCompletion(null);
            tracked.ImportItem = tracked.DownloadItem.Clone();
            tracked.ImportReceipt = new DownloadImportReceipt();
            tracked.ImportCompletionAnnounced = true;

            Subject.Handle(new ConfigSavedEvent());

            tracked.ImportRetryAfterUtc.Should().BeNull();
            tracked.ImportRetryCount.Should().Be(0);
            tracked.ImportItem.Should().BeNull();
            tracked.ImportReceipt.Should().BeNull();
            tracked.ImportCompletionAnnounced.Should().BeTrue();
            tracked.StatusMessages.Should().NotBeEmpty();
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        public void changing_a_client_resets_only_its_blocked_imports(int clientId, bool reset)
        {
            var tracked = TrackCompletion(null);

            Subject.Handle(new ProviderUpdatedEvent<IDownloadClient>(new DownloadClientDefinition { Id = clientId }));

            tracked.ImportRetryAfterUtc.HasValue.Should().Be(!reset);
        }

        [Test]
        public void refreshed_metadata_can_recover_a_blocked_matching_context()
        {
            var tracked = TrackCompletion(null);
            tracked.RemoteBook = null;
            var remapped = new RemoteBook { Author = new Author { Id = 5 }, Books = new List<Book> { new Book { Id = 4 } } };
            Mocker.GetMock<IParsingService>().Setup(service => service.Map(It.IsAny<ParsedBookInfo>(), null)).Returns(remapped);

            Subject.Handle(new BookInfoRefreshedEvent(new Author { Id = 5 }, new List<Book>(), new List<Book>(), new List<Book>()));

            tracked.RemoteBook.Should().BeSameAs(remapped);
            tracked.ImportRetryAfterUtc.Should().BeNull();
        }

        private void GivenDownloadHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238"), 1))
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
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>(), It.IsAny<int>()))
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
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>(), It.IsAny<int>()))
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
