using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public static class AudioRecordingIdentifier
    {
        private static readonly Regex FilenameAsin = new Regex(@"\[([A-Z0-9]{10})\]", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        public static string Normalize(string value)
        {
            var normalized = value?.Trim().ToUpperInvariant();
            return AudiobookId.IsAsin(normalized) ? normalized : null;
        }

        public static void Apply(ParsedTrackInfo tags, string path, IEnumerable<string> embeddedAsins)
        {
            var embeddedValues = (embeddedAsins ?? Enumerable.Empty<string>()).ToList();
            var malformed = embeddedValues.Any(value => !string.IsNullOrWhiteSpace(value) && Normalize(value) == null);
            var embedded = embeddedValues
                .Append(tags.Asin)
                .Select(Normalize)
                .Where(value => value != null)
                .Distinct()
                .ToList();
            var filename = ReadBracketedAsins(Path.GetFileNameWithoutExtension(path));
            var folder = new List<string>();
            var parent = Path.GetDirectoryName(path);
            for (var depth = 0; depth < 4 && !string.IsNullOrEmpty(parent); depth++)
            {
                folder = ReadBracketedAsins(Path.GetFileName(parent));
                if (folder.Any())
                {
                    // Only the nearest identified recording folder owns these tracks.
                    // The bounded walk supports CD/disc subfolders without searching
                    // unrelated library roots for an identifier.
                    break;
                }

                parent = Path.GetDirectoryName(parent);
            }

            var pathAsins = filename.Concat(folder).Distinct().ToList();
            if (tags.PreferPathAsin && pathAsins.Any())
            {
                // An explicit library preference can trust curated paths over embedded tags.
                // Different filename and recording-folder IDs still require review.
                tags.Asin = pathAsins.Count == 1 ? pathAsins[0] : null;
                tags.AsinConflict = tags.AsinConflict || pathAsins.Count > 1;
                return;
            }

            // Preserve a valid embedded identifier for inspection, but never silently choose
            // between different recording identifiers during automatic identification.
            tags.Asin = embedded.Count == 1 ? embedded[0] : filename.Count == 1 ? filename[0] : folder.Count == 1 ? folder[0] : null;
            tags.AsinConflict = tags.AsinConflict || malformed || embedded.Concat(filename).Concat(folder).Distinct().Count() > 1;
        }

        private static List<string> ReadBracketedAsins(string value)
        {
            return FilenameAsin.Matches(value ?? string.Empty)
                .Select(match => Normalize(match.Groups[1].Value))
                .Where(asin => asin != null)
                .Distinct()
                .ToList();
        }

        public static bool MatchesEdition(ParsedTrackInfo tags, string path, Edition edition)
        {
            if (!MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(path)))
            {
                return true;
            }

            tags ??= new ParsedTrackInfo();
            Apply(tags, path, null);
            if (tags.AsinConflict)
            {
                return false;
            }

            var asin = Normalize(tags.Asin);
            if (asin == null)
            {
                return true;
            }

            // An explicit recording identity is a constraint, never just a distance
            // penalty. Check both fields so inconsistent catalog rows fail safely.
            var editionAsin = Normalize(edition?.Asin);
            var identity = edition?.ForeignEditionId?.Split(':');
            var identityAsin = identity?.Length == 3 && identity[0] == "audible" ? Normalize(identity[2]) : null;
            return (editionAsin != null || identityAsin != null) &&
                   (editionAsin == null || editionAsin == asin) &&
                   (identityAsin == null || identityAsin == asin);
        }
    }
}
