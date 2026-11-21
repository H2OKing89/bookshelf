using System;
using System.Linq;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine
{
    public static class ReleaseSizePolicy
    {
        private const decimal BytesPerMebibyte = 1024 * 1024;
        private const string AudioUnit = "MiB/hour";
        private const string EbookUnit = "MiB";

        public static string GetSizeUnit(Quality quality)
        {
            if (quality == Quality.MP3 || quality == Quality.FLAC || quality == Quality.M4B)
            {
                return AudioUnit;
            }

            return quality == Quality.PDF || quality == Quality.EPUB || quality == Quality.MOBI || quality == Quality.AZW3
                ? EbookUnit
                : null;
        }

        public static QualityProfileQualityItem GetRules(RemoteBook remoteBook)
        {
            return GetRules(remoteBook?.ParsedBookInfo?.Quality?.Quality, remoteBook?.Author?.QualityProfile?.Value);
        }

        public static decimal? GetPreferredDeviation(RemoteBook remoteBook)
        {
            var preferred = GetRules(remoteBook)?.PreferredSize;
            if (preferred is not > 0 || GetSize(remoteBook, GetSizeUnit(remoteBook.ParsedBookInfo.Quality.Quality)) is not { } size)
            {
                return null;
            }

            var deviation = (double)Math.Abs(size - preferred.Value) / (double)preferred.Value;
            return deviation >= (double)decimal.MaxValue ? decimal.MaxValue : (decimal)deviation;
        }

        public static Decision Evaluate(RemoteBook remoteBook)
        {
            return Evaluate(remoteBook, remoteBook?.Author?.QualityProfile?.Value);
        }

        public static Decision Evaluate(RemoteBook remoteBook, QualityProfile profile)
        {
            var quality = remoteBook?.ParsedBookInfo?.Quality?.Quality;
            var rules = GetRules(quality, profile);
            if (rules == null || (rules.MinSize is not > 0 && rules.MaxSize is not > 0))
            {
                return Decision.Accept();
            }

            var unit = GetSizeUnit(quality);
            if (GetSize(remoteBook, unit) is not { } size)
            {
                return Decision.Accept();
            }

            if (rules.MinSize > 0 && size < rules.MinSize)
            {
                return Decision.RejectCode(RejectionCode.BelowMinimumSize, "Release size {0:0.####} {1} is below the profile minimum of {2} {1}", size, unit, rules.MinSize);
            }

            if (rules.MaxSize > 0 && size > rules.MaxSize)
            {
                return Decision.RejectCode(RejectionCode.AboveMaximumSize, "Release size {0:0.####} {1} is above the profile maximum of {2} {1}", size, unit, rules.MaxSize);
            }

            return Decision.Accept();
        }

        private static QualityProfileQualityItem GetRules(Quality quality, QualityProfile profile)
        {
            if (GetSizeUnit(quality) == null || profile?.Items == null)
            {
                return null;
            }

            return profile.Items.Where(item => item != null)
                .SelectMany(item => item.Quality == null ? item.Items ?? Enumerable.Empty<QualityProfileQualityItem>() : new[] { item })
                .FirstOrDefault(item => item?.Quality == quality);
        }

        private static decimal? GetSize(RemoteBook remoteBook, string unit)
        {
            var size = remoteBook.Release?.Size;
            if (size is not > 0)
            {
                return null;
            }

            if (unit == EbookUnit)
            {
                return size.Value / BytesPerMebibyte;
            }

            var seconds = ReleaseDurationEvidence.GetSeconds(remoteBook);
            return seconds.HasValue ? (decimal)size.Value * 3600 / (BytesPerMebibyte * seconds.Value) : null;
        }
    }
}
