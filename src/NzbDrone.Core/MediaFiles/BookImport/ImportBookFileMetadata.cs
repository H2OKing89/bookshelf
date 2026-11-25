using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    // Keep intended-name scoring and staged publication on the same acquisition metadata.
    public static class ImportBookFileMetadata
    {
        public static BookFile Create(LocalBook localBook, DownloadClientItem downloadClientItem, IHistoryService historyService)
        {
            var file = new BookFile
            {
                Path = localBook.Path.CleanFilePath(),
                CalibreId = localBook.CalibreId,
                Part = localBook.Part,
                PartCount = localBook.PartCount,
                Size = localBook.Size,
                Modified = localBook.Modified,
                DateAdded = DateTime.UtcNow,
                ReleaseGroup = localBook.ReleaseGroupOverride ?? localBook.ReleaseGroup,
                ReleaseGroupEvidence = localBook.ReleaseGroupOverride == null
                    ? localBook.ReleaseGroupEvidence
                    : ReleaseGroupEvidence.ManualChoice(localBook.ReleaseGroupEvidence, localBook.ReleaseGroupOverride),
                Quality = localBook.Quality,
                MediaInfo = localBook.FileTrackInfo?.MediaInfo,
                TaggedLanguage = localBook.FileTrackInfo?.Language,
                EditionId = localBook.Edition.Id,
                Author = localBook.Author,
                Edition = localBook.Edition
            };

            if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true && downloadClientItem.DownloadClientInfo?.Id > 0)
            {
                var grab = (historyService.FindByDownloadId(downloadClientItem.DownloadId, downloadClientItem.DownloadClientInfo.Id) ?? new List<EntityHistory>())
                    .OrderByDescending(row => row.Date).FirstOrDefault(row => row.EventType == EntityHistoryEventType.Grabbed);
                if (Enum.TryParse(grab?.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                {
                    file.IndexerFlags = flags;
                }
            }
            else
            {
                file.IndexerFlags = localBook.IndexerFlags;
            }

            if (!localBook.ExistingFile && downloadClientItem != null)
            {
                var title = Parser.Parser.RemoveFileExtension(downloadClientItem.Title);
                if (Parser.Parser.ParseBookTitle(title) != null)
                {
                    file.SceneName = title;
                }
            }

            return file;
        }
    }
}
