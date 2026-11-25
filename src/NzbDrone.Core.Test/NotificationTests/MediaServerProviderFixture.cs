using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Plex.PlexTv;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.Notifications.Subsonic;
using NzbDrone.Core.Test.Framework;
using SubsonicNotification = NzbDrone.Core.Notifications.Subsonic.Subsonic;

namespace NzbDrone.Core.Test.NotificationTests
{
    public class MediaServerProviderFixture : CoreTest
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());
        }

        private BookDownloadMessage[] OwnedBatch()
        {
            var author = new Author { Path = Path.Combine(TempFolder, "author") };
            return new[]
            {
                new BookDownloadMessage
                {
                    Author = author, Message = "Owned first book",
                    BookFiles = new List<BookFile>
                    {
                        new BookFile { Path = Path.Combine(author.Path, "book1", "part1.m4b") },
                        new BookFile { Path = Path.Combine(author.Path, "book1", "part2.m4b") }
                    }
                },
                new BookDownloadMessage
                {
                    Author = author, Message = "Owned second book",
                    BookFiles = new List<BookFile>
                    {
                        new BookFile { Path = Path.Combine(author.Path, "book2", "part1.m4b") },
                        new BookFile { Path = Path.Combine(TempFolder, "other-location", "part2.m4b") }
                    }
                }
            };
        }

        [Test]
        public void plex_refreshes_actual_committed_directories_once_per_author_path()
        {
            var plex = Mocker.Resolve<PlexServer>();
            var settings = new PlexServerSettings { Host = "owned" };
            plex.Definition = new NotificationDefinition { Id = 1, Settings = settings };
            var messages = OwnedBatch();
            List<string> paths = null;
            Mocker.GetMock<IPlexServerService>().Setup(service => service.UpdateLibraryPaths(It.IsAny<IEnumerable<string>>(), settings))
                .Callback<IEnumerable<string>, PlexServerSettings>((targets, _) => paths = targets.ToList());

            plex.QueueImportBatch(messages, true).Should().BeFalse();
            plex.ProcessPendingUpdates().Should().BeTrue();
            plex.ProcessPendingUpdates().Should().BeFalse();

            paths.Should().BeEquivalentTo(new[] { messages[0].Author.Path, Path.Combine(TempFolder, "other-location") });
            Mocker.GetMock<IPlexTvService>().Verify(service => service.Ping(settings.AuthToken), Times.Once());
            Mocker.GetMock<IPlexServerService>().Verify(service => service.UpdateLibraryPaths(It.IsAny<IEnumerable<string>>(), settings), Times.Once());
        }

        [Test]
        public void plex_respects_disabled_library_update()
        {
            var plex = Mocker.Resolve<PlexServer>();
            plex.Definition = new NotificationDefinition { Id = 1, Settings = new PlexServerSettings { Host = "owned", UpdateLibrary = false } };

            plex.QueueImportBatch(OwnedBatch(), true);
            plex.ProcessPendingUpdates().Should().BeFalse();

            Mocker.GetMock<IPlexServerService>().Verify(service => service.UpdateLibraryPaths(It.IsAny<IEnumerable<string>>(), It.IsAny<PlexServerSettings>()), Times.Never());
        }

        [Test]
        public void subsonic_upgrade_deletion_keeps_per_part_chat_but_defers_scan_to_commit_boundary()
        {
            var subsonic = Mocker.Resolve<SubsonicNotification>();
            var settings = new SubsonicSettings { Host = "owned", Notify = true, UpdateLibrary = true };
            subsonic.Definition = new NotificationDefinition { Id = 1, Settings = settings };
            subsonic.OnBookFileDelete(new BookFileDeleteMessage { IsPartOfImportBatch = true, Message = "Part one" });
            subsonic.OnBookFileDelete(new BookFileDeleteMessage { IsPartOfImportBatch = true, Message = "Part two" });
            subsonic.ProcessPendingUpdates().Should().BeFalse();

            subsonic.QueueImportBatch(new List<BookDownloadMessage>(), true, true);
            subsonic.ProcessPendingUpdates().Should().BeTrue();

            Mocker.GetMock<ISubsonicService>().Verify(service => service.Notify(settings, It.IsAny<string>()), Times.Exactly(2));
            Mocker.GetMock<ISubsonicService>().Verify(service => service.Update(settings), Times.Once());
        }

        [TestCase(true, true, 2, 1)]
        [TestCase(false, true, 0, 1)]
        [TestCase(true, false, 2, 0)]
        public void subsonic_keeps_per_book_chat_but_scans_once_when_enabled(bool sendChat, bool updateLibrary, int chatCount, int scanCount)
        {
            var subsonic = Mocker.Resolve<SubsonicNotification>();
            var settings = new SubsonicSettings { Host = "owned", Notify = true, UpdateLibrary = updateLibrary };
            subsonic.Definition = new NotificationDefinition { Id = 1, Settings = settings };

            subsonic.QueueImportBatch(OwnedBatch(), sendChat).Should().Be(sendChat);
            subsonic.ProcessPendingUpdates().Should().Be(updateLibrary);
            subsonic.ProcessPendingUpdates().Should().BeFalse();

            Mocker.GetMock<ISubsonicService>().Verify(service => service.Notify(settings, It.IsAny<string>()), Times.Exactly(chatCount));
            Mocker.GetMock<ISubsonicService>().Verify(service => service.Update(settings), Times.Exactly(scanCount));
        }
    }
}
