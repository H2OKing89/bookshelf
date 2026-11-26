using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookMetadataCleanup
    {
        private const string NumberPattern = @"[0-9]+(?:\.[0-9]+)?(?:-[0-9]+(?:\.[0-9]+)?)?";
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
        private static readonly Regex TrailingParenthesis = new Regex(@"\s*\((?<citation>[^()]*)\)\s*$", RegexOptions.Compiled);
        private static readonly Regex PositionPattern = new Regex(@"\A" + NumberPattern + @"\z", RegexOptions.Compiled);
        private static readonly Regex VolumePattern = new Regex(@"\A(?:books?|vol(?:ume)?s?)\.?\s*(?<position>" + NumberPattern + @")\z", Options | RegexOptions.Compiled);

        public static AudiobookMetadataProjection Project(
            Book book,
            Edition edition,
            IEnumerable<AudiobookMetadataCleanupRule> rules,
            string correctedTitle = null,
            bool protectTitle = false,
            bool protectSubtitle = false,
            DateTime? today = null)
        {
            book ??= edition?.Book?.Value ?? new Book();
            edition ??= book.Editions?.Value?.SingleOrDefault(item => item.Monitored);
            var originalTitle = edition?.Title ?? book.Title ?? string.Empty;
            var originalSubtitle = edition?.Subtitle ?? string.Empty;
            var primary = book.SeriesLinks?.Value?.OrderByDescending(link => link.IsPrimary).ThenBy(link => link.SeriesPosition).FirstOrDefault();
            var series = Text(primary?.Series?.Value?.Title);
            var projection = new AudiobookMetadataProjection
            {
                Title = protectTitle ? originalTitle : correctedTitle ?? Text(originalTitle),
                Subtitle = protectSubtitle ? originalSubtitle : Text(originalSubtitle),
                Series = series, Position = Text(primary?.Position),
                TitleProtected = protectTitle, SubtitleProtected = protectSubtitle
            };
            projection.DisplayTitle = projection.Title;
            var releaseDate = CalendarDate.Normalize(book.ReleaseDate ?? edition?.ReleaseDate);
            if (releaseDate.HasValue && releaseDate.Value > CalendarDate.Normalize(today ?? DateTime.UtcNow).AddYears(10))
            {
                projection.Warnings.Add($"The provider release date ({releaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) is more than 10 years away. Verify it with the publisher before relying on it.");
            }

            if (correctedTitle != null && !string.Equals(correctedTitle, originalTitle, StringComparison.Ordinal))
            {
                AddChange(projection, "title", originalTitle, originalTitle, null, "Verified title correction");
            }

            var active = (rules ?? Enumerable.Empty<AudiobookMetadataCleanupRule>()).Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.Phrase))
                .OrderByDescending(rule => rule.Phrase.Length).ToList();
            projection.Series = CleanField(series, "series", false, active, projection, true);
            projection.Series = CleanCorroboratedSeriesSuffix(projection.Series, series, originalTitle, originalSubtitle, active, projection);
            var seriesNames = new[] { series, projection.Series }.Where(value => value.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // A position disagreement is identity evidence, not text to clean away.
            if (ConflictingPosition(projection.Title, seriesNames, projection.Position) ||
                ConflictingPosition(projection.Subtitle, seriesNames, projection.Position))
            {
                projection.Warnings.Add("The title or subtitle conflicts with the catalog series position; title fields were retained.");
                projection.MetadataLabels = AudiobookTextNormalizer.Names(projection.Changes.SelectMany(change => change.Labels));
                return projection;
            }

            if (!protectTitle && correctedTitle == null)
            {
                projection.Title = CleanCitation(projection.Title, "title", false, seriesNames, projection.Position, projection);
                projection.Title = CleanField(projection.Title, "title", false, active, projection, true);
            }

            if (!protectSubtitle)
            {
                projection.Subtitle = CleanCitation(projection.Subtitle, "subtitle", true, seriesNames, projection.Position, projection);
                projection.Subtitle = CleanFormatVolume(projection.Subtitle, active, projection);
                projection.Subtitle = CleanField(projection.Subtitle, "subtitle", true, active, projection, true);
                if (Same(projection.Subtitle, originalTitle) || Same(projection.Subtitle, projection.Title) || CompleteSubtitle(projection.Title, projection.Subtitle))
                {
                    RemoveSubtitle(projection, "Duplicate title");
                }
            }

            if (ConflictingPosition(projection.Title, seriesNames, projection.Position) ||
                ConflictingPosition(projection.Subtitle, seriesNames, projection.Position))
            {
                projection.Title = protectTitle ? originalTitle : correctedTitle ?? Text(originalTitle);
                projection.Subtitle = protectSubtitle ? originalSubtitle : Text(originalSubtitle);
                projection.DisplayTitle = projection.Title;
                projection.Changes.RemoveAll(change => change.Field != "series" && change.Reason != "Verified title correction");
                projection.MetadataLabels = AudiobookTextNormalizer.Names(projection.Changes.SelectMany(change => change.Labels));
                projection.Warnings.Add("The title or subtitle conflicts with the catalog series position; title fields were retained.");
                return projection;
            }

            if (!protectTitle)
            {
                projection.DisplayTitle = FormatDisplayTitle(projection, out var consumedSubtitle);
                if (consumedSubtitle && !protectSubtitle)
                {
                    RemoveSubtitle(projection, "Repeated primary series and position");
                }
            }
            else
            {
                projection.DisplayTitle = projection.Title;
            }

            projection.MetadataLabels = AudiobookTextNormalizer.Names(projection.Changes.SelectMany(change => change.Labels));
            projection.Warnings = projection.Warnings.Distinct().ToList();
            return projection;
        }

        private static string CleanField(
            string value,
            string field,
            bool allowEmpty,
            List<AudiobookMetadataCleanupRule> rules,
            AudiobookMetadataProjection projection,
            bool delimited)
        {
            var original = value;
            var changes = new List<AudiobookMetadataCleanupChange>();
            foreach (var rule in rules)
            {
                var phrase = Text(rule.Phrase);
                if (value.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                void Record(string removed)
                {
                    changes.Add(Change(field, original, removed, rule, "Recognized format or promotional label"));
                }

                if (Same(value, phrase))
                {
                    Record(value);
                    value = string.Empty;
                    break;
                }

                string RemoveParenthetical(Match match)
                {
                    Record(match.Value);
                    return " ";
                }

                value = Regex.Replace(value, @"\(\s*" + Regex.Escape(phrase) + @"\s*\)", RemoveParenthetical, Options, Timeout);

                if (delimited)
                {
                    var suffix = @"(?<delimiter>:\s*|\s+[-–—]\s+)" + Regex.Escape(phrase) + @"(?=$|:\s*|\s+[-–—]\s+)";
                    string RemoveDelimited(Match match)
                    {
                        Record(phrase);
                        return string.Empty;
                    }

                    value = Regex.Replace(value, suffix, RemoveDelimited, Options, Timeout);
                    value = Regex.Replace(value, @"\A" + Regex.Escape(phrase) + @"(?:\s*:\s*|\s+[-–—]\s+)", RemoveDelimited, Options, Timeout);
                }

                if (changes.Any())
                {
                    value = Tidy(value);
                }
            }

            if (!allowEmpty && value.Length == 0 && original.Length != 0)
            {
                projection.Warnings.Add("Cleanup would empty the " + field + "; the original was retained.");
                return original;
            }

            projection.Changes.AddRange(changes);
            return value;
        }

        private static string CleanCorroboratedSeriesSuffix(
            string value,
            string original,
            string title,
            string subtitle,
            List<AudiobookMetadataCleanupRule> rules,
            AudiobookMetadataProjection projection)
        {
            // Do not reinterpret a complete label as a shorter label plus an article.
            if (rules.Any(rule => Same(value, rule.Phrase)))
            {
                return value;
            }

            foreach (var rule in rules)
            {
                var phrase = Text(rule.Phrase);
                if (!value.EndsWith(" " + phrase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!HasCompleteLabel(title, phrase) && !HasCompleteLabel(subtitle, phrase))
                {
                    continue;
                }

                var match = Regex.Match(value, @"\A(?<series>.+)\s+" + Regex.Escape(phrase) + @"\z", Options, Timeout);
                var retained = match.Success ? Text(match.Groups["series"].Value) : string.Empty;
                if (retained.Length == 0)
                {
                    continue;
                }

                AddChange(projection, "series", original, phrase, rule, "Series suffix corroborated by a complete edition label");
                return retained;
            }

            return value;
        }

        private static bool HasCompleteLabel(string value, string phrase)
        {
            value = Text(value);
            if (Same(value, phrase))
            {
                return true;
            }

            var escaped = Regex.Escape(phrase);
            return Regex.IsMatch(value, @"\(\s*" + escaped + @"\s*\)", Options, Timeout) ||
                Regex.IsMatch(value, @"(?:\A|:\s*|\s+[-–—]\s+)" + escaped + @"(?=$|:\s*|\s+[-–—]\s+)", Options, Timeout);
        }

        private static string CleanCitation(
            string value,
            string field,
            bool allowEmpty,
            List<string> seriesNames,
            string position,
            AudiobookMetadataProjection projection)
        {
            var original = value;
            var changes = new List<string>();
            if (IsCitation(value, seriesNames, position))
            {
                changes.Add(value);
                value = string.Empty;
            }
            else
            {
                while (value.Length != 0)
                {
                    var match = TrailingParenthesis.Match(value);
                    if (!match.Success)
                    {
                        break;
                    }

                    var citation = Text(match.Groups["citation"].Value);
                    if (!IsCitation(citation, seriesNames, position))
                    {
                        if (Regex.IsMatch(citation, @"\b(?:books?|vol(?:ume)?s?)\.?\s*" + NumberPattern, Options, Timeout))
                        {
                            projection.Warnings.Add("An unconfirmed catalog citation was retained in the " + field + ".");
                        }

                        break;
                    }

                    changes.Add(match.Value.Trim());
                    value = Tidy(value.Substring(0, match.Index));
                }
            }

            if (!allowEmpty && value.Length == 0)
            {
                return original;
            }

            foreach (var removed in changes)
            {
                AddChange(projection, field, original, removed, null, "Repeated primary series and position");
            }

            return value;
        }

        private static string CleanFormatVolume(string value, List<AudiobookMetadataCleanupRule> rules, AudiobookMetadataProjection projection)
        {
            if (!PositionPattern.IsMatch(projection.Position))
            {
                return value;
            }

            foreach (var rule in rules)
            {
                var match = Regex.Match(value, @"\A" + Regex.Escape(rule.Phrase) + @"\s*[,;:]?\s*(?<volume>(?:books?|vol(?:ume)?s?)\.?\s*(?<position>" + NumberPattern + @"))\z", Options, Timeout);
                if (match.Success && SamePosition(match.Groups["position"].Value, projection.Position))
                {
                    AddChange(projection, "subtitle", value, rule.Phrase, rule, "Recognized format label");
                    AddChange(projection, "subtitle", value, match.Groups["volume"].Value, null, "Repeated catalog position");
                    return string.Empty;
                }
            }

            return value;
        }

        private static bool IsCitation(string value, List<string> seriesNames, string position)
        {
            if (value.Length == 0)
            {
                return false;
            }

            if (seriesNames.Any(series => Same(series, value)))
            {
                return true;
            }

            if (!PositionPattern.IsMatch(position))
            {
                return false;
            }

            var volume = VolumePattern.Match(value);
            if (volume.Success)
            {
                return SamePosition(volume.Groups["position"].Value, position);
            }

            return seriesNames.Any(series =>
            {
                var match = Regex.Match(value, @"\A" + SeriesPrefix(series) + @"(?:\s*,\s*|\s+)(?:books?|vol(?:ume)?s?)\.?\s*(?<position>" + NumberPattern + @")\z", Options, Timeout);
                return match.Success && SamePosition(match.Groups["position"].Value, position);
            });
        }

        private static bool ConflictingPosition(string value, List<string> seriesNames, string position)
        {
            if (!PositionPattern.IsMatch(position))
            {
                return false;
            }

            return seriesNames.Any(series =>
            {
                var pattern = @"\A" + SeriesPrefix(series) + @"(?:\s*[,#:]+\s*|\s+)(?:(?:books?|vol(?:ume)?s?)\.?\s*)?(?<position>" + NumberPattern + @")(?=$|\s|[:,])";
                var match = Regex.Match(value, pattern, Options, Timeout);
                return match.Success && !SamePosition(match.Groups["position"].Value, position);
            });
        }

        private static string FormatDisplayTitle(AudiobookMetadataProjection projection, out bool consumedSubtitle)
        {
            consumedSubtitle = false;
            if (projection.Series.Length == 0 || !PositionPattern.IsMatch(projection.Position))
            {
                return projection.Title;
            }

            var candidates = new[] { projection.Title, projection.Title + ": " + projection.Subtitle };
            for (var index = 0; index < candidates.Length; index++)
            {
                if (index == 1 && (projection.Subtitle.Length == 0 || projection.SubtitleProtected))
                {
                    continue;
                }

                var pattern = @"\A" + SeriesPrefix(projection.Series) + @"[,\s:]*(?:(?:books?|vol(?:ume)?s?)\.?\s*)?(?<position>" + NumberPattern + @")?(?<suffix>\s*[:–—-].+)?\z";
                var match = Regex.Match(candidates[index], pattern, Options, Timeout);
                if (!match.Success || (match.Groups["position"].Success && !SamePosition(match.Groups["position"].Value, projection.Position)))
                {
                    continue;
                }

                var suffix = Text(match.Groups["suffix"].Value).TrimStart(':', '-', '–', '—', ' ');
                if (!match.Groups["position"].Success && suffix.Length != 0)
                {
                    continue;
                }

                consumedSubtitle = index == 1;
                return projection.Series + " #" + NormalizedPosition(projection.Position) + (suffix.Length == 0 ? string.Empty : ": " + suffix);
            }

            return projection.Title;
        }

        private static string SeriesPrefix(string series) => series.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? @"(?:The\s+)?" + Regex.Escape(series.Substring(4)) : Regex.Escape(series);

        private static string NormalizedPosition(string value) => string.Join("-", value.Split('-').Select(part =>
        {
            var pieces = part.Split('.');
            var whole = pieces[0].TrimStart('0');
            var fraction = pieces.Length == 2 ? pieces[1].TrimEnd('0') : string.Empty;
            return (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? string.Empty : "." + fraction);
        }));

        private static bool SamePosition(string first, string second) => NormalizedPosition(first) == NormalizedPosition(second);
        private static string Text(string value) => AudiobookTextNormalizer.Text(value) ?? string.Empty;
        private static bool Same(string first, string second) => string.Equals(Text(first), Text(second), StringComparison.OrdinalIgnoreCase);
        private static string Tidy(string value) => Text(value).Trim(' ', ':', ',', ';', '-', '–', '—');

        private static bool CompleteSubtitle(string title, string subtitle) => subtitle.Length != 0 &&
            Regex.IsMatch(title, @"\A.+(?::\s*|\s+[-–—]\s+)" + Regex.Escape(subtitle) + @"\z", Options, Timeout);

        private static AudiobookMetadataCleanupChange Change(string field, string original, string removed, AudiobookMetadataCleanupRule rule, string reason) => new AudiobookMetadataCleanupChange
        {
            Field = field, OriginalText = original, RemovedText = removed, RuleId = rule?.Id,
            Labels = AudiobookTextNormalizer.Names(rule?.Labels), Reason = reason
        };

        private static void AddChange(AudiobookMetadataProjection projection, string field, string original, string removed, AudiobookMetadataCleanupRule rule, string reason)
        {
            projection.Changes.Add(Change(field, original, removed, rule, reason));
        }

        private static void RemoveSubtitle(AudiobookMetadataProjection projection, string reason)
        {
            if (projection.Subtitle.Length != 0)
            {
                AddChange(projection, "subtitle", projection.Subtitle, projection.Subtitle, null, reason);
                projection.Subtitle = string.Empty;
            }
        }
    }
}
