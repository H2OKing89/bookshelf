using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookSynopsis
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
        private static readonly Regex HiddenContent = new Regex(@"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>|<!--[\s\S]*?-->", RegexOptions.IgnoreCase | RegexOptions.Compiled, Timeout);
        private static readonly Regex Paragraphs = new Regex(@"<\s*/?\s*(?:p|div|h[1-6]|li|ul|ol|blockquote|br)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, Timeout);
        private static readonly Regex Tags = new Regex(@"<[^>]*>", RegexOptions.Compiled, Timeout);
        private static readonly Regex Commerce = new Regex(@"^(?:buy now|pre-?order now|order (?:your copy|now)|download (?:your copy|now)|get your copy|available (?:at|from) (?:audible|amazon)|visit (?:https?://|www\.)|https?://|www\.)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, Timeout);

        public static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var decoded = WebUtility.HtmlDecode(value);
            var text = Tags.Replace(Paragraphs.Replace(HiddenContent.Replace(decoded, string.Empty), "\n"), string.Empty);
            var paragraphs = text.Split('\n').Select(AudiobookTextNormalizer.Text)
                .Where(line => line != null && !Commerce.IsMatch(line));
            var result = string.Join("\n\n", paragraphs);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        public static string Select(string summary, string description) => Clean(summary) ?? Clean(description);
    }
}
