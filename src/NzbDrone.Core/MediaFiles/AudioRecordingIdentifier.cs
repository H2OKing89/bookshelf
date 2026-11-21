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
    internal class AudioRecordingEvidence
    {
        public List<string> FilenameAsins { get; set; } = new List<string>();
        public List<string> FolderAsins { get; set; } = new List<string>();
        public List<string> EmbeddedAsins { get; set; } = new List<string>();
        public bool MalformedEmbeddedAsin { get; set; }
        public bool IdentifierConflict { get; set; }
        public string EffectiveAsin { get; set; }
    }

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
            var evidence = Inspect(path, embeddedAsins, tags.PreferPathAsin, tags.Asin);
            tags.Asin = evidence.EffectiveAsin;
            tags.AsinConflict |= evidence.IdentifierConflict;
        }

        internal static AudioRecordingEvidence Inspect(string path, IEnumerable<string> embeddedAsins, bool preferPathAsin, string existingAsin = null)
        {
            var embeddedValues = (embeddedAsins ?? Enumerable.Empty<string>()).ToList();
            var malformed = embeddedValues.Any(value => !string.IsNullOrWhiteSpace(value) && Normalize(value) == null);
            var embedded = embeddedValues
                .Append(existingAsin)
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

            var evidence = new AudioRecordingEvidence
            {
                FilenameAsins = filename,
                FolderAsins = folder,
                EmbeddedAsins = embedded,
                MalformedEmbeddedAsin = malformed
            };
            var pathAsins = filename.Concat(folder).Distinct().ToList();
            if (preferPathAsin && pathAsins.Any())
            {
                // An explicit library preference can trust curated paths over embedded tags.
                // Different filename and recording-folder IDs still require review.
                evidence.EffectiveAsin = pathAsins.Count == 1 ? pathAsins[0] : null;
                evidence.IdentifierConflict = pathAsins.Count > 1;
                return evidence;
            }

            // Preserve a valid embedded identifier for inspection, but never silently choose
            // between different recording identifiers during automatic identification.
            evidence.EffectiveAsin = embedded.Count == 1 ? embedded[0] : filename.Count == 1 ? filename[0] : folder.Count == 1 ? folder[0] : null;
            evidence.IdentifierConflict = malformed || embedded.Concat(filename).Concat(folder).Distinct().Count() > 1;
            return evidence;
        }

        internal static List<string> ReadBracketedAsins(string value)
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

            return MatchesAsin(asin, edition);
        }

        internal static bool MatchesAsin(string asin, Edition edition)
        {
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
