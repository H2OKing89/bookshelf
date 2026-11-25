using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    public class PlexLibraryRefreshFixture : CoreTest<PlexServerService>
    {
        private string _root;
        private PlexServerSettings _settings;
        private List<PlexSection> _sections;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(TempFolder, "local-library");
            _settings = new PlexServerSettings
            {
                Host = "owned", Port = 32400, MapFrom = _root, MapTo = "/media/audiobooks"
            };
            _sections = new List<PlexSection>
            {
                new PlexSection
                {
                    Id = 7, Type = "artist",
                    Locations = new List<PlexSectionLocation> { new PlexSectionLocation { Path = "/media/audiobooks" } }
                },
                new PlexSection
                {
                    Id = 9, Type = "artist",
                    Locations = new List<PlexSectionLocation> { new PlexSectionLocation { Path = "/media/other-music" } }
                }
            };

            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());
            Mocker.GetMock<IRootFolderService>().Setup(service => service.GetBestRootFolderPath(It.IsAny<string>())).Returns(_root);
            Mocker.GetMock<IPlexServerProxy>().Setup(proxy => proxy.Version(It.IsAny<PlexServerSettings>())).Returns("1.40.2.1-owned");
            Mocker.GetMock<IPlexServerProxy>().Setup(proxy => proxy.GetTvSections(It.IsAny<PlexServerSettings>())).Returns(_sections);
        }

        [Test]
        public void one_targeted_mapped_music_library_refresh_for_a_repeated_author()
        {
            var path = Path.Combine(_root, "Owned author");

            Subject.UpdateLibraryPaths(new[] { path, path }, _settings);

            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.GetTvSections(_settings), Times.Once());
            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Update(7, "/media/audiobooks/Owned author", _settings), Times.Once());
            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Update(9, It.IsAny<string>(), _settings), Times.Never());
        }

        [Test]
        public void whole_root_refresh_does_not_produce_a_malformed_relative_path()
        {
            Subject.UpdateLibraryPaths(new[] { _root }, _settings);

            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Update(7, "/media/audiobooks", _settings), Times.Once());
        }

        [Test]
        public void legacy_fallback_updates_each_music_section_but_deduplicates_repeated_targets()
        {
            _settings.MapFrom = null;
            _settings.MapTo = null;
            var path = Path.Combine(_root, "Owned author");

            Subject.UpdateLibraryPaths(new[] { path, path }, _settings);

            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Update(7, "/media/audiobooks/Owned author", _settings), Times.Once());
            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Update(9, "/media/other-music/Owned author", _settings), Times.Once());
        }

        [Test]
        public void version_cache_uses_each_server_endpoint_not_just_hostname()
        {
            var second = new PlexServerSettings
            {
                Host = _settings.Host, Port = 32401, MapFrom = _root, MapTo = "/media/audiobooks"
            };

            Subject.UpdateLibraryPaths(new[] { _root }, _settings);
            Subject.UpdateLibraryPaths(new[] { _root }, _settings);
            Subject.UpdateLibraryPaths(new[] { _root }, second);

            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Version(_settings), Times.Once());
            Mocker.GetMock<IPlexServerProxy>().Verify(proxy => proxy.Version(second), Times.Once());
        }
    }
}
