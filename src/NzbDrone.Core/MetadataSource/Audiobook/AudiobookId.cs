using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookId
    {
        private static readonly Regex AsinPattern = new Regex(@"\A(?:B[A-Z0-9]{9}|[0-9]{9}[0-9X])\z", RegexOptions.Compiled);

        public static bool IsAsin(string value) => value != null && AsinPattern.IsMatch(value);
        public static bool IsNative(string value) => value?.StartsWith("audible:", StringComparison.Ordinal) == true;
        public static string ForAsin(string region, string asin) => $"audible:{region}:{asin}";
        public static string ForName(string region, string name) => $"audible:{region}:name:{Uri.EscapeDataString(name.Trim())}";

        // A bare ASIN does not contain a marketplace. Try the configured preference
        // first, retaining the marketplace returned by a successful exact lookup.
        public static IEnumerable<string> LookupRegions(string preferredRegion) => new[] { preferredRegion ?? "us", "us", "uk" }
            .Concat(AudnexusProxy.Regions.OrderBy(region => region, StringComparer.Ordinal))
            .Where(AudnexusProxy.Regions.Contains)
            .Distinct(StringComparer.Ordinal);

        public static (string Region, string Value, bool IsName) Parse(string id)
        {
            var parts = id?.Split(':', 4);
            if (parts?.Length >= 3 && parts[0] == "audible" && AudnexusProxy.Regions.Contains(parts[1]))
            {
                if (parts.Length == 3 && IsAsin(parts[2]))
                {
                    return (parts[1], parts[2], false);
                }

                if (parts.Length == 4 && parts[2] == "name" && !string.IsNullOrWhiteSpace(parts[3]))
                {
                    return (parts[1], Uri.UnescapeDataString(parts[3]), true);
                }
            }

            throw new BookInfoException("This is not an audiobook catalog ID. Search by title, author, or Audible ASIN to add an audiobook.");
        }
    }
}
