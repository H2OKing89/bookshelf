using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using MonoTorrent;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.Test.MediaFiles.TorrentInfo
{
    [TestFixture]
    public class TorrentIdentityFixture
    {
        public const string V1Hash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951";
        public const string V2Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        public const string V2Topic = "urn:btmh:1220" + V2Hash;
        public const string V1Topic = "urn:btih:" + V1Hash;

        internal static async Task<byte[]> CreateOwnedTorrent(TorrentType type)
        {
            var folder = Directory.CreateTempSubdirectory("bookshelf-torrent-identity-");
            try
            {
                var file = Path.Combine(folder.FullName, "synthetic-recording.m4b");
                await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3, 4, 5 });
                var metadata = await new TorrentCreator(type).CreateAsync(new TorrentFileSource(file));
                return metadata.Encode();
            }
            finally
            {
                folder.Delete(true);
            }
        }

        [TestCase(TorrentType.V1Only)]
        [TestCase(TorrentType.V1V2Hybrid)]
        public async Task should_use_exact_v1_hash_from_owned_metainfo(TorrentType type)
        {
            var bytes = await CreateOwnedTorrent(type);
            var torrent = Torrent.Load(bytes);
            torrent.InfoHashes.V1.Should().NotBeNull();
            var raw = (MonoTorrent.BEncoding.BEncodedDictionary)MonoTorrent.BEncoding.BEncodedValue.Decode(bytes);
            var expected = Convert.ToHexString(SHA1.HashData(raw["info"].Encode()));
            TorrentIdentity.FromTorrent(bytes).Should().Be(expected);
            if (type == TorrentType.V1V2Hybrid)
            {
                torrent.InfoHashes.V2.Should().NotBeNull();
                torrent.InfoHashes.V2.ToHex().Length.Should().Be(64);
                var magnet = "magnet:?xt=urn:btmh:1220" + torrent.InfoHashes.V2.ToHex() + "&xt=urn:btih:" + expected;
                TorrentIdentity.FromMagnet(magnet).Should().Be(expected);
            }
        }

        [Test]
        public async Task should_not_substitute_a_v2_only_torrent_hash()
        {
            var bytes = await CreateOwnedTorrent(TorrentType.V2Only);
            var hashes = Torrent.Load(bytes).InfoHashes;
            hashes.V1.Should().BeNull();
            hashes.V2.ToHex().Length.Should().Be(64);
            Assert.Throws<TorrentIdentityNotSupportedException>(() => TorrentIdentity.FromTorrent(bytes));
        }

        [TestCase("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp")]
        [TestCase("magnet:?xt=" + V1Topic)]
        [TestCase("magnet:?xt=" + V1Topic + "&xt=" + V2Topic)]
        [TestCase("magnet:?xt=" + V2Topic + "&xt=" + V1Topic)]
        public void should_keep_v1_tracking_for_both_magnet_encodings_and_hybrid_topic_order(string uri)
        {
            TorrentIdentity.FromMagnet(uri).Should().Be(V1Hash);
            new Parser().Hash(uri).Should().Be(V1Hash);
        }

        [Test]
        public void should_not_advertise_a_v2_only_hash_as_v1_in_rss()
        {
            var uri = "magnet:?xt=" + V2Topic;
            Assert.Throws<TorrentIdentityNotSupportedException>(() => TorrentIdentity.FromMagnet(uri));
            new Parser().Hash(uri).Should().BeNull();
        }

        private class Parser : TorrentRssParser
        {
            protected override string GetDownloadUrl(XElement item)
            {
                return (string)item.Element("link");
            }

            public string Hash(string uri)
            {
                return GetInfoHash(new XElement("item", new XElement("link", uri)));
            }
        }
    }
}
