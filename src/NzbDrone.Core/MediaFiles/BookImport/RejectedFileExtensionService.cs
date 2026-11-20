using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public enum DownloadFileKind
    {
        Media,
        Sidecar,
        Archive,
        Executable,
        Dangerous,
        Unsupported,
        UserRejected
    }

    public interface IRejectedFileExtensionService
    {
        DownloadFileKind Classify(string path);
        Rejection GetRejection(string path);
        Rejection GetRejection(IEnumerable<string> paths);
    }

    public class RejectedFileExtensionService : IRejectedFileExtensionService
    {
        private static readonly Regex SplitArchive = new Regex(@"(\.[rz][0-9]{2}|\.(7z|zip|rar)\.[0-9]{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> Dangerous = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".arj", ".lnk", ".lzh", ".ps1", ".scr", ".vbs", ".zipx", ".js", ".jse", ".vbe", ".wsf", ".hta"
        };
        private static readonly HashSet<string> Executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".exe", ".sh", ".com", ".msi", ".app", ".appimage", ".dll"
        };
        private static readonly HashSet<string> Archives = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".7z", ".bz2", ".gz", ".rar", ".tar", ".tb2", ".tbz2", ".tgz", ".zip", ".r00"
        };
        private static readonly HashSet<string> Sidecars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cue", ".m3u", ".m3u8", ".nfo", ".txt", ".jpg", ".jpeg", ".png", ".webp", ".gif",
            ".bmp", ".tif", ".tiff", ".json", ".xml", ".opf", ".ncx", ".sfv", ".md5", ".sha1", ".sha256", ".log", ".srt", ".vtt", ".nzb", ".par2"
        };

        private readonly IConfigService _config;

        public RejectedFileExtensionService(IConfigService config)
        {
            _config = config;
        }

        public DownloadFileKind Classify(string path)
        {
            var extension = Extension(path);
            if (Dangerous.Contains(extension))
            {
                return DownloadFileKind.Dangerous;
            }

            if (Executables.Contains(extension))
            {
                return DownloadFileKind.Executable;
            }

            if (RejectedFileExtensions.Parse(_config.UserRejectedExtensions)
                .Any(rejected => path?.EndsWith(rejected, StringComparison.OrdinalIgnoreCase) == true))
            {
                return DownloadFileKind.UserRejected;
            }

            if (MediaFileExtensions.AudioExtensions.Contains(extension) || MediaFileExtensions.TextExtensions.Contains(extension))
            {
                return DownloadFileKind.Media;
            }

            if (Sidecars.Contains(extension))
            {
                return DownloadFileKind.Sidecar;
            }

            return Archives.Contains(extension) || SplitArchive.IsMatch(path ?? string.Empty) ? DownloadFileKind.Archive : DownloadFileKind.Unsupported;
        }

        public Rejection GetRejection(string path)
        {
            var extension = Extension(path);
            switch (Classify(path))
            {
                case DownloadFileKind.Dangerous:
                    return new Rejection($"Potentially dangerous file with extension '{extension}'", code: RejectionCode.DangerousFile);
                case DownloadFileKind.Executable:
                    return new Rejection($"Executable file with extension '{extension}' is not a book", code: RejectionCode.ExecutableFile);
                case DownloadFileKind.UserRejected:
                    return new Rejection($"File extension '{extension}' is rejected by Media Management settings", code: RejectionCode.UserRejectedExtension);
                case DownloadFileKind.Archive:
                    return new Rejection($"Archive with extension '{extension}' must be extracted before import", RejectionType.Temporary, RejectionCode.ArchiveNotExtracted);
                case DownloadFileKind.Unsupported:
                    return new Rejection(extension.Length == 0 ? "File has no supported book extension" : $"Unsupported book extension '{extension}'", code: RejectionCode.UnsupportedFileExtension);
                default:
                    return null;
            }
        }

        public Rejection GetRejection(IEnumerable<string> paths)
        {
            var files = (paths ?? Enumerable.Empty<string>()).ToList();
            var classified = files.Select(path => new { Path = path, Kind = Classify(path) }).ToList();
            foreach (var kind in new[] { DownloadFileKind.Dangerous, DownloadFileKind.Executable, DownloadFileKind.UserRejected })
            {
                var match = classified.FirstOrDefault(file => file.Kind == kind);
                if (match != null)
                {
                    return GetRejection(match.Path);
                }
            }

            if (classified.Any(file => file.Kind == DownloadFileKind.Media))
            {
                return null;
            }

            var archive = classified.FirstOrDefault(file => file.Kind == DownloadFileKind.Archive);
            var unsupported = classified.FirstOrDefault(file => file.Kind == DownloadFileKind.Unsupported);
            return archive != null ? GetRejection(archive.Path) : unsupported != null ? GetRejection(unsupported.Path) : null;
        }

        private static string Extension(string path)
        {
            // Paths can come from a remote client with either separator style.
            // Only the filename extension is inspected; this never resolves a path.
            return Path.GetExtension(path?.Replace('\\', '/') ?? string.Empty);
        }
    }
}
