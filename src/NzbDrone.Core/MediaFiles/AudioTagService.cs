using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using TagLib;

namespace NzbDrone.Core.MediaFiles
{
    public interface IAudioTagService
    {
        AudioTag ReadAudioTag(string path);
        ParsedTrackInfo ReadTags(string file);
        void WriteTags(BookFile trackfile, bool newDownload, bool force = false);
        void SyncTags(List<Edition> tracks);
        List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId);
        List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId);
        void RetagFiles(RetagFilesCommand message);
        void RetagAuthor(RetagAuthorCommand message);
    }

    public class AudioTagService : IAudioTagService
    {
        private readonly IConfigService _configService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderWatchingService _rootFolderWatchingService;
        private readonly IAuthorService _authorService;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IMediaInfoProbe _mediaInfoProbe;
        private readonly Logger _logger;

        public AudioTagService(IConfigService configService,
                               IMediaFileService mediaFileService,
                               IDiskProvider diskProvider,
                               IRootFolderWatchingService rootFolderWatchingService,
                               IAuthorService authorService,
                               IMapCoversToLocal mediaCoverService,
                               IEventAggregator eventAggregator,
                               IMediaInfoProbe mediaInfoProbe,
                               Logger logger)
        {
            _configService = configService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _rootFolderWatchingService = rootFolderWatchingService;
            _authorService = authorService;
            _mediaCoverService = mediaCoverService;
            _eventAggregator = eventAggregator;
            _mediaInfoProbe = mediaInfoProbe;
            _logger = logger;
        }

        public AudioTag ReadAudioTag(string path)
        {
            var bundleId = _mediaInfoProbe.BundleId;
            var before = MediaInfoAnalysis.Capture(_diskProvider, path, bundleId);
            var tag = new AudioTag(path);
            var mediaInfo = _mediaInfoProbe.Read(path);
            if (mediaInfo != null)
            {
                // TagLib can fill gaps in a partial probe of a single audio stream.
                // With multiple streams it may describe a different stream from MediaInfo's default.
                if (mediaInfo.AudioStreams.Count <= 1)
                {
                    var durationSeconds = mediaInfo.DurationSeconds;
                    var primary = mediaInfo.AudioStreams.FirstOrDefault() ?? mediaInfo;
                    primary.AudioBitrate = primary.AudioBitrate > 0 ? primary.AudioBitrate : tag.MediaInfo.AudioBitrate;
                    primary.AudioChannels = primary.AudioChannels > 0 ? primary.AudioChannels : tag.MediaInfo.AudioChannels;
                    primary.AudioBits = primary.AudioBits > 0 ? primary.AudioBits : tag.MediaInfo.AudioBits;
                    primary.AudioSampleRate = primary.AudioSampleRate > 0 ? primary.AudioSampleRate : tag.MediaInfo.AudioSampleRate;
                    mediaInfo.SetPrimaryStream(primary);
                    mediaInfo.DurationSeconds ??= durationSeconds;
                }

                tag.MediaInfo = mediaInfo;
                if (mediaInfo.DurationSeconds.HasValue)
                {
                    tag.Duration = TimeSpan.FromSeconds(mediaInfo.DurationSeconds.Value);
                }
            }

            var after = MediaInfoAnalysis.Capture(_diskProvider, path, bundleId);
            if (MediaInfoAnalysis.SameFile(before, after))
            {
                MediaInfoAnalysis.Stamp(tag.MediaInfo, before, DateTime.UtcNow);
            }
            else if (before != null)
            {
                tag.MediaInfo.Analysis = null;
                tag.MediaInfo.LastAnalysisAttempt = new MediaAnalysisAttempt { Target = after ?? before, AttemptedAtUtc = DateTime.UtcNow };
            }

            return tag;
        }

        public ParsedTrackInfo ReadTags(string path)
        {
            // Apply the import preference only when identifying files. Tag writes and
            // previews continue reading all identifiers without this override.
            return ReadAudioTag(path).ToParsedTrackInfo(_configService.PreferPathAsin);
        }

        public AudioTag GetTrackMetadata(BookFile trackfile)
        {
            var edition = trackfile.Edition.Value;
            var book = edition.Book.Value;
            var author = book.Author.Value;
            var partCount = edition.BookFiles.Value.Count;

            var fileTags = ReadAudioTag(trackfile.Path);

            var cover = edition.Images.FirstOrDefault(x => x.CoverType == MediaCoverTypes.Cover);
            string imageFile = null;
            long imageSize = 0;
            if (cover != null)
            {
                imageFile = _mediaCoverService.GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);
                _logger.Trace($"Embedding: {imageFile}");
                var fileInfo = _diskProvider.GetFileInfo(imageFile);
                if (fileInfo.Exists)
                {
                    imageSize = fileInfo.Length;
                }
                else
                {
                    imageFile = null;
                }
            }

            return new AudioTag
            {
                Title = edition.Title,
                Performers = edition.Narrators?.Any() == true ? edition.Narrators.ToArray() : new[] { author.Name },
                BookAuthors = new[] { author.Name },
                Track = (uint)trackfile.Part,
                TrackCount = (uint)partCount,
                Book = book.Title,
                Disc = fileTags.Disc,
                DiscCount = fileTags.DiscCount,

                // We may have omitted media so index in the list isn't the same as medium number
                Media = fileTags.Media,
                Date = edition.ReleaseDate,
                Year = (uint)(edition.ReleaseDate?.Year ?? 0),
                OriginalReleaseDate = book.ReleaseDate,
                OriginalYear = (uint)(book.ReleaseDate?.Year ?? 0),
                Publisher = edition.Publisher,
                Genres = new string[0],
                ImageFile = imageFile,
                ImageSize = imageSize,
            };
        }

        private void UpdateTrackfileSizeAndModified(BookFile trackfile, string path)
        {
            // update the saved file size so that the importer doesn't get confused on the next scan
            var fileInfo = _diskProvider.GetFileInfo(path);
            trackfile.Size = fileInfo.Length;
            trackfile.Modified = fileInfo.LastWriteTimeUtc;

            if (trackfile.Id > 0)
            {
                _mediaFileService.Update(trackfile);
            }
        }

        public void RemoveAllTags(string path)
        {
            TagLib.File file = null;
            try
            {
                file = TagLib.File.Create(path);
                file.RemoveTags(TagLib.TagTypes.AllTags);
                file.Save();
            }
            catch (CorruptFileException ex)
            {
                _logger.Warn(ex, $"Tag removal failed for {path}.  File is corrupt");
            }
            catch (Exception ex)
            {
                _logger.ForWarnEvent()
                    .Exception(ex)
                    .Message($"Tag removal failed for {path}")
                    .Log();
            }
            finally
            {
                file?.Dispose();
            }
        }

        public void WriteTags(BookFile trackfile, bool newDownload, bool force = false)
        {
            if (!force)
            {
                if (_configService.WriteAudioTags == WriteAudioTagsType.No ||
                    (_configService.WriteAudioTags == WriteAudioTagsType.NewFiles && !newDownload))
                {
                    return;
                }
            }

            var path = trackfile.Path;
            var oldTags = ReadAudioTag(path);
            if (!AudioRecordingIdentifier.MatchesEdition(oldTags, path, trackfile.Edition?.Value))
            {
                _logger.Warn("Skipping audio tag update because the file's recording ASIN conflicts with the selected edition: {0}", path);
                return;
            }

            var newTags = GetTrackMetadata(trackfile);
            var diff = oldTags.Diff(newTags);

            if (!diff.Any())
            {
                _logger.Debug("No tags update for {0} due to no difference", trackfile);
                return;
            }

            _rootFolderWatchingService.ReportFileSystemChangeBeginning(path);

            if (_configService.ScrubAudioTags)
            {
                _logger.Debug($"Scrubbing tags for {trackfile}");
                RemoveAllTags(path);
            }

            _logger.Debug($"Writing tags for {trackfile}");

            newTags.Write(path);

            UpdateTrackfileSizeAndModified(trackfile, path);

            _eventAggregator.PublishEvent(new BookFileRetaggedEvent(trackfile.Author.Value, trackfile, diff, _configService.ScrubAudioTags));
        }

        public void SyncTags(List<Edition> editions)
        {
            if (_configService.WriteAudioTags != WriteAudioTagsType.Sync)
            {
                return;
            }

            // get the tracks to update
            foreach (var edition in editions)
            {
                var bookFiles = edition.BookFiles.Value;

                _logger.Debug($"Syncing audio tags for {bookFiles.Count} files");

                foreach (var file in bookFiles.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))))
                {
                    // populate tracks (which should also have release/book/author set) because
                    // not all of the updates will have been committed to the database yet
                    file.Edition = edition;
                    WriteTags(file, false);
                }
            }
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId)
        {
            var files = _mediaFileService.GetFilesByAuthor(authorId);

            return GetPreviews(files).OrderBy(b => b.BookId).ThenBy(b => b.Path).ToList();
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId)
        {
            var files = _mediaFileService.GetFilesByBook(bookId);

            return GetPreviews(files).OrderBy(b => b.BookId).ThenBy(b => b.Path).ToList();
        }

        private IEnumerable<RetagBookFilePreview> GetPreviews(List<BookFile> files)
        {
            foreach (var f in files.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))).OrderBy(x => x.Edition.Value.Title))
            {
                var file = f;

                if (f.Edition.Value == null)
                {
                    _logger.Warn($"File {f} is not linked to any books");
                    continue;
                }

                var oldTags = ReadAudioTag(f.Path);
                if (!AudioRecordingIdentifier.MatchesEdition(oldTags, f.Path, f.Edition.Value))
                {
                    _logger.Debug("Skipping retag preview for a conflicting recording ASIN: {0}", f.Path);
                    continue;
                }

                var newTags = GetTrackMetadata(f);
                var diff = oldTags.Diff(newTags);

                if (diff.Any())
                {
                    yield return new RetagBookFilePreview
                    {
                        AuthorId = file.Author.Value.Id,
                        BookId = file.Edition.Value.Id,
                        BookFileId = file.Id,
                        Path = file.Path,
                        Changes = diff
                    };
                }
            }
        }

        public void RetagFiles(RetagFilesCommand message)
        {
            var author = _authorService.GetAuthor(message.AuthorId);
            var bookFiles = _mediaFileService.Get(message.Files);
            var audioFiles = bookFiles.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))).ToList();

            _logger.ProgressInfo("Re-tagging {0} audio files for {1}", audioFiles.Count, author.Name);
            foreach (var file in audioFiles)
            {
                WriteTags(file, false, force: true);
            }

            _logger.ProgressInfo("Selected audio files re-tagged for {0}", author.Name);
        }

        public void RetagAuthor(RetagAuthorCommand message)
        {
            _logger.Debug("Re-tagging all audio files for selected authors");
            var authorToRename = _authorService.GetAuthors(message.AuthorIds);

            foreach (var author in authorToRename)
            {
                var bookFiles = _mediaFileService.GetFilesByAuthor(author.Id);
                var audioFiles = bookFiles.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))).ToList();

                _logger.ProgressInfo("Re-tagging all audio files for author: {0}", author.Name);
                foreach (var file in audioFiles)
                {
                    WriteTags(file, false, force: true);
                }

                _logger.ProgressInfo("All audio files re-tagged for {0}", author.Name);
            }
        }
    }
}
