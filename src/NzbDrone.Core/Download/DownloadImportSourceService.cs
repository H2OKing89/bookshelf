using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Download
{
    public class DownloadImportSourceService : IDownloadImportSourceService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolders;
        private readonly IMediaFileService _mediaFiles;
        private readonly IEditionService _editions;
        private readonly IProvideImportItemService _importItems;
        private readonly Logger _logger;

        public DownloadImportSourceService(IDiskProvider diskProvider,
                                          IRootFolderService rootFolders,
                                          IMediaFileService mediaFiles,
                                          IEditionService editions,
                                          IProvideImportItemService importItems,
                                          Logger logger)
        {
            _diskProvider = diskProvider;
            _rootFolders = rootFolders;
            _mediaFiles = mediaFiles;
            _editions = editions;
            _importItems = importItems;
            _logger = logger;
        }

        public bool IsSafeRejectionSource(TrackedDownload trackedDownload)
        {
            try
            {
                var item = trackedDownload.ImportItem;
                if (item == null || item.DownloadId != trackedDownload.DownloadItem.DownloadId || item.DownloadClientInfo?.Id != trackedDownload.DownloadClient)
                {
                    return false;
                }

                var root = Normalize(item.OutputPath.FullPath);
                ValidateSourceRoot(root);
                return _diskProvider.FileExists(root) || _diskProvider.FolderExists(root);
            }
            catch (Exception error)
            {
                _logger.Debug("Unable to validate rejection source ({0}); retaining the download for review", error.GetType().Name);
                return false;
            }
        }

        public List<DownloadImportSourceFile> Scan(TrackedDownload trackedDownload)
        {
            var root = OutputPath(trackedDownload);
            ValidateSourceRoot(root);
            return Scan(root);
        }

        public bool Validate(TrackedDownload trackedDownload, DownloadImportReceipt receipt)
        {
            try
            {
                return ValidateReceipt(trackedDownload, receipt);
            }
            catch (Exception error)
            {
                _logger.Debug(error, "Unable to validate download source authority; retaining the download for review");
                return false;
            }
        }

        private bool ValidateReceipt(TrackedDownload trackedDownload, DownloadImportReceipt receipt)
        {
            var root = OutputPath(trackedDownload);
            ValidateSourceRoot(root);
            if (receipt == null || receipt.Version != 1 || receipt.Files == null || receipt.Files.Count == 0 ||
                receipt.BookIds == null || receipt.BookIds.Count == 0 || receipt.BookIds.Any(id => id <= 0) ||
                !receipt.BookIds.ToHashSet().SetEquals(receipt.Files.Select(file => file.BookId)) ||
                !string.Equals(receipt.DownloadId, trackedDownload.DownloadItem?.DownloadId, StringComparison.Ordinal) ||
                receipt.DownloadClientId != trackedDownload.DownloadClient || !root.PathEquals(Normalize(receipt.OutputPath)))
            {
                return false;
            }

            var current = Scan(root);
            if (_diskProvider.FileExists(root) && !Eligible(root))
            {
                return false;
            }

            var sources = new HashSet<string>(PathEqualityComparer.Instance);
            var fileIds = new HashSet<int>();
            var importedPaths = receipt.Files.Select(file => Normalize(file.ImportedPath)).ToHashSet(PathEqualityComparer.Instance);
            foreach (var file in receipt.Files)
            {
                var source = Normalize(file.SourcePath);
                var destination = Normalize(file.ImportedPath);
                if (!Contains(root, source) || !Eligible(source) || !sources.Add(source) || !fileIds.Add(file.FileId) || file.FileId <= 0 ||
                    file.BookId <= 0 || file.EditionId <= 0 || file.SourceSize < 0 || file.SourceModifiedUtc == default ||
                    importedPaths.Contains(source) || Contains(root, destination))
                {
                    return false;
                }

                ValidateSourceRoot(source);
                ValidateDestinationPath(destination);
                var live = _mediaFiles.Get(file.FileId);
                var edition = _editions.GetEdition(file.EditionId);
                if (live == null || live.Id != file.FileId || live.EditionId != file.EditionId || live.Part != file.Part ||
                    edition == null || edition.BookId != file.BookId || !live.Path.PathEquals(destination) ||
                    !_diskProvider.FileExists(destination))
                {
                    return false;
                }

                if (_diskProvider.FileExists(source) &&
                    (_diskProvider.GetFileSize(source) != file.SourceSize || _diskProvider.FileGetLastWrite(source) != file.SourceModifiedUtc))
                {
                    return false;
                }
            }

            // Validate the entire live inventory before deleting a single source.
            // Unexpected/new files or changed identities retain the whole download.
            if (current.Any(file => !sources.Contains(file.Path)))
            {
                return false;
            }

            return true;
        }

        public bool Retire(TrackedDownload trackedDownload, DownloadImportReceipt receipt)
        {
            if (!Validate(trackedDownload, receipt))
            {
                return false;
            }

            try
            {
                if (trackedDownload.KeepSourceFiles || receipt.KeepSourceFiles || trackedDownload.DownloadItem?.CanMoveFiles != true)
                {
                    return true;
                }

                foreach (var file in receipt.Files)
                {
                    var source = Normalize(file.SourcePath);
                    var destination = Normalize(file.ImportedPath);
                    if (_diskProvider.FileExists(source))
                    {
                        // Recheck the immediate authority after the batch preflight.
                        ValidateSourceRoot(source);
                        ValidateDestinationPath(destination);
                        if (!_diskProvider.FileExists(destination) || _diskProvider.GetFileSize(source) != file.SourceSize ||
                            _diskProvider.FileGetLastWrite(source) != file.SourceModifiedUtc)
                        {
                            return false;
                        }

                        _diskProvider.DeleteFile(source);
                    }
                }

                // Sidecars, archives and directories are not authorized by a file receipt.
                return true;
            }
            catch (Exception error)
            {
                _logger.Debug(error, "Unable to retire proven download sources; retaining the download for review");
                return false;
            }
        }

        private List<DownloadImportSourceFile> Scan(string root)
        {
            if (_diskProvider.FileExists(root))
            {
                return Eligible(root) ? new List<DownloadImportSourceFile> { Snapshot(root) } : new List<DownloadImportSourceFile>();
            }

            if (!_diskProvider.FolderExists(root))
            {
                return new List<DownloadImportSourceFile>();
            }

            var result = new List<DownloadImportSourceFile>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var folder))
            {
                ValidateSourceRoot(folder);
                foreach (var path in _diskProvider.GetFiles(folder, false))
                {
                    var normalized = Normalize(path);
                    if (!Contains(root, normalized))
                    {
                        throw new InvalidOperationException("Source inventory leaves the download output path");
                    }

                    ValidateNoLinks(normalized);
                    if (Eligible(normalized))
                    {
                        result.Add(Snapshot(normalized));
                    }
                }

                foreach (var path in _diskProvider.GetDirectories(folder))
                {
                    var normalized = Normalize(path);
                    if (root.PathEquals(normalized) || !Contains(root, normalized))
                    {
                        throw new InvalidOperationException("Source inventory leaves the download output path");
                    }

                    ValidateSourceRoot(normalized);
                    pending.Push(normalized);
                }
            }

            return result.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
        }

        private DownloadImportSourceFile Snapshot(string path)
        {
            return new DownloadImportSourceFile
            {
                Path = path,
                Size = _diskProvider.GetFileSize(path),
                ModifiedUtc = _diskProvider.FileGetLastWrite(path)
            };
        }

        private void ValidateSourceRoot(string path)
        {
            ValidateNoLinks(path);
            foreach (var library in TrustedLibraryRoots())
            {
                if (Contains(library.Configured, path) || Contains(path, library.Configured) ||
                    Contains(library.Resolved, path) || Contains(path, library.Resolved))
                {
                    throw new InvalidOperationException("Download source overlaps a library root");
                }
            }
        }

        private void ValidateDestinationPath(string path)
        {
            var anchors = new List<string>();
            foreach (var library in TrustedLibraryRoots())
            {
                if (Contains(library.Configured, path))
                {
                    anchors.Add(library.Configured);
                }

                if (Contains(library.Resolved, path))
                {
                    anchors.Add(library.Resolved);
                }
            }

            var anchor = anchors.OrderByDescending(value => value.Length).FirstOrDefault();
            if (anchor == null)
            {
                throw new InvalidOperationException("Imported destination must belong to a configured library root");
            }

            // The operator's configured root (and its ancestors) may intentionally
            // alias another disk. No child link may extend that trusted boundary.
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if (current.PathEquals(anchor))
                {
                    return;
                }

                if (!Contains(anchor, current))
                {
                    throw new InvalidOperationException("Imported destination leaves its configured library root");
                }

                if ((_diskProvider.GetFileAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Imported destinations must not traverse links below their library root");
                }
            }

            throw new InvalidOperationException("Imported destination leaves its configured library root");
        }

        private IEnumerable<(string Configured, string Resolved)> TrustedLibraryRoots()
        {
            foreach (var root in _rootFolders.All() ?? new List<RootFolder>())
            {
                if (root.Path.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var configured = Normalize(root.Path);
                yield return (configured, ResolveLibraryRoot(configured));
            }
        }

        private string ResolveLibraryRoot(string path)
        {
            var resolved = path;
            for (var hops = 0; hops < 64; hops++)
            {
                var prefix = Path.GetPathRoot(resolved);
                var components = resolved.Substring(prefix.Length)
                    .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                var followed = false;
                foreach (var component in components)
                {
                    prefix = Path.Combine(prefix, component);
                    if ((_diskProvider.GetFileAttributes(prefix) & FileAttributes.ReparsePoint) == 0)
                    {
                        continue;
                    }

                    var target = _diskProvider.GetDirectoryInfo(prefix).LinkTarget;
                    if (target.IsNullOrWhiteSpace())
                    {
                        throw new InvalidOperationException("Configured library link target could not be resolved");
                    }

                    var targetPath = Path.IsPathFullyQualified(target) ? target : Path.Combine(Path.GetDirectoryName(prefix), target);
                    var suffix = Path.GetRelativePath(prefix, resolved);
                    resolved = Normalize(suffix == "." ? targetPath : Path.Combine(targetPath, suffix));
                    followed = true;
                    break;
                }

                if (!followed)
                {
                    if (!_diskProvider.FolderExists(resolved))
                    {
                        throw new InvalidOperationException("Configured library root is unavailable");
                    }

                    return resolved;
                }
            }

            throw new InvalidOperationException("Configured library link chain could not be resolved safely");
        }

        private void ValidateNoLinks(string path)
        {
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
            {
                FileAttributes attributes;
                try
                {
                    // FileExists is false for dangling links; attributes are still
                    // needed to refuse their authority before target resolution.
                    attributes = _diskProvider.GetFileAttributes(current);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Download paths must not traverse symbolic links");
                }
            }
        }

        private string OutputPath(TrackedDownload trackedDownload)
        {
            if (trackedDownload?.DownloadItem == null)
            {
                throw new InvalidOperationException("A tracked download is required");
            }

            trackedDownload.ImportItem = _importItems.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
            if (trackedDownload.ImportItem == null)
            {
                throw new InvalidOperationException("The local download import path could not be determined");
            }

            var item = trackedDownload?.ImportItem;
            var path = item?.OutputPath.FullPath;
            if (path.IsNullOrWhiteSpace())
            {
                path = trackedDownload?.DownloadItem?.OutputPath.FullPath;
            }

            return Normalize(path);
        }

        private static bool Contains(string root, string path)
        {
            return root.PathEquals(path) || root.IsParentPath(path);
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                throw new InvalidOperationException("An absolute download output path is required");
            }

            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (normalized.PathEquals(Path.GetPathRoot(normalized)))
            {
                throw new InvalidOperationException("A filesystem root cannot authorize download cleanup");
            }

            return normalized;
        }

        private static bool Eligible(string path)
        {
            var extension = Path.GetExtension(path);
            return MediaFileExtensions.AudioExtensions.Contains(extension) || MediaFileExtensions.TextExtensions.Contains(extension);
        }
    }
}
