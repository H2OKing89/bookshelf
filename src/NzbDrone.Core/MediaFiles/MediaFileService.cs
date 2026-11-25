using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.LibraryStats;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileService
    {
        BookFile Add(BookFile bookFile);
        void AddMany(List<BookFile> bookFiles);
        void ImportFiles(List<BookFile> added, List<BookFile> updated, List<BookFile> replaced);
        void Update(BookFile bookFile);
        void Update(List<BookFile> bookFiles);
        void UpdateQualityAndReleaseGroup(List<BookFile> bookFiles, bool updateQuality, bool updateReleaseGroup);
        void Delete(BookFile bookFile, DeleteMediaFileReason reason);
        void DeleteMany(List<BookFile> bookFiles, DeleteMediaFileReason reason);
        List<BookFile> GetFiles();
        List<BookFile> GetFilesByAuthor(int authorId);
        List<BookFile> GetFilesByAuthorMetadataId(int authorMetadataId);
        List<BookFile> GetFilesByBook(int bookId);
        List<BookFile> GetFilesByEdition(int editionId);
        List<BookFile> GetUnmappedFiles();
        List<IFileInfo> FilterUnchangedFiles(List<IFileInfo> files, FilterFilesType filter);
        BookFile Get(int id);
        List<BookFile> Get(IEnumerable<int> ids);
        List<BookFile> GetFilesWithBasePath(string path);
        List<BookFile> GetFileWithPath(List<string> path);
        BookFile GetFileWithPath(string path);
        void UpdateMediaInfo(List<BookFile> bookFiles);
    }

    public class MediaFileService : IMediaFileService,
        IHandle<AuthorMovedEvent>,
        IHandleAsync<BookDeletedEvent>,
        IHandleAsync<ModelEvent<RootFolder>>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly Logger _logger;

        public MediaFileService(IMediaFileRepository mediaFileRepository, IEventAggregator eventAggregator, Logger logger)
        {
            _mediaFileRepository = mediaFileRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public BookFile Add(BookFile bookFile)
        {
            var addedFile = _mediaFileRepository.Insert(bookFile);
            _eventAggregator.PublishEvent(new BookFileAddedEvent(addedFile));
            return addedFile;
        }

        public void AddMany(List<BookFile> bookFiles)
        {
            _mediaFileRepository.InsertMany(bookFiles);
            foreach (var addedFile in bookFiles)
            {
                _eventAggregator.PublishEvent(new BookFileAddedEvent(addedFile));
            }
        }

        public void Update(BookFile bookFile)
        {
            _mediaFileRepository.Update(bookFile);
            _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
        }

        public void ImportFiles(List<BookFile> added, List<BookFile> updated, List<BookFile> replaced)
        {
            _mediaFileRepository.ImportFiles(added, updated, replaced);
            _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
            foreach (var file in replaced.Where(file => file.EditionId > 0))
            {
                try
                {
                    _eventAggregator.PublishEvent(new BookFileDeletedEvent(file, DeleteMediaFileReason.Upgrade));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Replacement saved, but unable to publish old-file deletion notification for {0}", file.Id);
                }
            }

            foreach (var file in added)
            {
                try
                {
                    _eventAggregator.PublishEvent(new BookFileAddedEvent(file));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Replacement saved, but unable to publish new-file notification for {0}", file.Id);
                }
            }
        }

        public void Update(List<BookFile> bookFiles)
        {
            _mediaFileRepository.UpdateMany(bookFiles);
            if (bookFiles.Count > 0)
            {
                _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
            }
        }

        public void UpdateQualityAndReleaseGroup(List<BookFile> bookFiles, bool updateQuality, bool updateReleaseGroup)
        {
            if (bookFiles.Count == 0 || (!updateQuality && !updateReleaseGroup))
            {
                return;
            }

            // Editor saves are field updates, not import or probe operations.
            // Do not rewrite an existing file's ownership, state or analysis
            // document (including its exact timestamp) from an earlier read.
            if (updateQuality && updateReleaseGroup)
            {
                _mediaFileRepository.SetFields(bookFiles, file => file.Quality, file => file.ReleaseGroup, file => file.ReleaseGroupEvidence);
            }
            else if (updateQuality)
            {
                _mediaFileRepository.SetFields(bookFiles, file => file.Quality);
            }
            else
            {
                _mediaFileRepository.SetFields(bookFiles, file => file.ReleaseGroup, file => file.ReleaseGroupEvidence);
            }

            _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
            if (updateQuality)
            {
                _eventAggregator.PublishEvent(new QualityRankingChangedEvent());
            }
        }

        public void Delete(BookFile bookFile, DeleteMediaFileReason reason)
        {
            _mediaFileRepository.Delete(bookFile);

            // If the trackfile wasn't mapped to a track, don't publish an event
            if (bookFile.EditionId > 0)
            {
                _eventAggregator.PublishEvent(new BookFileDeletedEvent(bookFile, reason));
            }
            else
            {
                _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
            }
        }

        public void DeleteMany(List<BookFile> bookFiles, DeleteMediaFileReason reason)
        {
            _mediaFileRepository.DeleteMany(bookFiles);

            // publish events where trackfile was mapped to a track
            foreach (var bookFile in bookFiles.Where(x => x.EditionId > 0))
            {
                _eventAggregator.PublishEvent(new BookFileDeletedEvent(bookFile, reason));
            }

            if (bookFiles.Any(file => file.EditionId <= 0))
            {
                _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
            }
        }

        public List<IFileInfo> FilterUnchangedFiles(List<IFileInfo> files, FilterFilesType filter)
        {
            if (filter == FilterFilesType.None)
            {
                return files;
            }

            _logger.Debug($"Filtering {files.Count} files for unchanged files");

            var knownFiles = GetFileWithPath(files.Select(x => x.FullName).ToList());
            _logger.Trace($"Got {knownFiles.Count} existing files");

            if (!knownFiles.Any())
            {
                return files;
            }

            var combined = files
                .Join(knownFiles,
                      f => f.FullName,
                      af => af.Path,
                      (f, af) => new { DiskFile = f, DbFile = af },
                      PathEqualityComparer.Instance)
                .ToList();
            _logger.Trace($"Matched paths for {combined.Count} files");

            List<IFileInfo> unwanted = null;
            if (filter == FilterFilesType.Known)
            {
                unwanted = combined
                    .Where(x => IsUnchanged(x.DiskFile, x.DbFile))
                    .Select(x => x.DiskFile)
                    .ToList();
                _logger.Trace($"{unwanted.Count} unchanged existing files");
            }
            else if (filter == FilterFilesType.Matched)
            {
                unwanted = combined
                    .Where(x =>
                    {
                        var unchanged = IsUnchanged(x.DiskFile, x.DbFile);
                        var matched = x.DbFile.Edition == null || (x.DbFile.Edition.IsLoaded && x.DbFile.Edition.Value != null);
                        var alreadyRemoteSearched = x.DbFile.LastRemoteSearchTime.HasValue;

                        // Skip unchanged files that are matched, or that we already searched upstream
                        // for without a match (no point re-searching until the file changes).
                        return unchanged && (matched || alreadyRemoteSearched);
                    })
                    .Select(x => x.DiskFile)
                    .ToList();
                _logger.Trace($"{unwanted.Count} unchanged files that are matched or already remote-searched");
            }
            else
            {
                throw new ArgumentException("Unrecognised value of FilterFilesType filter");
            }

            return files.Except(unwanted).ToList();
        }

        public BookFile Get(int id)
        {
            return _mediaFileRepository.Get(id);
        }

        public List<BookFile> Get(IEnumerable<int> ids)
        {
            return _mediaFileRepository.Get(ids).ToList();
        }

        public List<BookFile> GetFilesWithBasePath(string path)
        {
            return _mediaFileRepository.GetFilesWithBasePath(path);
        }

        public List<BookFile> GetFileWithPath(List<string> path)
        {
            return _mediaFileRepository.GetFileWithPath(path);
        }

        public BookFile GetFileWithPath(string path)
        {
            return _mediaFileRepository.GetFileWithPath(path);
        }

        public List<BookFile> GetFilesByAuthor(int authorId)
        {
            return _mediaFileRepository.GetFilesByAuthor(authorId);
        }

        public List<BookFile> GetFiles()
        {
            return _mediaFileRepository.All().ToList();
        }

        public List<BookFile> GetFilesByAuthorMetadataId(int authorMetadataId)
        {
            return _mediaFileRepository.GetFilesByAuthorMetadataId(authorMetadataId);
        }

        public List<BookFile> GetFilesByBook(int bookId)
        {
            return _mediaFileRepository.GetFilesByBook(bookId);
        }

        public List<BookFile> GetFilesByEdition(int editionId)
        {
            return _mediaFileRepository.GetFilesByEdition(editionId);
        }

        public List<BookFile> GetUnmappedFiles()
        {
            return _mediaFileRepository.GetUnmappedFiles();
        }

        public void UpdateMediaInfo(List<BookFile> bookFiles)
        {
            _mediaFileRepository.UpdateMediaInfo(bookFiles);
            if (bookFiles.Count > 0)
            {
                _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
            }
        }

        public void Handle(AuthorMovedEvent message)
        {
            var files = _mediaFileRepository.GetFilesWithBasePath(message.SourcePath);

            foreach (var file in files)
            {
                var newPath = message.DestinationPath + file.Path.Substring(message.SourcePath.Length);
                file.Path = newPath;
            }

            Update(files);
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                _mediaFileRepository.DeleteFilesByBook(message.Book.Id);
            }
            else
            {
                _mediaFileRepository.UnlinkFilesByBook(message.Book.Id);
            }

            _eventAggregator.PublishEvent(new LibraryStatisticsChangedEvent());
        }

        public void HandleAsync(ModelEvent<RootFolder> message)
        {
            if (message.Action == ModelAction.Deleted)
            {
                var files = GetFilesWithBasePath(message.Model.Path);
                DeleteMany(files, DeleteMediaFileReason.Manual);
            }
        }

        private static bool IsUnchanged(IFileInfo diskFile, BookFile storedFile)
        {
            var metadata = storedFile.MediaInfo;
            var hasAnalysis = metadata?.Analysis != null || metadata?.LastAnalysisAttempt != null;
            var tolerance = hasAnalysis ? MediaInfoAnalysis.StoredTimestampToleranceMilliseconds : 1000;
            var modified = hasAnalysis && storedFile.Modified.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(storedFile.Modified, DateTimeKind.Utc)
                : storedFile.Modified.ToUniversalTime();
            return diskFile.Length == storedFile.Size && Math.Abs((diskFile.LastWriteTimeUtc - modified).TotalMilliseconds) <= tolerance;
        }
    }
}
