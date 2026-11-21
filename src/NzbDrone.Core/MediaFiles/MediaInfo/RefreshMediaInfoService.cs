using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public class RefreshMediaInfoService : IExecute<RefreshMediaInfoCommand>, IExecute<RefreshStaleMediaInfoCommand>
    {
        private const int BatchSize = 100;
        private const int AutomaticLimit = 30;

        private readonly IMediaFileService _mediaFileService;
        private readonly IAudioTagService _audioTagService;
        private readonly IMediaInfoProbe _mediaInfoProbe;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public RefreshMediaInfoService(IMediaFileService mediaFileService, IAudioTagService audioTagService, IMediaInfoProbe mediaInfoProbe, IDiskProvider diskProvider, Logger logger)
        {
            _mediaFileService = mediaFileService;
            _audioTagService = audioTagService;
            _mediaInfoProbe = mediaInfoProbe;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(RefreshMediaInfoCommand message)
        {
            if (message.AuthorId.HasValue && message.BookId.HasValue)
            {
                throw new ArgumentException("Specify an author or a book for media info refresh, or omit both to refresh all files");
            }

            if (message.AuthorId <= 0 || message.BookId <= 0)
            {
                throw new ArgumentException("Media info refresh IDs must be positive");
            }

            var knownFiles = message.BookId.HasValue
                ? _mediaFileService.GetFilesByBook(message.BookId.Value)
                : message.AuthorId.HasValue
                    ? _mediaFileService.GetFilesByAuthor(message.AuthorId.Value)
                    : _mediaFileService.GetFiles();
            Refresh(knownFiles, true);
        }

        public void Execute(RefreshStaleMediaInfoCommand message)
        {
            if (_mediaInfoProbe.BundleId != null)
            {
                Refresh(_mediaFileService.GetFiles(), false);
            }
        }

        private void Refresh(List<BookFile> knownFiles, bool force)
        {
            var bundleId = _mediaInfoProbe.BundleId;
            var files = knownFiles.Where(file => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(file.Path)))
                .OrderBy(file => file.MediaInfo?.LastAnalysisAttempt?.AttemptedAtUtc).ThenBy(file => file.Id).ToList();
            var pendingUpdates = new List<BookFile>();
            var inspected = 0;
            var refreshed = 0;
            var retained = 0;

            if (force)
            {
                _logger.ProgressInfo("Refreshing media info for {0} audio files", files.Count);
            }

            foreach (var file in files)
            {
                if (!force && inspected >= AutomaticLimit)
                {
                    break;
                }

                var now = DateTime.UtcNow;
                var savedState = MediaInfoAnalysis.CreateStamp(file.Size, file.Modified, bundleId);
                if (!force && savedState != null && !MediaInfoAnalysis.ShouldProbe(file.MediaInfo, savedState, now))
                {
                    // The regular library scan updates the saved file state. Avoid
                    // stat walks over every healthy file on an hourly maintenance run.
                    continue;
                }

                var before = MediaInfoAnalysis.Capture(_diskProvider, file.Path, bundleId);
                if (before == null || (!force && !MediaInfoAnalysis.ShouldProbe(file.MediaInfo, before, now)))
                {
                    continue;
                }

                inspected++;
                var read = Read(file);
                var after = MediaInfoAnalysis.Capture(_diskProvider, file.Path, bundleId);
                if (!MediaInfoAnalysis.SameFile(before, after))
                {
                    retained++;
                    continue;
                }

                if (read != null && read.Analysis == null && read.LastAnalysisAttempt == null)
                {
                    MediaInfoAnalysis.Stamp(read, before, now);
                }

                if (read?.Analysis != null && !MediaInfoAnalysis.SameFile(read.Analysis, before))
                {
                    read = null;
                }

                var merged = MediaInfoAnalysis.PreserveUseful(file, read, before, now);
                file.MediaInfo = merged;
                pendingUpdates.Add(file);
                if (ReferenceEquals(merged, read) && merged.Analysis?.Complete == true)
                {
                    refreshed++;
                }
                else
                {
                    retained++;
                }

                if (pendingUpdates.Count == BatchSize)
                {
                    _mediaFileService.UpdateMediaInfo(pendingUpdates);
                    pendingUpdates = new List<BookFile>();
                    if (force)
                    {
                        _logger.ProgressInfo("Inspected {0} of {1} audio files: refreshed {2}, retained {3}", inspected, files.Count, refreshed, retained);
                    }
                }
            }

            if (pendingUpdates.Count > 0)
            {
                _mediaFileService.UpdateMediaInfo(pendingUpdates);
            }

            if (force)
            {
                _logger.ProgressInfo("Media info refresh completed: refreshed {0}, retained {1}, skipped {2} non-audio files", refreshed, retained, knownFiles.Count - files.Count);
            }
            else if (inspected > 0)
            {
                _logger.Debug("Scheduled media info refresh inspected {0} files: refreshed {1}, retained {2}", inspected, refreshed, retained);
            }
        }

        private MediaInfoModel Read(BookFile file)
        {
            try
            {
                return _audioTagService.ReadAudioTag(file.Path)?.MediaInfo;
            }
            catch (Exception ex)
            {
                _logger.Debug("Could not refresh media info for file {0} ({1}); retaining existing media info", file.Id, ex.GetType().Name);
                return null;
            }
        }
    }
}
