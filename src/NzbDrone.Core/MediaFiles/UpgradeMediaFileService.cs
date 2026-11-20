using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false);
        void CommitUpgrade(List<BookFileMoveResult> replacements);
        void AbortUpgrade(List<BookFileMoveResult> replacements);
        void FinalizeUpgrade(List<BookFileMoveResult> replacements);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IMoveBookFiles _bookFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMetadataTagService metadataTagService,
                                       IMoveBookFiles bookFileMover,
                                       IDiskProvider diskProvider,
                                       IRootFolderService rootFolderService,
                                       ICalibreProxy calibre,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _bookFileMover = bookFileMover;
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _logger = logger;
        }

        public BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false)
        {
            var editionId = bookFile.EditionId;
            if (bookFile.EditionId > 0 && localBook.Edition?.Id > 0 && (bookFile.EditionId != localBook.Edition.Id ||
                    (localBook.Edition.BookId > 0 && localBook.Edition.BookId != localBook.Book.Id)))
            {
                throw new InvalidOperationException("Incoming file and import decision identify different recordings");
            }

            // A book may retain several regional editions or narrations. Only the
            // explicitly selected recording owns the files this import can retire.
            var existingFiles = localBook.Book.BookFiles.Value
                .Where(file => editionId > 0 && file.EditionId == editionId).ToList();
            if (existingFiles.Any())
            {
                var ownedIds = existingFiles.Select(file => file.Id).ToHashSet();
                var sharedPaths = (_mediaFileService.GetFileWithPath(existingFiles.Select(file => file.Path).ToList()) ?? new List<BookFile>())
                    .Where(file => !ownedIds.Contains(file.Id)).Select(file => file.Path)
                    .ToHashSet(NzbDrone.Common.PathEqualityComparer.Instance);
                existingFiles.RemoveAll(file => sharedPaths.Contains(file.Path));
            }

            var rootFolderPath = _diskProvider.GetParentFolder(localBook.Author.Path);
            var rootFolder = _rootFolderService.GetBestRootFolder(rootFolderPath);
            if (rootFolder == null || !_diskProvider.FolderExists(rootFolder.Path))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolderPath}' was not found.");
            }

            var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;
            BookFileMoveResult result;
            if (isCalibre)
            {
                result = new BookFileMoveResult
                {
                    BookFile = bookFile,
                    SourcePath = bookFile.Path,
                    CopyOnly = copyOnly,
                    OldFiles = existingFiles,
                    CalibreSettings = rootFolder.CalibreSettings
                };

                // A new Calibre entry leaves every old format intact if conversion fails.
                // Reusing its ID would replace the original format before conversion finishes.
                bookFile.CalibreId = 0;
                try
                {
                    result.BookFile = _calibre.AddAndConvert(bookFile, rootFolder.CalibreSettings);
                    return result;
                }
                catch
                {
                    AbortUpgrade(new List<BookFileMoveResult> { result });
                    throw;
                }
            }

            result = _bookFileMover.StageBookFile(bookFile, localBook);
            result.CopyOnly = copyOnly;
            result.OldFiles = existingFiles;
            var destination = bookFile.Path;
            try
            {
                if (_diskProvider.FileExists(destination) && !existingFiles.Any(file => file.Path.PathEquals(destination)))
                {
                    throw new DestinationAlreadyExistsException($"Import destination already exists: {destination}");
                }

                bookFile.Path = result.StagingPath;
                _metadataTagService.WriteTags(bookFile, true);
                return result;
            }
            catch
            {
                AbortUpgrade(new List<BookFileMoveResult> { result });
                throw;
            }
            finally
            {
                bookFile.Path = destination;
            }
        }

        public void CommitUpgrade(List<BookFileMoveResult> replacements)
        {
            if (!replacements.Any())
            {
                return;
            }

            var pending = replacements.Where(result => !result.Committed).ToList();
            var diskReplacements = pending.Where(result => result.CalibreSettings == null).ToList();
            foreach (var recording in pending.GroupBy(result => result.BookFile.EditionId))
            {
                var oldRecordingFiles = recording.SelectMany(result => result.OldFiles).DistinctBy(file => file.Id).ToList();
                var incomingParts = recording.Select(result => result.BookFile.Part).ToHashSet();
                var oldDuration = oldRecordingFiles.All(file => file.MediaInfo?.DurationSeconds > 0)
                    ? oldRecordingFiles.Sum(file => file.MediaInfo.DurationSeconds.Value) : 0;
                var newDuration = recording.All(result => result.BookFile.MediaInfo?.DurationSeconds > 0)
                    ? recording.Sum(result => result.BookFile.MediaInfo.DurationSeconds.Value) : 0;
                if (oldRecordingFiles.Count > recording.Count() && !(oldDuration > 0 && newDuration >= oldDuration * 0.95))
                {
                    // Another recording's runtime cannot prove that missing parts of
                    // this recording were included in a consolidated replacement.
                    oldRecordingFiles = oldRecordingFiles.Where(file => incomingParts.Contains(file.Part) ||
                        recording.Any(result => result.BookFile.Path.PathEquals(file.Path))).ToList();
                }

                foreach (var replacement in recording)
                {
                    replacement.OldFiles.RemoveAll(file => !oldRecordingFiles.Any(old => old.Id == file.Id));
                }
            }

            var oldFiles = pending.SelectMany(result => result.OldFiles).DistinctBy(file => file.Id).ToList();

            try
            {
                // Validate the whole release before changing any library path. In particular,
                // two parts must never publish to the same destination.
                var destinations = new HashSet<string>(NzbDrone.Common.PathEqualityComparer.Instance);
                foreach (var replacement in diskReplacements)
                {
                    var destination = replacement.BookFile.Path;
                    if (pending.Any(result => result.SourcePath.PathEquals(destination)))
                    {
                        throw new SameFilenameException("Import destination is also a source file in this batch", destination);
                    }

                    if (!destinations.Add(destination) ||
                        (_diskProvider.FileExists(destination) && !oldFiles.Any(file => file.Path.PathEquals(destination))))
                    {
                        throw new DestinationAlreadyExistsException($"Import destination already exists: {destination}");
                    }
                }

                foreach (var replacement in diskReplacements)
                {
                    var destination = replacement.BookFile.Path;
                    if (_diskProvider.FileExists(destination))
                    {
                        var backup = destination + $".bookshelf-backup-{Guid.NewGuid():N}";
                        _diskProvider.MoveFile(destination, backup);
                        replacement.BackupPath = backup;
                    }

                    _diskProvider.MoveFile(replacement.StagingPath, destination);
                    replacement.Published = true;
                }
            }
            catch
            {
                AbortUpgrade(pending);
                throw;
            }
        }

        public void FinalizeUpgrade(List<BookFileMoveResult> replacements)
        {
            var pending = replacements.Where(result => !result.Committed && !result.Aborted).ToList();
            var oldFiles = pending.SelectMany(result => result.OldFiles).DistinctBy(file => file.Id).ToList();
            var backups = pending.Where(result => result.BackupPath.IsNotNullOrWhiteSpace())
                .ToDictionary(result => result.BookFile.Path, result => result.BackupPath, NzbDrone.Common.PathEqualityComparer.Instance);

            // The complete replacement is now readable at its final paths. Keep failures in
            // optional retirement/source cleanup from turning a successful import into a retry.
            foreach (var replacement in pending)
            {
                replacement.Committed = true;
            }

            foreach (var file in oldFiles)
            {
                var calibreReplacement = pending.FirstOrDefault(result => result.CalibreSettings != null && result.OldFiles.Any(old => old.Id == file.Id));
                if (calibreReplacement != null)
                {
                    if (file.CalibreId > 0)
                    {
                        TryCleanup(() =>
                        {
                            // Calibre deletes a whole record, including other formats.
                            // A retained part/edition/author may still own that record.
                            var root = _rootFolderService.GetBestRootFolder(file.Path);
                            var knownFiles = root == null ? null : _mediaFileService.GetFilesWithBasePath(root.Path);
                            if (knownFiles == null || knownFiles.Any(other => other.CalibreId == file.CalibreId &&
                                !oldFiles.Any(retired => retired.Id == other.Id)))
                            {
                                return;
                            }

                            _calibre.DeleteBook(file, calibreReplacement.CalibreSettings);
                        }, file.Path);
                    }
                }
                else
                {
                    var path = backups.TryGetValue(file.Path, out var backup) ? backup : file.Path;
                    if (_diskProvider.FileExists(path))
                    {
                        var root = _rootFolderService.GetBestRootFolder(file.Path);
                        var subfolder = root?.Path.GetRelativePath(Path.GetDirectoryName(file.Path)) ?? string.Empty;
                        TryCleanup(() => _recycleBinProvider.DeleteFile(path, subfolder), path);
                    }
                }
            }

            foreach (var replacement in pending.Where(result => !result.CopyOnly))
            {
                // The source is retained through copy, tagging, publication and old-file
                // retirement. Cleanup failure simply leaves an extra recoverable copy.
                if (_diskProvider.FileExists(replacement.SourcePath))
                {
                    TryCleanup(() => _diskProvider.DeleteFile(replacement.SourcePath), replacement.SourcePath);
                }
            }
        }

        public void AbortUpgrade(List<BookFileMoveResult> replacements)
        {
            foreach (var replacement in replacements.Where(result => !result.Committed && !result.Aborted).Reverse())
            {
                replacement.Aborted = true;
                if (replacement.CalibreSettings != null)
                {
                    if (replacement.BookFile.CalibreId > 0)
                    {
                        TryCleanup(() => _calibre.DeleteBook(replacement.BookFile, replacement.CalibreSettings), replacement.SourcePath);
                    }
                }
                else
                {
                    if (replacement.Published && _diskProvider.FileExists(replacement.BookFile.Path))
                    {
                        TryCleanup(() => _diskProvider.DeleteFile(replacement.BookFile.Path), replacement.BookFile.Path);
                    }

                    if (replacement.BackupPath.IsNotNullOrWhiteSpace() && _diskProvider.FileExists(replacement.BackupPath))
                    {
                        TryCleanup(() => _diskProvider.MoveFile(replacement.BackupPath, replacement.BookFile.Path), replacement.BackupPath);
                    }

                    if (replacement.StagingPath.IsNotNullOrWhiteSpace() && _diskProvider.FileExists(replacement.StagingPath))
                    {
                        TryCleanup(() => _diskProvider.DeleteFile(replacement.StagingPath), replacement.StagingPath);
                    }
                }
            }
        }

        private void TryCleanup(Action cleanup, string path)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to finish import cleanup for {0}; retaining the recoverable copy", path);
            }
        }
    }
}
