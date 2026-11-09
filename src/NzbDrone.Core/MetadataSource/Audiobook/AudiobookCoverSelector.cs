using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookCoverSelector
    {
        private static readonly HashSet<string> AmazonHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "m.media-amazon.com", "images-na.ssl-images-amazon.com", "images-eu.ssl-images-amazon.com", "images-fe.ssl-images-amazon.com"
        };

        private static readonly Regex AmazonImage = new Regex(
            @"\A/images/I/(?<asset>[a-zA-Z0-9+%\-]+)(?:\._SL(?<size>[1-9]\d*)_)?\.(?:jpe?g|png|webp)\z",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        public static string Largest(IEnumerable<KeyValuePair<int, string>> variants) => variants?
            .Where(variant => IsHttpUrl(variant.Value))
            .OrderByDescending(variant => variant.Key)
            .Select(variant => variant.Value)
            .FirstOrDefault();

        public static bool NeedsEnrichment(List<MediaCover.MediaCover> images)
        {
            var covers = images?.Where(image => image?.CoverType == MediaCover.MediaCoverTypes.Cover && IsHttpUrl(image.Url)).ToList();
            return covers?.Any() != true || covers.All(image =>
                TryAmazonImage(image.Url, out _, out var size) && size.HasValue && size <= 500);
        }

        // Keep supplied URLs intact. Only comparable variants of the same Amazon
        // asset can displace an existing image; an original has no resize suffix,
        // but its actual source dimensions remain unknown.
        public static List<MediaCover.MediaCover> PreferLarger(List<MediaCover.MediaCover> preferred, List<MediaCover.MediaCover> alternate)
        {
            if (preferred?.Any() != true)
            {
                return alternate ?? preferred ?? new List<MediaCover.MediaCover>();
            }

            if (alternate?.Any() != true)
            {
                return preferred;
            }

            return preferred.Select(image => alternate
                .Where(candidate => candidate.CoverType == image.CoverType && IsLarger(image.Url, candidate.Url))
                .Aggregate(image, (best, candidate) => IsLarger(best.Url, candidate.Url) ? candidate : best))
                .Concat(alternate.Where(candidate => preferred.All(image => image.CoverType != candidate.CoverType)))
                .ToList();
        }

        private static bool IsLarger(string current, string candidate)
        {
            if (!TryAmazonImage(current, out var asset, out var size) ||
                !TryAmazonImage(candidate, out var candidateAsset, out var candidateSize) ||
                !string.Equals(asset, candidateAsset, StringComparison.Ordinal))
            {
                return false;
            }

            return size.HasValue && (!candidateSize.HasValue || candidateSize > size);
        }

        private static bool TryAmazonImage(string value, out string asset, out int? size)
        {
            asset = null;
            size = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !AmazonHosts.Contains(uri.Host) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }

            var match = AmazonImage.Match(uri.AbsolutePath);
            if (!match.Success || (match.Groups["size"].Success && !int.TryParse(match.Groups["size"].Value, out _)))
            {
                return false;
            }

            asset = match.Groups["asset"].Value;
            size = match.Groups["size"].Success ? int.Parse(match.Groups["size"].Value) : null;
            return true;
        }

        private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https");
    }
}
