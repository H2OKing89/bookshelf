using System;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.Organizer
{
    public class AudiobookNamingFields
    {
        private const string NumberPattern = @"[0-9]+(?:\.[0-9]+)?(?:-[0-9]+(?:\.[0-9]+)?)?";
        private static readonly Regex PositionPattern = new Regex(@"\A" + NumberPattern + @"\z", RegexOptions.Compiled);
        private static readonly Regex PaddingPattern = new Regex(@"\A0+\z", RegexOptions.Compiled);

        public string Title { get; set; } = string.Empty;
        public string WorkTitle { get; set; } = string.Empty;

        public static AudiobookNamingFields From(Edition edition, AudiobookMetadataProjection projection)
        {
            var title = projection?.DisplayTitle ?? AudiobookTextNormalizer.Text(edition.Title) ?? string.Empty;
            var result = new AudiobookNamingFields { Title = title, WorkTitle = title };
            if (projection?.SubtitleProtected == true && !string.IsNullOrWhiteSpace(projection.Subtitle))
            {
                result.Title = title + ": " + projection.Subtitle;
                result.WorkTitle = result.Title;
            }

            var series = AudiobookTextNormalizer.Text(projection?.Series);
            var position = AudiobookTextNormalizer.Text(projection?.Position);
            if (projection?.TitleProtected == true || series == null || position == null || !PositionPattern.IsMatch(position))
            {
                var retainedSubtitle = AudiobookTextNormalizer.Text(projection?.Subtitle);
                if (retainedSubtitle != null && (projection.SubtitleProtected || (projection.TitleProtected &&
                    !Same(retainedSubtitle, title) && !HasCompleteSubtitle(title, retainedSubtitle))))
                {
                    result.Title = title + ": " + retainedSubtitle;
                    result.WorkTitle = result.Title;
                }

                return result;
            }

            if (!TryWorkTitle(title, series, position, out var workTitle))
            {
                return result;
            }

            var subtitle = AudiobookTextNormalizer.Text(projection.Subtitle);
            if (!projection.SubtitleProtected && Same(subtitle, AudiobookTextNormalizer.Text(edition.Title)))
            {
                subtitle = null;
            }

            var combinedTitle = (AudiobookTextNormalizer.Text(edition.Title) ?? title) + ": " + subtitle;
            if (!projection.SubtitleProtected && subtitle != null && TryWorkTitle(combinedTitle, series, position, out var combinedWork) && string.IsNullOrWhiteSpace(combinedWork))
            {
                subtitle = null;
            }

            if (subtitle != null)
            {
                var subtitleWork = subtitle;
                if (!projection.SubtitleProtected && !TryWorkTitle(subtitle, series, position, out subtitleWork))
                {
                    return result;
                }

                if (!string.IsNullOrWhiteSpace(subtitleWork) && (projection.SubtitleProtected ||
                    (!Same(subtitleWork, workTitle) && !Same(subtitleWork, title) && !HasCompleteSubtitle(workTitle, subtitleWork))))
                {
                    workTitle = string.IsNullOrWhiteSpace(workTitle) ? subtitleWork : workTitle + ": " + subtitleWork;
                }
            }

            result.Title = series + " vol_" + FormatPosition(position, "00") +
                (string.IsNullOrWhiteSpace(workTitle) ? string.Empty : " " + workTitle);
            result.WorkTitle = string.IsNullOrWhiteSpace(workTitle) ? title : workTitle;
            return result;
        }

        public static string FormatPosition(string position, string format)
        {
            if (position == null || format == null || !PaddingPattern.IsMatch(format) || !PositionPattern.IsMatch(position))
            {
                return position ?? string.Empty;
            }

            return string.Join("-", position.Split('-').Select(part =>
            {
                var pieces = part.Split('.');
                var whole = pieces[0].TrimStart('0');
                whole = whole.Length == 0 ? "0" : whole;
                var fraction = pieces.Length == 2 ? "." + pieces[1] : string.Empty;
                return (whole == "0" && fraction.Length != 0 ? whole : whole.PadLeft(format.Length, '0')) + fraction;
            }));
        }

        private static bool TryWorkTitle(string title, string series, string position, out string workTitle)
        {
            workTitle = title;
            var prefix = series.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? @"(?:The\s+)?" + Regex.Escape(series.Substring(4)) : Regex.Escape(series);
            var seriesOnly = @"\A" + prefix + @"\z";
            if (Regex.IsMatch(title, seriesOnly, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                workTitle = string.Empty;
                return true;
            }

            var pattern = @"\A" + prefix + @"(?:\s*[:,#]\s*|\s+)" +
                @"(?:(?:books?|vol(?:ume)?s?)\.?\s*)?(?<position>" + NumberPattern + @")" +
                @"(?:\s*[:–—-]\s*(?<work>.+))?\z";
            var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success)
            {
                if (!SamePosition(match.Groups["position"].Value, position))
                {
                    return false;
                }

                workTitle = AudiobookTextNormalizer.Text(match.Groups["work"].Value) ?? string.Empty;
                return true;
            }

            // A partial series prefix does not prove where the work title starts.
            return !Regex.IsMatch(title, @"\A" + prefix + @"(?=$|[\s,:#])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }

        private static bool SamePosition(string first, string second)
        {
            string Key(string value) => string.Join("-", value.Split('-').Select(part =>
            {
                var pieces = part.Split('.');
                var whole = pieces[0].TrimStart('0');
                var fraction = pieces.Length == 2 ? pieces[1].TrimEnd('0') : string.Empty;
                return (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? string.Empty : "." + fraction);
            }));

            return Key(first) == Key(second);
        }

        private static bool HasCompleteSubtitle(string title, string subtitle)
        {
            var pattern = @"\A.+(?::\s*|\s+[-–—]\s+)" + Regex.Escape(subtitle) + @"\z";
            return !string.IsNullOrWhiteSpace(title) && Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }

        private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }
}
