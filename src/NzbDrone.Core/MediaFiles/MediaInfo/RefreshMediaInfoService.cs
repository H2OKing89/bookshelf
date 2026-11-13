using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public class RefreshMediaInfoService : IExecute<RefreshMediaInfoCommand>
    {
        private const int BatchSize = 100;

        private readonly IMediaFileService _mediaFileService;
        private readonly IAudioTagService _audioTagService;
        private readonly Logger _logger;

        public RefreshMediaInfoService(IMediaFileService mediaFileService, IAudioTagService audioTagService, Logger logger)
        {
            _mediaFileService = mediaFileService;
            _audioTagService = audioTagService;
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
            var audioExtensions = MediaFileExtensions.AudioExtensions;
            var files = knownFiles.Where(file => audioExtensions.Contains(Path.GetExtension(file.Path))).ToList();
            var pendingUpdates = new List<BookFile>();
            var refreshed = 0;
            var retained = 0;

            _logger.ProgressInfo("Refreshing media info for {0} audio files", files.Count);

            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];

                try
                {
                    // Use the same fresh TagLib fallback as imports for partial native probes.
                    // A failed native probe must not replace previously stored rich metadata.
                    var mediaInfo = _audioTagService.ReadAudioTag(file.Path)?.MediaInfo;
                    if (mediaInfo?.Source != "MediaInfo")
                    {
                        retained++;
                    }
                    else
                    {
                        file.MediaInfo = mediaInfo;
                        pendingUpdates.Add(file);
                        refreshed++;
                    }
                }
                catch (Exception ex)
                {
                    retained++;
                    _logger.Debug("Could not refresh media info for file {0} ({1}); retaining existing media info", file.Id, ex.GetType().Name);
                }

                if (pendingUpdates.Count == BatchSize)
                {
                    _mediaFileService.UpdateMediaInfo(pendingUpdates);
                    pendingUpdates = new List<BookFile>();
                }

                if ((index + 1) % BatchSize == 0 || index == files.Count - 1)
                {
                    _logger.ProgressInfo("Inspected {0} of {1} audio files: refreshed {2}, retained {3}", index + 1, files.Count, refreshed, retained);
                }
            }

            if (pendingUpdates.Count > 0)
            {
                _mediaFileService.UpdateMediaInfo(pendingUpdates);
            }

            _logger.ProgressInfo("Media info refresh completed: refreshed {0}, retained {1}, skipped {2} non-audio files", refreshed, retained, knownFiles.Count - files.Count);
        }
    }
}
