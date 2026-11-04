using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookTextNormalizer
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex CommaSpacing = new Regex(@",\s*", RegexOptions.Compiled);
        public static string Text(string value) => string.IsNullOrWhiteSpace(value)
            ? null : Whitespace.Replace(value.Normalize(NormalizationForm.FormC).Trim(), " ");

        public static string Publisher(string value)
        {
            var text = Text(value);
            return text == null ? null : CommaSpacing.Replace(text, ", ").Trim();
        }

        public static string Language(string value) => value.CanonicalizeLanguage() ?? Text(value)?.ToLowerInvariant();

        public static List<string> Names(IEnumerable<string> values) => values?.Select(Text).Where(v => v != null)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

        public static string Position(string value) => TryPosition(value, out var number)
            ? number.ToString("0.###########################", CultureInfo.InvariantCulture) : Text(value);

        private static bool TryPosition(string value, out decimal number)
        {
            return decimal.TryParse(Text(value),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out number);
        }

        public static string DisplayTitle(Book book, string publishedTitle = null, Edition edition = null)
        {
            var title = Text(publishedTitle ?? book.Title);
            var primary = book.SeriesLinks?.Value?.FirstOrDefault(l => l.IsPrimary);
            var series = Text(primary?.Series?.Value?.Title);
            if (title == null || series == null ||
                !TryPosition(primary.Position, out var position) || position < 0)
            {
                return title;
            }

            var candidates = new List<string> { title };
            var subtitle = Text(edition != null ? edition.Subtitle : book.Editions?.Value?.SingleOrDefault(e => e.Monitored)?.Subtitle);
            if (subtitle != null)
            {
                candidates.Add(title + ": " + subtitle);
            }

            // Only reshape titles built from this recording's own series name and number.
            var prefix = series.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
                ? @"(?:The\s+)?" + Regex.Escape(series.Substring(4)) : Regex.Escape(series);
            var pattern = @"\A" + prefix + @"(?:\s+\(Light Novel\))?[,\s:]*(?:(?:vol(?:ume)?\.?|book)\s*)?(?<number>\d+(?:\.\d+)?)?(?:\s+\(Light Novel\))?(?<suffix>\s*[:–—-].+)?\z";
            foreach (var candidate in candidates)
            {
                var match = Regex.Match(candidate, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (!match.Success || (match.Groups["number"].Success &&
                    (!TryPosition(match.Groups["number"].Value, out var titlePosition) || titlePosition != position)))
                {
                    continue;
                }

                var suffix = Text(match.Groups["suffix"].Value)?.TrimStart(':', '-', '–', '—', ' ');
                if (string.Equals(suffix, "A LitRPG Adventure", StringComparison.OrdinalIgnoreCase))
                {
                    suffix = null;
                }

                return series + " #" + Position(primary.Position) + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : ": " + suffix);
            }

            return title;
        }
    }
}
