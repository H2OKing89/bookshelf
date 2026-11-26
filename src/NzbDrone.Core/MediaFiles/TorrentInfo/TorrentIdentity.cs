using System;
using MonoTorrent;

namespace NzbDrone.Core.MediaFiles.TorrentInfo
{
    // Download clients, blocklists and persisted download receipts currently
    // share a v1 (SHA-1) tracking key. Hybrid torrents own both hashes, but their
    // v1 identity remains the same as the existing queue/history key. Never
    // silently substitute or truncate a v2-only SHA-256 identity.
    public static class TorrentIdentity
    {
        public static string FromMagnet(string magnetUrl)
        {
            return TrackingHash(MagnetLink.Parse(magnetUrl).InfoHashes);
        }

        public static string FromTorrent(byte[] fileContents)
        {
            return TrackingHash(Torrent.Load(fileContents).InfoHashes);
        }

        private static string TrackingHash(InfoHashes hashes)
        {
            if (hashes.V1 != null)
            {
                return hashes.V1.ToHex();
            }

            if (hashes.V2 != null)
            {
                throw new TorrentIdentityNotSupportedException();
            }

            throw new FormatException("The torrent has no valid info hash.");
        }
    }

    public class TorrentIdentityNotSupportedException : NotSupportedException
    {
        public TorrentIdentityNotSupportedException()
            : base("This is a BitTorrent v2-only release. Bookshelf's download tracking requires a v1 or hybrid torrent with a v1 info hash.")
        {
        }
    }
}
