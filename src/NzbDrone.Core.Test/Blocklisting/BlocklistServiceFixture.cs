using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Blocklisting
{
    [TestFixture]
    public class BlocklistServiceFixture : CoreTest<BlocklistService>
    {
        private DownloadFailedEvent _event;

        [SetUp]
        public void Setup()
        {
            _event = new DownloadFailedEvent
            {
                AuthorId = 12345,
                BookIds = new List<int> { 1 },
                Quality = new QualityModel(Quality.MP3),
                SourceTitle = "author.name.book.title",
                DownloadClient = "SabnzbdClient",
                DownloadId = "Sabnzbd_nzo_2dfh73k"
            };

            _event.Data.Add("publishedDate", DateTime.UtcNow.ToString("s") + "Z");
            _event.Data.Add("size", "1000");
            _event.Data.Add("indexer", "nzbs.org");
            _event.Data.Add("protocol", "1");
            _event.Data.Add("message", "Marked as failed");
        }

        [Test]
        public void should_match_full_v2_hash_without_matching_its_truncated_handle()
        {
            var handle = new string('A', 40);
            var fullHash = handle + new string('B', 24);
            var entry = new Blocklist { TorrentInfoHash = fullHash };
            Mocker.GetMock<IBlocklistRepository>()
                .Setup(repo => repo.BlocklistedByTorrentInfoHash(12345, It.IsAny<string>()))
                .Returns(new List<Blocklist> { entry });

            Assert.That(Subject.Blocklisted(12345, new TorrentInfo { DownloadProtocol = DownloadProtocol.Torrent, InfoHash = fullHash }), Is.True);
            Assert.That(Subject.Blocklisted(12345, new TorrentInfo { DownloadProtocol = DownloadProtocol.Torrent, InfoHash = handle }), Is.False);
            Assert.That(Subject.Blocklisted(12345, new TorrentInfo { DownloadProtocol = DownloadProtocol.Torrent, InfoHash = handle + new string('C', 24) }), Is.False);
        }

        [Test]
        public void should_add_to_repository()
        {
            Subject.Handle(_event);

            Mocker.GetMock<IBlocklistRepository>()
                .Verify(v => v.Insert(It.Is<Blocklist>(b => b.BookIds == _event.BookIds)), Times.Once());
        }

        [Test]
        public void should_add_to_repository_missing_size_and_protocol()
        {
            Subject.Handle(_event);

            _event.Data.Remove("size");
            _event.Data.Remove("protocol");

            Mocker.GetMock<IBlocklistRepository>()
                .Verify(v => v.Insert(It.Is<Blocklist>(b => b.BookIds == _event.BookIds)), Times.Once());
        }
    }
}
