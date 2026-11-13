using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMediaFiles
    {
        void DeleteTrackFile(Author author, BookFile bookFile);
        void DeleteTrackFile(BookFile bookFile, string subfolder = "");
    }

    public class MediaFileDeletionService : IDeleteMediaFiles,
                                            IHandle<AuthorDeletedEvent>,
                                            IHandleAsync<AuthorDeletedEvent>,
                                            IHandleAsync<BookDeletedEvent>,
                                            IHandle<BookFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAuthorService _authorService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;

        public MediaFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMediaFileService mediaFileService,
                                        IAuthorService authorService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        IRootFolderService rootFolderService,
                                        ICalibreProxy calibre,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _authorService = authorService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _logger = logger;
        }

        public void DeleteTrackFile(Author author, BookFile bookFile)
        {
            var fullPath = bookFile.Path;
            var rootFolder = _rootFolderService.GetBestRootFolder(fullPath)?.Path;

            if (rootFolder.IsNullOrWhiteSpace() || !_diskProvider.FolderExists(rootFolder))
            {
                _logger.Warn("Author's root folder ({0}) doesn't exist.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Author's root folder ({0}) doesn't exist.", rootFolder);
            }

            if (_diskProvider.GetDirectories(rootFolder).Empty() && !_diskProvider.FileExists(fullPath))
            {
                _logger.Warn("Author's root folder ({0}) is empty.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Author's root folder ({0}) is empty.", rootFolder);
            }

            var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(fullPath));
            DeleteTrackFile(bookFile, subfolder);
        }

        public void DeleteTrackFile(BookFile bookFile, string subfolder = "")
        {
            var fullPath = bookFile.Path;

            if (_diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting book file: {0}", fullPath);
                DeleteFile(bookFile, subfolder);
            }

            // Delete the track file from the database to clean it up even if the file was already deleted
            _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        private void DeleteFile(BookFile bookFile, string subfolder = "")
        {
            var rootFolder = _rootFolderService.GetBestRootFolder(bookFile.Path);
            var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

            try
            {
                if (!isCalibre)
                {
                    _recycleBinProvider.DeleteFile(bookFile.Path, subfolder);
                }
                else
                {
                    _calibre.DeleteBook(bookFile, rootFolder.CalibreSettings);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to delete book file");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete book file");
            }
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AuthorDeletedEvent message)
        {
            if (!message.DeleteFiles)
            {
                return;
            }

            var author = message.Author;
            if (_authorService.GetAllAuthors().Any(other => other.Id != author.Id && other.AuthorMetadataId == author.AuthorMetadataId))
            {
                _logger.Warn("Author metadata is still used by another library profile, preserving its files: {0}", author);
                return;
            }

            // Capture ownership before the synchronous book-deletion handler removes the
            // catalog rows. Author.Path is an organizational preference, not file ownership.
            var files = _mediaFileService.GetFilesByAuthorMetadataId(author.AuthorMetadataId);
            var ownedIds = files.Select(file => file.Id).ToHashSet();
            var otherPaths = _mediaFileService.GetFileWithPath(files.Select(file => file.Path).ToList())
                .Where(file => !ownedIds.Contains(file.Id))
                .Select(file => file.Path)
                .ToHashSet(NzbDrone.Common.PathEqualityComparer.Instance);
            var deletedCalibreBooks = new HashSet<string>();
            foreach (var file in files)
            {
                var root = _rootFolderService.GetBestRootFolder(file.Path);
                if (otherPaths.Contains(file.Path) || root == null || !_diskProvider.FolderExists(root.Path))
                {
                    _logger.Warn("Preserving book file with shared ownership or an unavailable root: {0}", file.Path);
                    continue;
                }

                file.Author = author;
                var isCalibre = root.IsCalibreLibrary && root.CalibreSettings != null;
                if (isCalibre)
                {
                    if (file.CalibreId > 0 && deletedCalibreBooks.Add($"{root.Path}|{file.CalibreId}"))
                    {
                        _calibre.DeleteBook(file, root.CalibreSettings);
                    }
                }
                else if (_diskProvider.FileExists(file.Path))
                {
                    var subfolder = root.Path.GetRelativePath(Path.GetDirectoryName(file.Path));
                    DeleteFile(file, subfolder);
                }

                _mediaFileService.Delete(file, DeleteMediaFileReason.Manual);
            }
        }

        public void HandleAsync(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                // Never recycle an entire author directory: it can contain other authors'
                // recordings, unmapped files, or user sidecars. Empty-folder cleanup remains
                // governed by DeleteEmptyFolders during individual file deletion.
                _eventAggregator.PublishEvent(new DeleteCompletedEvent());
            }
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var files = _mediaFileService.GetFilesByBook(message.Book.Id);
                foreach (var file in files)
                {
                    DeleteFile(file);
                }
            }
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(BookFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            if (_configService.DeleteEmptyFolders)
            {
                var author = message.BookFile.Author?.Value;
                if (author == null)
                {
                    return;
                }

                var bookFolder = message.BookFile.Path.GetParentPath();
                var root = _rootFolderService.GetBestRootFolder(message.BookFile.Path);
                var otherAuthorPaths = _authorService.AllAuthorPaths()
                    .Where(item => item.Key != author.Id).Select(item => item.Value).ToList();

                if (root != null && !root.Path.PathEquals(author.Path) &&
                    !otherAuthorPaths.Any(path => author.Path.PathEquals(path) || author.Path.IsParentPath(path)) &&
                    _diskProvider.FolderExists(author.Path) && _diskProvider.FolderEmpty(author.Path))
                {
                    _diskProvider.DeleteFolder(author.Path, false);
                }
                else if (root != null && !root.Path.PathEquals(bookFolder) &&
                         !otherAuthorPaths.Any(path => bookFolder.PathEquals(path) || bookFolder.IsParentPath(path)) &&
                         _diskProvider.FolderExists(bookFolder) && _diskProvider.GetFiles(bookFolder, true).Empty())
                {
                    _diskProvider.RemoveEmptySubfolders(bookFolder);
                }
            }
        }
    }
}
