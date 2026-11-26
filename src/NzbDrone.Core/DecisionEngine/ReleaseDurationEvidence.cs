using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine
{
    internal static class ReleaseDurationEvidence
    {
        private static readonly Regex PartialRecording = new Regex(@"\b(?:part|disc|disk|cd|track)\s*[-_. ]*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        private static readonly decimal MaximumSeconds = (decimal)TimeSpan.MaxValue.TotalSeconds;

        public static decimal? GetSeconds(RemoteBook remoteBook)
        {
            var reportedDuration = ValidSeconds(remoteBook.Release?.AudioDurationSeconds);
            if (reportedDuration.HasValue)
            {
                return reportedDuration;
            }

            var title = remoteBook.Release?.Title;
            if (string.IsNullOrWhiteSpace(title) || PartialRecording.IsMatch(title) || remoteBook.Books?.Count is not > 0 ||
                remoteBook.Books.Any(book => book == null || book.Id <= 0))
            {
                return null;
            }

            var identities = AudioRecordingIdentifier.ReadBracketedAsins(title);
            var books = remoteBook.Books.DistinctBy(book => book.Id).ToList();
            if (identities.Count != books.Count || (books.Count == 1 && remoteBook.ParsedBookInfo?.Discography == true))
            {
                return null;
            }

            var matchedBooks = new HashSet<int>();
            var seconds = 0m;
            foreach (var identity in identities)
            {
                var matches = books.SelectMany(book => book.Editions?.Value ?? Enumerable.Empty<Edition>())
                    .Where(edition => edition != null && !edition.IsEbook && AudioRecordingIdentifier.MatchesAsin(identity, edition))
                    .Take(2).ToList();
                if (matches.Count != 1 || !books.Any(book => book.Id == matches[0].BookId) || !matchedBooks.Add(matches[0].BookId))
                {
                    return null;
                }

                var duration = GetEditionSeconds(matches[0]);
                if (!duration.HasValue)
                {
                    return null;
                }

                seconds += duration.Value;
                if (seconds > MaximumSeconds)
                {
                    return null;
                }
            }

            return seconds;
        }

        private static decimal? GetEditionSeconds(Edition edition)
        {
            var chapters = edition.ChapterMetadata;
            var chapterSeconds = chapters?.IsAccurate != false ? ValidSeconds(chapters?.RuntimeLengthMs / 1000.0) : null;
            var catalogSeconds = chapterSeconds ?? ValidSeconds(edition.RuntimeLengthMinutes * 60.0);
            var files = edition.BookFiles?.Value;
            if (files?.Count is not > 0 || files.Any(file => file == null || file.EditionId != edition.Id) || !HasDistinctLocalPaths(files) ||
                !files.Select(file => file.Part).Order().SequenceEqual(Enumerable.Range(1, files.Count)))
            {
                return catalogSeconds;
            }

            var measured = 0m;
            foreach (var file in files)
            {
                var duration = file.MediaInfo?.Source == "MediaInfo" && MediaInfoAnalysis.GetStatus(file.MediaInfo, file.Size, file.Modified) == "current"
                    ? ValidSeconds(file.MediaInfo.GetFileDurationSeconds())
                    : null;
                if (!duration.HasValue)
                {
                    return catalogSeconds;
                }

                measured += duration.Value;
                if (measured > MaximumSeconds)
                {
                    return catalogSeconds;
                }
            }

            // Persisted part numbers alone cannot establish recording completeness.
            // A complete in-memory inventory or corroborating catalog length can.
            return files.All(file => file.PartCount == files.Count) || (catalogSeconds.HasValue && Math.Abs(measured - catalogSeconds.Value) <= (chapterSeconds.HasValue ? 1m : 60m)) ? measured : catalogSeconds;
        }

        private static bool HasDistinctLocalPaths(List<BookFile> files)
        {
            try
            {
                return files.All(file => file.Path.IsPathValid(PathValidationType.CurrentOs) && Path.IsPathFullyQualified(file.Path)) &&
                    files.Select(file => file.Path).Distinct(PathEqualityComparer.Instance).Count() == files.Count;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                return false;
            }
        }

        private static decimal? ValidSeconds(double? value)
        {
            return value is >= 1 && double.IsFinite(value.Value) && value.Value <= (double)MaximumSeconds
                ? (decimal)value.Value
                : null;
        }
    }
}
