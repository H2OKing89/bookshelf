using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            var embedded = (embeddedAsins ?? Enumerable.Empty<string>())
                .Append(tags.Asin)
                .Select(Normalize)
                .Where(value => value != null)
                .Distinct()
                .ToList();
            var filename = FilenameAsin.Matches(Path.GetFileNameWithoutExtension(path) ?? string.Empty)
                .Select(match => Normalize(match.Groups[1].Value))
                .Where(value => value != null)
                .Distinct()
                .ToList();

            // Preserve a valid embedded identifier for inspection, but never silently choose
            // between different recording identifiers during automatic identification.
            tags.Asin = embedded.Count == 1 ? embedded[0] : filename.Count == 1 ? filename[0] : null;
            tags.AsinConflict = tags.AsinConflict || embedded.Concat(filename).Distinct().Count() > 1;
        }
    }
}
