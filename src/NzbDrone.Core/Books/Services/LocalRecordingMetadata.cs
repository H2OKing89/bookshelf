using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Books
{
    internal static class LocalRecordingMetadata
    {
        public static void Normalize(LocalRecording recording)
        {
            if (recording == null || string.IsNullOrWhiteSpace(recording.Title) || recording.Title.Length > 1000 ||
                recording.Year < 1 || recording.Year > 9999 || recording.Subtitle?.Length > 1000 ||
                recording.Publisher?.Length > 1000 || recording.Language?.Length > 100 ||
                recording.Narrators?.Count > 100 || recording.Narrators?.Any(name => name?.Length > 500) == true)
            {
                throw Invalid("Enter a title and check that the metadata fields and year are valid.");
            }

            recording.Title = recording.Title.Trim();
            recording.Subtitle = Clean(recording.Subtitle);
            recording.Language = Clean(recording.Language);
            recording.Publisher = Clean(recording.Publisher);
            recording.Narrators = (recording.Narrators ?? new List<string>()).Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim()).Distinct().ToList();

            if (recording.ChapterMetadata != null)
            {
                var metadata = recording.ChapterMetadata;
                if (metadata.Chapters == null || metadata.Chapters.Count > 10000 || metadata.RuntimeLengthMs < 0 ||
                    metadata.BrandIntroDurationMs < 0 || metadata.BrandOutroDurationMs < 0)
                {
                    throw Invalid("Check that the chapter metadata is valid.");
                }

                long previousEnd = 0;
                var previousOrdinal = -1;
                for (var index = 0; index < metadata.Chapters.Count; index++)
                {
                    var chapter = metadata.Chapters[index];
                    if (chapter == null || chapter.Ordinal <= previousOrdinal || chapter.Title?.Length > 1000 ||
                        chapter.StartOffsetMs < previousEnd || chapter.LengthMs < 0 ||
                        chapter.StartOffsetMs > long.MaxValue - chapter.LengthMs)
                    {
                        throw Invalid("Chapters must have increasing nonnegative ordinals and nonnegative, non-overlapping times.");
                    }

                    previousEnd = chapter.StartOffsetMs + chapter.LengthMs;
                    previousOrdinal = chapter.Ordinal;
                }
            }
        }

        public static DateTime? ReleaseDate(int? year) => year.HasValue
            ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : null;

        public static Dictionary<string, string> Sources(LocalRecording recording, string source)
        {
            return new Dictionary<string, string>
            {
                ["title"] = Present(recording.Title, source),
                ["subtitle"] = Present(recording.Subtitle, source),
                ["narrators"] = recording.Narrators?.Count > 0 ? source : "unknown",
                ["publisher"] = Present(recording.Publisher, source),
                ["language"] = Present(recording.Language, source),
                ["year"] = recording.Year.HasValue ? source : "unknown",
                ["chapters"] = recording.ChapterMetadata?.Chapters?.Count > 0 ? source : "unknown"
            };
        }

        public static Dictionary<string, string> EditSources(LocalRecording previous, LocalRecording next)
        {
            var result = Sources(previous, "legacy");
            foreach (var field in previous.Provenance ?? new Dictionary<string, string>())
            {
                result[field.Key] = field.Value;
            }

            // An explicit clear is a manual correction as well as a non-empty change.
            if (Clean(previous.Title) != next.Title)
            {
                result["title"] = "manual";
            }

            if (Clean(previous.Subtitle) != next.Subtitle)
            {
                result["subtitle"] = "manual";
            }

            if (!(previous.Narrators ?? new List<string>()).SequenceEqual(next.Narrators))
            {
                result["narrators"] = "manual";
            }

            if (Clean(previous.Publisher) != next.Publisher)
            {
                result["publisher"] = "manual";
            }

            if (Clean(previous.Language) != next.Language)
            {
                result["language"] = "manual";
            }

            if (previous.Year != next.Year)
            {
                result["year"] = "manual";
            }

            if (JsonSerializer.Serialize(previous.ChapterMetadata) != JsonSerializer.Serialize(next.ChapterMetadata))
            {
                result["chapters"] = "manual";
            }

            return result;
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Present(string value, string source) => string.IsNullOrWhiteSpace(value) ? "unknown" : source;
        private static NzbDroneClientException Invalid(string message) => new NzbDroneClientException(HttpStatusCode.BadRequest, message);
    }
}
