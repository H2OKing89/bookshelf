using System;
using System.Collections.Generic;
using System.Linq;
using MonoTorrent;

namespace NzbDrone.Core.MediaFiles.TorrentInfo
{
    // Content hashes are never client-local download handles. Retain both
    // versions for hybrid releases; v2-only uses the entire SHA-256 hash.
    public sealed class TorrentIdentity
    {
        public TorrentIdentity(string v1, string v2)
        {
            V1 = Normalize(v1, 40);
            V2 = Normalize(v2, 64);
            if (V1 == null && V2 == null)
            {
                throw new FormatException("The torrent has no valid info hash.");
            }
        }

        public string V1 { get; }
        public string V2 { get; }
        public string ContentHash => V1 ?? V2;
        public bool IsV2Only => V1 == null;
        public IEnumerable<string> Hashes => new[] { V1, V2 }.Where(hash => hash != null);

        public static string Normalize(string value, int length) => value?.Length == length && value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
        public static bool EqualsHash(string left, string right) => left != null && right != null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        public static TorrentIdentity ReadMagnet(string magnetUrl) => Read(MagnetLink.Parse(magnetUrl).InfoHashes);
        public static TorrentIdentity ReadTorrent(byte[] fileContents) => Read(Torrent.Load(fileContents).InfoHashes);
        public static string FromMagnet(string magnetUrl) => ReadMagnet(magnetUrl).ContentHash;
        public static string FromTorrent(byte[] fileContents) => ReadTorrent(fileContents).ContentHash;

        // A reported truncated v2 hash can be a valid native client handle but
        // cannot prove the full content identity.
        public bool IsNativeHandle(string value) => EqualsHash(V1, value) || EqualsHash(V2, value) ||
            (V2 != null && EqualsHash(V2[..40], value));

        public bool MatchesReportedHashes(string v1, string v2) =>
            (V1 == null || EqualsHash(V1, Normalize(v1, 40))) && (V2 == null || EqualsHash(V2, Normalize(v2, 64)));

        private static TorrentIdentity Read(InfoHashes hashes) => new TorrentIdentity(hashes.V1?.ToHex(), hashes.V2?.ToHex());
    }
}
