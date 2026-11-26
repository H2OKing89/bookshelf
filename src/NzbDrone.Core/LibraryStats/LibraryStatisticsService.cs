using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using Dapper;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.LibraryStats
{
    public interface ILibraryStatisticsService
    {
        LibraryStatistics GetStatistics(int? authorId = null, string media = "all", string source = "all");
    }

    public class LibraryStatisticsService : ILibraryStatisticsService
    {
        private const string Unknown = "Unknown";
        private readonly IMainDatabase _database;

        public LibraryStatisticsService(IMainDatabase database)
        {
            _database = database;
        }

        public LibraryStatistics GetStatistics(int? authorId = null, string media = "all", string source = "all")
        {
            if (authorId <= 0 || !(media is "all" or "audiobook" or "ebook") || !(source is "all" or "audible" or "local" or "other"))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Invalid library statistics filter.");
            }

            // One consistent snapshot, with narrowly projected rows. Never join files
            // to work memberships or read media from disk while counting the catalog.
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.SQLite ? IsolationLevel.ReadCommitted : IsolationLevel.RepeatableRead);
            var authors = connection.Query<AuthorRow>(
                """
                SELECT a."Id", a."AuthorMetadataId", m."Name"
                FROM "Authors" a LEFT JOIN "AuthorMetadata" m ON m."Id" = a."AuthorMetadataId"
                """,
                transaction: transaction).ToList();
            var books = connection.Query<BookRow>("SELECT \"Id\", \"AuthorMetadataId\" FROM \"Books\"", transaction: transaction).ToDictionary(book => book.Id);
            var metadataIds = authors.Where(author => !authorId.HasValue || author.Id == authorId).Select(author => author.AuthorMetadataId).ToHashSet();
            var filteredBooks = books.Values.Where(book => !authorId.HasValue || metadataIds.Contains(book.AuthorMetadataId)).Select(book => book.Id).ToHashSet();
            var result = new LibraryStatistics
            {
                Authors = authors.OrderBy(author => author.Name, StringComparer.OrdinalIgnoreCase).ThenBy(author => author.Id)
                    .Select(author => new LibraryStatisticsAuthor { Id = author.Id, Name = Clean(author.Name) }).ToList()
            };

            var editions = connection.Query<Edition>(
                """
                SELECT "Id", "BookId", "ForeignEditionId", "Asin", "AudibleRegion", COALESCE("IsEbook", FALSE) AS "IsEbook", "Narrators", "Language"
                FROM "Editions"
                """,
                transaction: transaction).Where(edition => books.ContainsKey(edition.BookId)).ToDictionary(edition => edition.Id);
            var selected = editions.Values.Where(edition => filteredBooks.Contains(edition.BookId) &&
                (media == "all" || media == (edition.IsEbook ? "ebook" : "audiobook")) &&
                (source == "all" || source == Source(edition))).ToDictionary(edition => edition.Id);
            var hasRecordingFilter = media != "all" || source != "all";
            var selectedBooks = hasRecordingFilter ? selected.Values.Select(edition => edition.BookId).ToHashSet() : filteredBooks;
            var selectedMetadata = selectedBooks.Select(id => books[id].AuthorMetadataId).ToHashSet();
            result.BookCount = selectedBooks.Count;
            result.AuthorCount = authors.Count(author => (!authorId.HasValue || author.Id == authorId) && (!hasRecordingFilter || selectedMetadata.Contains(author.AuthorMetadataId)));
            result.AudioRecordingCount = selected.Values.Count(edition => !edition.IsEbook);
            result.EbookEditionCount = selected.Count - result.AudioRecordingCount;

            var owned = new HashSet<int>();
            var codecs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bitrates = new Dictionary<string, int>();
            var mediaInfo = new Dictionary<string, int>();
            var audioExtensions = MediaFileExtensions.AudioExtensions;
            var textExtensions = MediaFileExtensions.TextExtensions;

            // Each BookFiles row is visited exactly once. A missing edition or book is
            // an unresolved association, never assigned by filename or author guess.
            foreach (var file in connection.Query<BookFile>(
                """SELECT "Id", "EditionId", "Path", "Size", "Modified", "MediaInfo" FROM "BookFiles" """,
                transaction: transaction,
                buffered: false))
            {
                var extension = Path.GetExtension(file.Path) ?? string.Empty;
                var isAudio = audioExtensions.Contains(extension);
                var isEbook = textExtensions.Contains(extension);
                var mapped = editions.ContainsKey(file.EditionId);
                if (mapped)
                {
                    if (!selected.ContainsKey(file.EditionId))
                    {
                        continue;
                    }

                    owned.Add(file.EditionId);
                }
                else if (authorId.HasValue || source != "all" || (media == "audiobook" && !isAudio) || (media == "ebook" && !isEbook))
                {
                    continue;
                }

                result.FileCount++;
                var size = Math.Max(0, file.Size);
                result.SizeOnDisk += size;
                if (mapped)
                {
                    result.MappedFileCount++;
                    result.MappedSizeOnDisk += size;
                }
                else
                {
                    result.UnresolvedFileCount++;
                    result.UnresolvedSizeOnDisk += size;
                }

                if (isAudio)
                {
                    result.AudioFileCount++;
                    var status = MediaInfoAnalysis.GetStatus(file.MediaInfo, file.Size, file.Modified) ?? "missing";
                    Count(mediaInfo, status);

                    // Stale or merely advertised evidence must not masquerade as a
                    // measured current duration or codec. Count measured file time,
                    // never edition runtime or the sum of parallel audio streams.
                    var measured = status is "current" or "partial" ? file.MediaInfo : null;
                    Count(codecs, MediaInfoFormatter.FormatAudioCodec(measured));
                    Count(bitrates, Bitrate(measured?.AudioBitrate ?? 0));
                    var duration = measured?.GetFileDurationSeconds();
                    if (duration is > 0 && double.IsFinite(duration.Value))
                    {
                        result.KnownAudioDurationSeconds = (result.KnownAudioDurationSeconds ?? 0) + duration.Value;
                        result.AudioFilesWithKnownDuration++;
                    }
                }
                else if (isEbook)
                {
                    result.EbookFileCount++;
                }
                else
                {
                    result.OtherFileCount++;
                }
            }

            result.OwnedAudioRecordingCount = owned.Count(id => !selected[id].IsEbook);
            result.OwnedEbookEditionCount = owned.Count - result.OwnedAudioRecordingCount;
            var sources = new Dictionary<string, int>();
            var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var narrators = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var edition in selected.Values.Where(edition => !edition.IsEbook))
            {
                Count(sources, Source(edition));
                foreach (var language in RecordingLanguageEvidence.Parse(edition.Language).Select(language => language.Name).DefaultIfEmpty(Unknown))
                {
                    Count(languages, language);
                }

                foreach (var narrator in (edition.Narrators ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean)
                    .Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty(Unknown))
                {
                    Count(narrators, narrator);
                }
            }

            var confirmedWorks = new HashSet<int>();
            var ownedWorks = new HashSet<int>();
            foreach (var membership in connection.Query<RecordingWorkMembership>(
                """
                SELECT m."EditionId", m."WorkId", m."RecordingIdentity" FROM "RecordingWorkMemberships" m
                INNER JOIN "RecordingWorks" w ON w."Id" = m."WorkId"
                WHERE m."Confirmed" = TRUE
                """,
                transaction: transaction,
                buffered: false))
            {
                if (selected.TryGetValue(membership.EditionId, out var edition) && !edition.IsEbook &&
                    membership.RecordingIdentity == RecordingWorkRepository.Identity(edition.ForeignEditionId, edition.Asin, edition.AudibleRegion))
                {
                    confirmedWorks.Add(membership.WorkId);
                    if (owned.Contains(edition.Id))
                    {
                        ownedWorks.Add(membership.WorkId);
                    }
                }
            }

            result.ConfirmedWorkCount = confirmedWorks.Count;
            result.OwnedConfirmedWorkCount = ownedWorks.Count;
            result.Codecs = Groups(codecs);
            result.Bitrates = Groups(bitrates);
            result.Languages = Groups(languages);
            result.Narrators = Groups(narrators);
            result.Sources = Groups(sources);
            result.MediaInfoStatus = Groups(mediaInfo);
            return result;
        }

        private static string Source(Edition edition)
        {
            return LocalRecordingId.IsLocal(edition.ForeignEditionId) ? "local" : AudiobookId.IsNative(edition.ForeignEditionId) ? "audible" : "other";
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();

        private static string Bitrate(int kbps) => kbps switch
        {
            <= 0 => Unknown,
            < 64 => "Under 64 kbps",
            < 96 => "64–95 kbps",
            < 128 => "96–127 kbps",
            < 192 => "128–191 kbps",
            < 256 => "192–255 kbps",
            < 320 => "256–319 kbps",
            _ => "320 kbps and above"
        };

        private static void Count(Dictionary<string, int> groups, string key)
        {
            groups[key] = groups.GetValueOrDefault(key) + 1;
        }

        private static List<LibraryStatisticsGroup> Groups(Dictionary<string, int> groups)
        {
            return groups.OrderByDescending(group => group.Value).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new LibraryStatisticsGroup { Label = group.Key, Count = group.Value }).ToList();
        }

        private class AuthorRow : LibraryStatisticsAuthor
        {
            public int AuthorMetadataId { get; set; }
        }

        private class BookRow
        {
            public int Id { get; set; }
            public int AuthorMetadataId { get; set; }
        }
    }
}
