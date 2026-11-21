using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Download
{
    public interface IDownloadGrabSpaceService
    {
        Decision Preview(RemoteBook remoteBook);
        GrabSpaceAssessment Check(RemoteBook remoteBook, DownloadSpaceBudget budget, DownloadClientInfo clientInfo);
    }

    public class DownloadGrabSpaceService : IDownloadGrabSpaceService
    {
        private readonly IConfigService _configService;
        private readonly IAuthorService _authorService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public DownloadGrabSpaceService(IConfigService configService,
                                        IAuthorService authorService,
                                        IRootFolderService rootFolderService,
                                        IDiskProvider diskProvider,
                                        Logger logger)
        {
            _configService = configService;
            _authorService = authorService;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public Decision Preview(RemoteBook remoteBook)
        {
            return _configService.CheckFreeSpaceWhenGrabbing
                ? Assess(remoteBook.Author, remoteBook.Release.Size, null, null).Decision
                : Decision.Accept();
        }

        public GrabSpaceAssessment Check(RemoteBook remoteBook, DownloadSpaceBudget budget, DownloadClientInfo clientInfo = null)
        {
            if (!_configService.CheckFreeSpaceWhenGrabbing)
            {
                return new GrabSpaceAssessment(Decision.Accept());
            }

            // Search and pending releases can outlive a change to the author's destination.
            try
            {
                var author = remoteBook.Author.Id > 0 ? _authorService.GetAuthor(remoteBook.Author.Id) : null;
                return Assess(author, remoteBook.Release.Size, budget, clientInfo);
            }
            catch (ModelNotFoundException)
            {
                return Unverified("The author has no valid library destination.");
            }
        }

        private GrabSpaceAssessment Assess(Author author, long size, DownloadSpaceBudget budget, DownloadClientInfo clientInfo)
        {
            if (size <= 0)
            {
                return Unverified("The release size is unknown. Bookshelf cannot check library free space before grabbing.");
            }

            try
            {
                if (author?.Path.IsPathValid(PathValidationType.CurrentOs) != true)
                {
                    return Unverified("The author has no valid library destination.");
                }

                var path = Path.GetFullPath(author.Path);
                var root = _rootFolderService.GetBestRootFolder(path);

                if (root == null || !_diskProvider.FolderExists(root.Path))
                {
                    return Unverified("The library root folder is missing or inaccessible.");
                }

                var destination = FindExistingDestination(path, Path.GetFullPath(root.Path));
                var mount = destination == null ? null : _diskProvider.GetMount(destination);

                if (mount == null || !mount.IsReady || mount.MountOptions?.IsReadOnly == true || mount.RootDirectory.IsNullOrWhiteSpace())
                {
                    return Unverified("The destination filesystem is missing, inaccessible or read-only.");
                }

                var available = mount.AvailableFreeSpace;

                if (available < 0)
                {
                    return Unverified("Available space on the destination filesystem cannot be determined.");
                }

                var mountPath = mount.RootDirectory;
                var sharedOutput = clientInfo?.IsLocalhost == true && clientInfo.OutputRootFolders?.Any(output => IsOutputOnMount(output, mountPath)) == true;
                var copies = sharedOutput ? 2 : 1;
                var reserve = Math.Max(0, _configService.MinimumFreeSpaceWhenImporting).Megabytes();
                var submitted = budget?.GetSubmittedBytes(mountPath) ?? 0;
                var usable = Math.Max(0, available - submitted);

                // Budget the full release for the staged copy without crediting existing media or possible links.
                if (usable < reserve || size > (usable - reserve) / copies)
                {
                    var needed = sharedOutput ? $"{size.SizeSuffix()} each for the download and staged copy" : $"{size.SizeSuffix()} for the staged copy";
                    var message = $"Not enough library space before grabbing. {usable.SizeSuffix()} available for this batch, " +
                        $"{needed}, and {reserve.SizeSuffix()} required to remain free.";
                    return new GrabSpaceAssessment(Decision.RejectCode(RejectionCode.MinimumFreeSpace, message));
                }

                return new GrabSpaceAssessment(Decision.Accept(), mountPath, size * copies);
            }
            catch (Exception ex) when (IsPathProbeFailure(ex))
            {
                _logger.Debug(ex, "Could not determine library space before grabbing");
                return Unverified("Available space on the destination filesystem cannot be determined.");
            }
        }

        private bool IsOutputOnMount(OsPath output, string mountPath)
        {
            try
            {
                var path = output.FullPath;
                return path.IsPathValid(PathValidationType.CurrentOs) && _diskProvider.FolderExists(path) &&
                    _diskProvider.GetMount(path)?.RootDirectory.PathEquals(mountPath) == true;
            }
            catch (Exception ex) when (IsPathProbeFailure(ex))
            {
                _logger.Debug(ex, "Could not identify the filesystem for a reported download destination");
                return false;
            }
        }

        private static bool IsPathProbeFailure(Exception ex)
        {
            return ex is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or InvalidOperationException;
        }

        private string FindExistingDestination(string path, string root)
        {
            while (root.IsParentPath(path))
            {
                try
                {
                    // Attribute lookup distinguishes permission failures from an author folder that has not been created yet.
                    return _diskProvider.GetFileAttributes(path).HasFlag(FileAttributes.Directory) ? path : null;
                }
                catch (IOException ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
                {
                    path = Path.GetDirectoryName(path);
                }
            }

            return root.PathEquals(path) ? root : null;
        }

        private static GrabSpaceAssessment Unverified(string message)
        {
            return new GrabSpaceAssessment(Decision.RejectCode(RejectionCode.GrabFreeSpaceUnverified, message));
        }
    }

    public class GrabSpaceAssessment
    {
        public GrabSpaceAssessment(Decision decision, string mountPath = null, long bytes = 0)
        {
            Decision = decision;
            MountPath = mountPath;
            Bytes = bytes;
        }

        public Decision Decision { get; }
        public string MountPath { get; }
        public long Bytes { get; }
    }

    // Tracks estimates during one sequence of submissions. This creates no disk reservation.
    public class DownloadSpaceBudget
    {
        private readonly Dictionary<string, long> _submitted = new Dictionary<string, long>(PathEqualityComparer.Instance);

        public long GetSubmittedBytes(string mountPath)
        {
            return _submitted.GetValueOrDefault(mountPath);
        }

        public void RecordSubmission(GrabSpaceAssessment assessment)
        {
            if (assessment.MountPath == null)
            {
                return;
            }

            var previous = GetSubmittedBytes(assessment.MountPath);
            _submitted[assessment.MountPath] = previous + Math.Min(long.MaxValue - previous, assessment.Bytes);
        }
    }
}
