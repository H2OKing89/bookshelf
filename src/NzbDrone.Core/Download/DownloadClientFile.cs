using System.Globalization;
using System.Linq;

namespace NzbDrone.Core.Download
{
    // Client inventory describes remote names only. It is never a local path or
    // evidence that an imported file exists, and cannot authorize deletion.
    public class DownloadClientFile
    {
        public const int MaximumFiles = 10000;
        public const int MaximumRelativePathLength = 4096;
        public const int MaximumInventoryPathCharacters = 1024 * 1024;

        public string RelativePath { get; set; }
        public long? Size { get; set; }

        public static bool TryNormalizeRelativePath(string value, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumRelativePathLength ||
                value.Any(character => char.IsControl(character) ||
                    char.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
            {
                return false;
            }

            var normalized = value.Replace('\\', '/');
            if (normalized[0] == '/' ||
                (normalized.Length > 1 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':'))
            {
                return false;
            }

            var segments = normalized.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            {
                return false;
            }

            relativePath = normalized;
            return true;
        }
    }
}
