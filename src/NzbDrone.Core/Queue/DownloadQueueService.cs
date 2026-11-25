using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Queue
{
    public enum QueueDownloadKind
    {
        Download,
        Pending,
        Unaddressable
    }

    public enum QueueCoverageSource
    {
        RecordedGrab,
        PendingRelease,
        MappedCandidates,
        Unknown
    }

    public class QueueRecording
    {
        public int BookId { get; set; }
        public string RecordingId { get; set; }
        public string Title { get; set; }
        public string TitleSlug { get; set; }
        public DateTime? ReleaseDate { get; set; }
    }

    public class QueueDownload
    {
        public string Id { get; set; }
        public string Revision { get; set; }
        public QueueDownloadKind Kind { get; set; }
        public QueueCoverageSource CoverageSource { get; set; }
        public Queue Item { get; set; }
        public int? AuthorId { get; set; }
        public Author Author { get; set; }
        public List<QueueRecording> Recordings { get; set; }
        public List<EntityHistory> GrabbedItems { get; set; }
        public int? GrabRecordId { get; set; }
        public bool CanRemoveFromClient { get; set; }
        public bool CanChangeCategory { get; set; }
        public bool CanIgnore { get; set; }
        public bool CanBlocklist { get; set; }
        public bool CanRetry { get; set; }
        public bool CanGrab { get; set; }
        public bool CanImport { get; set; }
        public bool CanRemove => Kind == QueueDownloadKind.Pending || CanRemoveFromClient || CanChangeCategory || CanIgnore || CanBlocklist;
    }

    public interface IDownloadQueueService
    {
        List<QueueDownload> GetDownloads();
    }

    public class DownloadQueueService : IDownloadQueueService
    {
        private readonly IQueueService _queue;
        private readonly IPendingReleaseService _pending;
        private readonly IDownloadHistoryService _downloadHistory;
        private readonly IHistoryService _history;
        private readonly IBookRepository _books;
        private readonly IAuthorService _authors;

        public DownloadQueueService(IQueueService queue, IPendingReleaseService pending, IDownloadHistoryService downloadHistory, IHistoryService history, IBookRepository books, IAuthorService authors)
        {
            _queue = queue;
            _pending = pending;
            _downloadHistory = downloadHistory;
            _history = history;
            _books = books;
            _authors = authors;
        }

        public static string DownloadIdentity(int clientId, string downloadId) => $"download:{clientId}:{downloadId}";

        public static int[] MappedIds(Queue item) => item.RemoteBook?.Books?
            .Select(book => book.Id).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray() ?? Array.Empty<int>();

        public List<QueueDownload> GetDownloads()
        {
            // Both streams use the existing queue visibility rules. A pending
            // release has a persisted key, not a title/hash or inferred ASIN.
            var groups = _queue.GetQueue().Concat(_pending.GetPendingQueue())
                .Select((item, index) => new
                {
                    Item = item,
                    Id = item.PendingReleaseId is > 0
                        ? $"pending:{item.PendingReleaseId}"
                        : item.DownloadClientId is > 0 && !string.IsNullOrWhiteSpace(item.DownloadId)
                            ? DownloadIdentity(item.DownloadClientId.Value, item.DownloadId)
                            : $"unaddressable:{item.Id}:{index}"
                })
                .GroupBy(row => row.Id, StringComparer.Ordinal)
                .Select(group => Project(group.Key, group.Select(row => row.Item).ToList())).ToList();

            var books = groups.SelectMany(group => group.Recordings.Select(recording => recording.BookId)).Distinct().Chunk(500)
                .SelectMany(ids => _books.FindByIds(ids)).ToDictionary(book => book.Id);
            var authors = groups.Where(group => group.AuthorId is > 0).Select(group => group.AuthorId.Value).Distinct().Chunk(500)
                .SelectMany(ids => _authors.FindAuthors(ids)).ToDictionary(author => author.Id);

            foreach (var group in groups)
            {
                foreach (var recording in group.Recordings)
                {
                    if (books.TryGetValue(recording.BookId, out var book))
                    {
                        recording.Title = book.Title;
                        recording.TitleSlug = book.TitleSlug;
                        recording.ReleaseDate = book.ReleaseDate;
                        recording.RecordingId = book.ForeignBookId;
                    }
                }

                group.CanRetry = group.Kind == QueueDownloadKind.Download && group.CanBlocklist &&
                    group.Recordings.All(recording => books.ContainsKey(recording.BookId));
                group.Author = group.AuthorId.HasValue ? authors.GetValueOrDefault(group.AuthorId.Value) : null;

                // Include action-relevant provenance, but never progress, ETA,
                // or polling time. Confirmation survives ordinary progress.
                var revision = STJson.ToJson(new
                {
                    group.Id,
                    group.Kind,
                    group.CoverageSource,
                    group.AuthorId,
                    Recordings = group.Recordings.Select(r => new { r.BookId, r.RecordingId }),
                    group.Item.Title,
                    group.GrabRecordId,
                    IndexerId = group.Item.RemoteBook?.Release?.IndexerId,
                    ReleaseGuid = group.Item.RemoteBook?.Release?.Guid,
                    group.Item.OutputPath,
                    group.Item.DownloadCategory,
                    group.Item.Status,
                    group.Item.TrackedDownloadState,
                    group.Item.TrackedDownloadStatus,
                    group.Item.DownloadClientAvailable,
                    group.CanRemove,
                    group.CanRemoveFromClient,
                    group.CanChangeCategory,
                    group.CanBlocklist,
                    group.CanRetry,
                    group.CanIgnore,
                    group.CanGrab,
                    group.CanImport,
                    GrabIds = group.GrabbedItems.Select(grab => grab.Id)
                });
                group.Revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision)));
            }

            return groups;
        }

        private QueueDownload Project(string id, List<Queue> rows)
        {
            var item = rows[0];
            var kind = id.StartsWith("pending:", StringComparison.Ordinal) ? QueueDownloadKind.Pending
                : id.StartsWith("download:", StringComparison.Ordinal) ? QueueDownloadKind.Download : QueueDownloadKind.Unaddressable;

            // A conflicting snapshot must never grant action authority or claim
            // combined coverage just because a client reused its download ID.
            if (rows.Any(row => row.Title != item.Title || row.OutputPath != item.OutputPath ||
                                row.Status != item.Status || row.Author?.Id != item.Author?.Id ||
                                !MappedIds(row).SequenceEqual(MappedIds(item))))
            {
                kind = QueueDownloadKind.Unaddressable;
            }

            var result = new QueueDownload
            {
                Id = id,
                Kind = kind,
                Item = item,
                AuthorId = kind == QueueDownloadKind.Unaddressable ? null : item.Author?.Id,
                CoverageSource = QueueCoverageSource.Unknown,
                GrabbedItems = new List<EntityHistory>()
            };

            var ids = kind == QueueDownloadKind.Unaddressable ? Array.Empty<int>() : MappedIds(item);
            if (ids.Length > 0)
            {
                result.CoverageSource = QueueCoverageSource.MappedCandidates;
            }

            if (kind == QueueDownloadKind.Pending && ValidIds(item.PendingRecordedBookIds))
            {
                ids = item.PendingRecordedBookIds.Distinct().OrderBy(bookId => bookId).ToArray();
                result.CoverageSource = QueueCoverageSource.PendingRelease;
            }

            if (kind == QueueDownloadKind.Download)
            {
                var grab = _downloadHistory.GetLatestGrab(item.DownloadId, item.DownloadClientId.Value);
                result.GrabRecordId = grab?.Id;
                if (grab?.Data != null && grab.Data.TryGetValue("BookIds", out var data) && !string.IsNullOrWhiteSpace(data) && STJson.TryDeserialize<List<int>>(data, out var grabbedIds) && ValidIds(grabbedIds))
                {
                    ids = grabbedIds.Distinct().OrderBy(bookId => bookId).ToArray();
                    result.CoverageSource = QueueCoverageSource.RecordedGrab;
                    result.AuthorId = grab.AuthorId;

                    // Only the recorded release and exact set of local IDs can
                    // authorize a blocklist/retry, not a sibling or old grab.
                    var history = _history.Find(item.DownloadId, EntityHistoryEventType.Grabbed, item.DownloadClientId.Value);
                    var matching = history.Where(h => h.SourceTitle == grab.SourceTitle && h.AuthorId == grab.AuthorId)
                        .GroupBy(h => h.BookId)
                        .Select(group => group.OrderByDescending(h => h.Date).ThenByDescending(h => h.Id).First()).OrderBy(h => h.BookId).ToList();
                    if (matching.Select(h => h.BookId).SequenceEqual(ids))
                    {
                        result.GrabbedItems = matching;
                    }
                }
            }

            result.Recordings = ids.Select(bookId => new QueueRecording { BookId = bookId }).ToList();
            var exactMapping = ids.Length > 0 && result.AuthorId is > 0 && result.AuthorId == item.Author?.Id && ids.SequenceEqual(MappedIds(item));
            result.CanGrab = kind == QueueDownloadKind.Pending && exactMapping;
            result.CanBlocklist = kind == QueueDownloadKind.Pending ? exactMapping : !item.Removed && result.GrabbedItems.Count > 0;
            if (kind == QueueDownloadKind.Download && !item.Removed)
            {
                result.CanRemoveFromClient = item.DownloadClientAvailable;
                result.CanChangeCategory = item.DownloadClientAvailable && item.DownloadClientHasPostImportCategory;
                result.CanIgnore = exactMapping;
                result.CanImport = item.DownloadClientAvailable && item.Status == "Completed" && item.TrackedDownloadStatus == TrackedDownloadStatus.Warning;
            }

            return result;
        }

        private static bool ValidIds(List<int> ids) => ids is { Count: > 0 and <= 4096 } && ids.All(id => id > 0);
    }
}
