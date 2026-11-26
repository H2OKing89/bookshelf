using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NzbDrone.Core.Parser.Model
{
    public class TorrentInfo : ReleaseInfo
    {
        public string MagnetUrl { get; set; }
        public string InfoHash { get; set; }
        public string InfoHashV1 { get; set; }
        public string InfoHashV2 { get; set; }

        public IEnumerable<string> KnownHashes() => new[] { InfoHash, InfoHashV1, InfoHashV2 }
            .Where(hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase);
        public int? Seeders { get; set; }
        public int? Peers { get; set; }

        public static int? GetSeeders(ReleaseInfo release)
        {
            var torrentInfo = release as TorrentInfo;

            if (torrentInfo == null)
            {
                return null;
            }

            return torrentInfo.Seeders;
        }

        public static int? GetPeers(ReleaseInfo release)
        {
            var torrentInfo = release as TorrentInfo;

            if (torrentInfo == null)
            {
                return null;
            }

            return torrentInfo.Peers;
        }

        public override string ToString(string format)
        {
            var stringBuilder = new StringBuilder(base.ToString(format));
            switch (format.ToUpperInvariant())
            {
                case "L": // Long format
                    stringBuilder.AppendLine("MagnetUrl: " + MagnetUrl ?? "Empty");
                    stringBuilder.AppendLine("InfoHash: " + InfoHash ?? "Empty");
                    stringBuilder.AppendLine("Seeders: " + Seeders ?? "Empty");
                    stringBuilder.AppendLine("Peers: " + Peers ?? "Empty");
                    break;
            }

            return stringBuilder.ToString();
        }
    }
}
