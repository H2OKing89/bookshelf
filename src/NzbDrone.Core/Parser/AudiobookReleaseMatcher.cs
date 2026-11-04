using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Parser
{
    public static class AudiobookReleaseMatcher
    {
        private static readonly Regex FormatLabel = new Regex(@"\s*\((?:light novel|unabridged|audiobook)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolumeLabel = new Regex(@"\b(?:vol(?:ume)?|book)\.?\s*(?=\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolumeNumber = new Regex(@"\b(?:vol(?:ume)?|book)\.?\s*(?<number>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<Book> Match(ReleaseInfo release, List<Book> candidates, out string rejection, Func<string, string> narratorIdentity = null)
        {
            rejection = "Unable to match structured audiobook metadata";
            if (release.BookSeries.Any(series => !string.IsNullOrWhiteSpace(series.Position) && Position(series.Position) == null))
            {
                rejection = "Audiobook pack or unsupported series position; one recording could not be identified";
                return new List<Book>();
            }

            var title = Key(release.Book);
            var titleMatches = candidates.Where(book => Titles(book).Any(value => Key(value) == title)).ToList();
            var seriesMatches = candidates.Where(book => (book.SeriesLinks?.Value ?? new List<SeriesBookLink>()).Any(link =>
                !string.IsNullOrWhiteSpace(link.Position) && release.BookSeries.Any(series =>
                    Key(series.Name) == Key(link.Series.Value.Title) &&
                    Position(series.Position) != null && Position(series.Position) == Position(link.Position)))).ToList();

            var matches = titleMatches.Any() && seriesMatches.Any()
                ? titleMatches.Intersect(seriesMatches).ToList()
                : seriesMatches.Any() ? seriesMatches : titleMatches;
            if (titleMatches.Any() && seriesMatches.Any() && !matches.Any())
            {
                rejection = "Audiobook title conflicts with the indexed series position";
                return new List<Book>();
            }

            // Explicit volume numbers and structured positions must agree. Search
            // context alone cannot turn a companion book into the requested volume.
            var explicitVolumes = VolumeNumber.Matches(release.Book).Select(match => Position(match.Groups["number"].Value)).Distinct().ToList();
            matches = matches.Where(book => !(book.SeriesLinks?.Value ?? new List<SeriesBookLink>()).Any(link =>
                Position(link.Position) != null && release.BookSeries.Any(series => Key(series.Name) == Key(link.Series.Value.Title) &&
                    Position(series.Position) != null && Position(series.Position) != Position(link.Position)) &&
                !release.BookSeries.Any(series => Key(series.Name) == Key(link.Series.Value.Title) && Position(series.Position) == Position(link.Position))))
                .Where(book => !explicitVolumes.Any() || (book.SeriesLinks?.Value ?? new List<SeriesBookLink>()).Any(link =>
                    explicitVolumes.Count == 1 && explicitVolumes[0] == Position(link.Position))).ToList();

            if (matches.Any() && release.Narrators.Any())
            {
                string NarratorKey(string name) => Key(narratorIdentity?.Invoke(name) ?? name);
                var narrators = release.Narrators.Select(NarratorKey).ToHashSet();
                matches = matches.Where(book => !(book.Editions?.Value ?? new List<Edition>()).SelectMany(edition => edition.Narrators).Any() ||
                    book.Editions.Value.SelectMany(edition => edition.Narrators).Any(narrator => narrators.Contains(NarratorKey(narrator)))).ToList();
                if (!matches.Any())
                {
                    rejection = "Narrator does not match the catalog recording";
                    return new List<Book>();
                }
            }

            var abridged = Regex.IsMatch(release.Book, @"\babridged\b", RegexOptions.IgnoreCase);
            var unabridged = Regex.IsMatch(release.Book, @"\bunabridged\b", RegexOptions.IgnoreCase);
            matches = matches.Where(book => !(book.Editions?.Value ?? new List<Edition>()).Any(edition =>
                (abridged && string.Equals(edition.Disambiguation, "unabridged", StringComparison.OrdinalIgnoreCase)) ||
                (unabridged && string.Equals(edition.Disambiguation, "abridged", StringComparison.OrdinalIgnoreCase)))).ToList();

            if (matches.Count == 1)
            {
                return matches;
            }

            if (matches.Count > 1)
            {
                rejection = "Ambiguous audiobook recording; title, series and narrator do not identify one recording";
            }

            return new List<Book>();
        }

        private static IEnumerable<string> Titles(Book book)
        {
            yield return book.Title;
            foreach (var edition in book.Editions?.Value ?? new List<Edition>())
            {
                yield return edition.Title;
                if (!string.IsNullOrWhiteSpace(edition.Subtitle))
                {
                    yield return edition.Title + ": " + edition.Subtitle;
                }
            }

            yield return AudiobookTextNormalizer.DisplayTitle(book);
        }

        private static string Position(string value)
        {
            return decimal.TryParse(value?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                ? number.ToString("0.############################", CultureInfo.InvariantCulture) : null;
        }

        private static string Key(string value)
        {
            var text = AudiobookTextNormalizer.Text(value) ?? string.Empty;
            text = Regex.Replace(text, @"\AThe\s+", string.Empty, RegexOptions.IgnoreCase);
            return VolumeLabel.Replace(FormatLabel.Replace(text, string.Empty), string.Empty).CleanAuthorName();
        }
    }
}
