using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport;

namespace NzbDrone.Core.Download
{
    public interface IDownloadImportDiagnostics
    {
        List<TrackedDownloadStatusMessage> GetWarnings(TrackedDownload trackedDownload);
    }

    // Inventory explains an import block. It is never import or cleanup authority.
    public class DownloadImportDiagnostics : IDownloadImportDiagnostics
    {
        private readonly IDiskProvider _disk;
        private readonly IProvideDownloadClient _clients;
        private readonly IRejectedFileExtensionService _extensions;
        private readonly Logger _logger;

        public DownloadImportDiagnostics(IDiskProvider disk, IProvideDownloadClient clients, IRejectedFileExtensionService extensions, Logger logger)
        {
            _disk = disk;
            _clients = clients;
            _extensions = extensions;
            _logger = logger;
        }

        public List<TrackedDownloadStatusMessage> GetWarnings(TrackedDownload trackedDownload)
        {
            var warnings = new List<TrackedDownloadStatusMessage>();
            var paths = new List<string>();
            var output = trackedDownload.ImportItem?.OutputPath.FullPath;
            try
            {
                if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
                {
                    throw new InvalidOperationException("A mapped local output is required");
                }

                output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
                if (output.PathEquals(Path.GetPathRoot(output)))
                {
                    throw new InvalidOperationException("A download output cannot be a filesystem root");
                }

                NoLinks(output);
                if (_disk.FileExists(output))
                {
                    paths.Add(output);
                }
                else if (_disk.FolderExists(output))
                {
                    var pending = new Stack<(string Path, int Depth)>();
                    var visited = new HashSet<string>(PathEqualityComparer.Instance);
                    pending.Push((output, 0));
                    var characters = 0;
                    while (pending.TryPop(out var folder))
                    {
                        if (visited.Count >= 512 || folder.Depth > 32 || !visited.Add(folder.Path))
                        {
                            throw new InventoryLimitException();
                        }

                        NoLinks(folder.Path);
                        foreach (var path in _disk.GetFiles(folder.Path, false))
                        {
                            var normalized = Confined(output, path);
                            NoLinks(normalized);
                            characters += normalized.Length;
                            if (paths.Count >= DownloadClientFile.MaximumFiles || characters > DownloadClientFile.MaximumInventoryPathCharacters)
                            {
                                throw new InventoryLimitException();
                            }

                            paths.Add(normalized);
                        }

                        foreach (var path in _disk.GetDirectories(folder.Path))
                        {
                            if (pending.Count + visited.Count >= 512)
                            {
                                throw new InventoryLimitException();
                            }

                            pending.Push((Confined(output, path), folder.Depth + 1));
                        }
                    }
                }
                else
                {
                    throw new DirectoryNotFoundException();
                }
            }
            catch (InventoryLimitException)
            {
                warnings.Add(Warning(RejectionCode.InventoryTruncated, "Local file inventory exceeds the diagnostic limit. Review the download output path."));
            }
            catch (Exception error)
            {
                _logger.Debug(error, "Local download inventory was unavailable");
                warnings.Add(Warning(RejectionCode.ImportPathUnavailable, "The local download path is unavailable or cannot be safely inspected. Check its mount, permissions and remote path mapping."));
            }

            AddClassification(warnings, paths, false);
            if (paths.Any(path => _extensions.Classify(path) == DownloadFileKind.Media))
            {
                return warnings;
            }

            // Remote names are passed only to the pure extension classifier. Never
            // combine them with output or pass them to a filesystem API.
            try
            {
                var item = trackedDownload.DownloadItem;
                var client = _clients.Get(trackedDownload.DownloadClient);
                if (client == null || client.Definition.Id != trackedDownload.DownloadClient ||
                    (item.DownloadClientInfo?.Id > 0 && item.DownloadClientInfo.Id != trackedDownload.DownloadClient))
                {
                    throw new InvalidOperationException("Download client context changed");
                }

                var remote = new List<string>();
                var characters = 0;
                foreach (var file in client.GetFiles(item) ?? Enumerable.Empty<DownloadClientFile>())
                {
                    if (file == null || file.Size < 0 || !DownloadClientFile.TryNormalizeRelativePath(file.RelativePath, out var path))
                    {
                        warnings.Add(Warning(RejectionCode.InventoryInvalid, "The download client returned an invalid file inventory. Existing import diagnostics remain available."));
                        return warnings;
                    }

                    characters += path.Length;
                    if (remote.Count >= DownloadClientFile.MaximumFiles || characters > DownloadClientFile.MaximumInventoryPathCharacters)
                    {
                        warnings.Add(Warning(RejectionCode.InventoryTruncated, "The download client file inventory exceeds the diagnostic limit."));
                        return warnings;
                    }

                    remote.Add(path);
                }

                AddClassification(warnings, remote, true);
                if (remote.Any(path => _extensions.Classify(path) == DownloadFileKind.Media))
                {
                    warnings.Add(Warning(RejectionCode.ImportPathUnavailable, "The download client reports supported book files, but none are accessible for local import. Check the completed output path and mount or remote path mapping."));
                }
            }
            catch (Exception error)
            {
                _logger.Debug("Download client file inventory was unavailable ({0}); retaining local diagnostics", error.GetType().Name);
                warnings.Add(Warning(RejectionCode.InventoryUnavailable, "The download client file list could not be retrieved. Existing import diagnostics remain available."));
            }

            return warnings;
        }

        private void AddClassification(List<TrackedDownloadStatusMessage> warnings, List<string> paths, bool remote)
        {
            var rejection = _extensions.GetRejection(paths);
            if (rejection != null)
            {
                warnings.Add(new TrackedDownloadStatusMessage(remote ? "Download client files" : "Local download files", new List<string> { rejection.Reason }, new[] { rejection }));
            }
        }

        private static TrackedDownloadStatusMessage Warning(string code, string message)
        {
            return new TrackedDownloadStatusMessage("Import blocked", new List<string> { message }, new[] { new Rejection(message, RejectionType.Temporary, code) });
        }

        private void NoLinks(string path)
        {
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if ((_disk.GetFileAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Download inventory cannot traverse links");
                }
            }
        }

        private static string Confined(string root, string path)
        {
            var normalized = Path.GetFullPath(path);
            if (!root.IsParentPath(normalized))
            {
                throw new InvalidOperationException("Download inventory leaves its output root");
            }

            return normalized;
        }

        private sealed class InventoryLimitException : Exception
        {
        }
    }
}
