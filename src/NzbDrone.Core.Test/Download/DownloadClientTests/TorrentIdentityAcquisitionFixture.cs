using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using MonoTorrent;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.Test.Download.DownloadClientTests
{
    [TestFixture]
    public class TorrentIdentityAcquisitionFixture : DownloadClientFixtureBase<QBittorrent>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ITorrentFileInfoReader>(new TorrentFileInfoReader(Mocker.Resolve<Logger>()));
            Subject.Definition = new DownloadClientDefinition
            {
                Settings = new QBittorrentSettings { Host = "127.0.0.1", Port = 2222, MusicCategory = "books" }
            };
            Mocker.GetMock<IQBittorrentProxy>().Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>()))
                .Returns(new QBittorrentPreferences { DhtEnabled = true });
            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                .Returns(Mocker.GetMock<IQBittorrentProxy>().Object);
        }

        private RemoteBook GivenRelease(string url)
        {
            var book = CreateRemoteBook();
            book.Release = new TorrentInfo { Title = "Owned synthetic audiobook", DownloadUrl = url, DownloadProtocol = DownloadProtocol.Torrent };
            return book;
        }

        private async Task<byte[]> GivenTorrent(TorrentType type)
        {
            var bytes = await TorrentIdentityFixture.CreateOwnedTorrent(type);
            Mocker.GetMock<IHttpClient>().Setup(s => s.GetAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader(), bytes)));
            return bytes;
        }

        private void VerifyNoSubmission()
        {
            Mocker.GetMock<IQBittorrentProxy>().Verify(s => s.AddTorrentFromUrl(It.IsAny<string>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            Mocker.GetMock<IQBittorrentProxy>().Verify(s => s.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public async Task should_submit_hybrid_file_and_track_original_v1_hash()
        {
            var bytes = await GivenTorrent(TorrentType.V1V2Hybrid);
            var expected = Torrent.Load(bytes).InfoHashes.V1.ToHex();
            var book = GivenRelease("http://127.0.0.1/owned.torrent");
            (await Subject.Download(book, CreateIndexer())).Should().Be(expected);
            ((TorrentInfo)book.Release).InfoHash.Should().Be(expected);
            Mocker.GetMock<IQBittorrentProxy>().Verify(s => s.AddTorrentFromFile(It.IsAny<string>(), bytes, It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public async Task should_submit_hybrid_magnet_and_track_same_v1_identity()
        {
            var uri = "magnet:?xt=" + TorrentIdentityFixture.V2Topic + "&xt=" + TorrentIdentityFixture.V1Topic;
            var book = GivenRelease(uri);
            (await Subject.Download(book, CreateIndexer())).Should().Be(TorrentIdentityFixture.V1Hash);
            ((TorrentInfo)book.Release).InfoHash.Should().Be(TorrentIdentityFixture.V1Hash);
            Mocker.GetMock<IQBittorrentProxy>().Verify(s => s.AddTorrentFromUrl(uri, It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public async Task should_reject_v2_only_file_before_client_submission()
        {
            await GivenTorrent(TorrentType.V2Only);
            var book = GivenRelease("http://127.0.0.1/owned.torrent");
            Func<Task> download = () => Subject.Download(book, CreateIndexer());
            await download.Should().ThrowAsync<ReleaseDownloadException>().WithMessage("*v2-only*");
            VerifyNoSubmission();
        }

        [Test]
        public async Task should_reject_v2_only_magnet_before_client_submission()
        {
            var book = GivenRelease("magnet:?xt=" + TorrentIdentityFixture.V2Topic);
            Func<Task> download = () => Subject.Download(book, CreateIndexer());
            await download.Should().ThrowAsync<ReleaseDownloadException>().WithMessage("*v2-only*");
            VerifyNoSubmission();
        }

        [Test]
        public async Task should_reject_v2_only_redirect_without_adding_anything()
        {
            var headers = new HttpHeader();
            headers["Location"] = "magnet:?xt=" + TorrentIdentityFixture.V2Topic;
            Mocker.GetMock<IHttpClient>().Setup(s => s.GetAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, headers, Array.Empty<byte>(), HttpStatusCode.SeeOther)));
            var book = GivenRelease("http://127.0.0.1/redirect");
            Func<Task> download = () => Subject.Download(book, CreateIndexer());
            await download.Should().ThrowAsync<ReleaseDownloadException>().WithMessage("*v2-only*");
            VerifyNoSubmission();
        }

        [Test]
        public async Task should_apply_grab_blocklist_to_hybrid_v1_hash_before_submission()
        {
            var uri = "magnet:?xt=" + TorrentIdentityFixture.V2Topic + "&xt=" + TorrentIdentityFixture.V1Topic;
            var book = GivenRelease(uri);
            var settings = new Mock<ITorrentIndexerSettings>();
            settings.SetupGet(s => s.RejectBlocklistedTorrentHashesWhileGrabbing).Returns(true);
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Settings = settings.Object });
            Mocker.GetMock<IBlocklistService>().Setup(s => s.BlocklistedTorrentHash(book.Author.Id, TorrentIdentityFixture.V1Hash)).Returns(true);
            Func<Task> download = () => Subject.Download(book, indexer.Object);
            await download.Should().ThrowAsync<ReleaseBlockedException>();
            VerifyNoSubmission();
        }
    }
}
