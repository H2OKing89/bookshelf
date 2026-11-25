using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using Readarr.Http;
using Readarr.Http.REST;
using NzbDroneClientException = NzbDrone.Core.Exceptions.NzbDroneClientException;

namespace Readarr.Api.V1.Queue
{
    [V2ApiController("queue")]
    public class DownloadQueueController : Controller
    {
        private static readonly HashSet<string> Statuses = Enum.GetNames<DownloadItemStatus>()
            .Concat(Enum.GetNames<PendingReleaseReason>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        private readonly IDownloadQueueService _queue;
        private readonly ITrackedDownloadService _tracked;
        private readonly IPendingReleaseService _pending;
        private readonly IProvideDownloadClient _clients;
        private readonly IFailedDownloadService _failed;
        private readonly IIgnoredDownloadService _ignored;
        private readonly IBlocklistService _blocklist;
        private readonly IDownloadService _downloads;
        private readonly QualityModelComparer _quality;

        public DownloadQueueController(IDownloadQueueService queue, ITrackedDownloadService tracked, IPendingReleaseService pending, IProvideDownloadClient clients, IFailedDownloadService failed, IIgnoredDownloadService ignored, IBlocklistService blocklist, IDownloadService downloads, QualityProfileService qualityProfiles)
        {
            _queue = queue;
            _tracked = tracked;
            _pending = pending;
            _clients = clients;
            _failed = failed;
            _ignored = ignored;
            _blocklist = blocklist;
            _downloads = downloads;
            _quality = new QualityModelComparer(qualityProfiles.GetDefaultProfile(string.Empty));
        }

        [HttpGet]
        [Produces("application/json")]
        public PagingResource<DownloadQueueResource> GetQueue([FromQuery] PagingRequestResource paging, bool includeUnknownAuthorItems = false, string status = null, DownloadProtocol? protocol = null)
        {
            if (paging.Page is < 1 || paging.PageSize is < 1 or > 500 ||
                (status != null && !Statuses.Contains(status)) || (protocol.HasValue && !Enum.IsDefined(protocol.Value)) ||
                (paging.SortDirection.HasValue && !Enum.IsDefined(paging.SortDirection.Value)))
            {
                throw new BadRequestException(new { message = "Invalid queue paging or filter." });
            }

            var result = new PagingResource<DownloadQueueResource>(paging)
            {
                SortKey = paging.SortKey ?? "timeleft",
                SortDirection = paging.SortDirection == SortDirection.Descending ? SortDirection.Descending : SortDirection.Ascending
            };
            var ascending = result.SortDirection == SortDirection.Ascending;
            var rows = _queue.GetDownloads()
                .Where(row => (includeUnknownAuthorItems || row.Author != null) &&
                    (status == null || string.Equals(row.Item.Status, status, StringComparison.OrdinalIgnoreCase)) &&
                    (!protocol.HasValue || protocol.Value == row.Item.Protocol)).ToList();

            IOrderedEnumerable<QueueDownload> Order<T>(Func<QueueDownload, T> key, IComparer<T> comparer = null) =>
                ascending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer);

            var sorted = result.SortKey switch
            {
                "status" => Order(row => row.Item.Status, StringComparer.OrdinalIgnoreCase),
                "authors.sortName" => Order(row => row.Author?.Metadata.Value.SortName ?? row.Item.Title, StringComparer.InvariantCultureIgnoreCase),
                "authors.sortNameLastFirst" => Order(row => row.Author?.Metadata.Value.SortNameLastFirst ?? string.Empty, StringComparer.InvariantCultureIgnoreCase),
                "title" => Order(row => row.Item.Title, StringComparer.InvariantCultureIgnoreCase),
                "book" or "book.title" => Order(row => row.Recordings.Select(recording => recording.Title).Where(title => title != null).OrderBy(title => title, StringComparer.InvariantCultureIgnoreCase).FirstOrDefault(), StringComparer.InvariantCultureIgnoreCase),
                "book.releaseDate" => Order(row => row.Recordings.Min(recording => recording.ReleaseDate)),
                "size" => Order(row => row.Item.Size),
                "progress" => Order(row => row.Item.Size > 0 ? 100 - ((row.Item.Sizeleft / row.Item.Size) * 100) : 0),
                "timeleft" => Order(row => row.Item.Timeleft, new TimeleftComparer()),
                "estimatedCompletionTime" => Order(row => row.Item.EstimatedCompletionTime, new EstimatedCompletionTimeComparer()),
                "protocol" => Order(row => row.Item.Protocol),
                "indexer" => Order(row => row.Item.Indexer, StringComparer.InvariantCultureIgnoreCase),
                "downloadClient" => Order(row => row.Item.DownloadClient, StringComparer.InvariantCultureIgnoreCase),
                "quality" => Order(row => row.Item.Quality, _quality),
                "customFormatScore" => Order(row => DownloadQueueResource.From(row).CustomFormatScore),
                "outputPath" => Order(row => row.Item.OutputPath, StringComparer.InvariantCultureIgnoreCase),
                _ => throw new BadRequestException(new { message = "Invalid queue sort key." })
            };

            result.TotalRecords = rows.Count;
            result.Page = Math.Min(result.Page, Math.Max(1, (int)Math.Ceiling((decimal)rows.Count / result.PageSize)));
            result.Records = sorted.ThenBy(row => row.Id, StringComparer.Ordinal)
                .Skip((result.Page - 1) * result.PageSize).Take(result.PageSize).Select(DownloadQueueResource.From).ToList();
            return result;
        }

        [HttpGet("status")]
        public QueueStatusResource GetStatus()
        {
            var rows = _queue.GetDownloads();
            return new QueueStatusResource
            {
                TotalCount = rows.Count,
                Count = rows.Count(row => row.Author != null),
                UnknownCount = rows.Count(row => row.Author == null),
                Errors = rows.Any(row => row.Author != null && row.Item.TrackedDownloadStatus == TrackedDownloadStatus.Error),
                Warnings = rows.Any(row => row.Author != null && row.Item.TrackedDownloadStatus == TrackedDownloadStatus.Warning),
                UnknownErrors = rows.Any(row => row.Author == null && row.Item.TrackedDownloadStatus == TrackedDownloadStatus.Error),
                UnknownWarnings = rows.Any(row => row.Author == null && row.Item.TrackedDownloadStatus == TrackedDownloadStatus.Warning)
            };
        }

        [HttpPost("grab")]
        [Consumes("application/json")]
        [ProducesResponseType(204)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> Grab([FromBody] DownloadQueueActionRequest request)
        {
            var rows = Resolve(request);
            if (rows.Any(row => !row.CanGrab))
            {
                throw StaleSelection();
            }

            var budget = new DownloadSpaceBudget();
            foreach (var row in rows)
            {
                await _downloads.DownloadReport(row.Item.RemoteBook, null, budget);
            }

            return NoContent();
        }

        [HttpPost("remove")]
        [Consumes("application/json")]
        [ProducesResponseType(204)]
        [ProducesResponseType(409)]
        public IActionResult Remove([FromBody] DownloadQueueRemoveRequest request)
        {
            var rows = Resolve(request);
            if ((request.RemoveFromClient && request.ChangeCategory) || (request.SkipRedownload && !request.Blocklist))
            {
                throw new BadRequestException(new { message = "Invalid removal options." });
            }

            // Preflight every target before changing any external client. The
            // submitted identities can never select an inferred sibling/client.
            var actions = rows.Select(row =>
            {
                if (!row.CanRemove || (request.Blocklist && !row.CanBlocklist) ||
                    (row.Kind == QueueDownloadKind.Download && request.Blocklist && !request.SkipRedownload && !row.CanRetry))
                {
                    throw StaleSelection();
                }

                if (row.Kind == QueueDownloadKind.Pending)
                {
                    return (Row: row, Tracked: (TrackedDownload)null, Client: (IDownloadClient)null);
                }

                if ((request.RemoveFromClient && !row.CanRemoveFromClient) || (request.ChangeCategory && !row.CanChangeCategory) ||
                    (!request.RemoveFromClient && !request.ChangeCategory && !request.Blocklist && !row.CanIgnore))
                {
                    throw StaleSelection();
                }

                var item = row.Item;
                var tracked = _tracked.Find(item.DownloadId, item.DownloadClientId.Value);
                if (tracked == null || tracked.DownloadClient != item.DownloadClientId ||
                    tracked.DownloadItem.DownloadId != item.DownloadId || tracked.DownloadItem.Removed ||
                    tracked.DownloadItem.Status.ToString() != item.Status || tracked.State != item.TrackedDownloadState ||
                    tracked.Status != item.TrackedDownloadStatus || tracked.IsClientAvailable != item.DownloadClientAvailable ||
                    tracked.DownloadItem.Category != item.DownloadCategory ||
                    tracked.DownloadItem.OutputPath.ToString() != item.OutputPath ||
                    NzbDrone.Core.Parser.Parser.RemoveFileExtension(tracked.DownloadItem.Title) != item.Title ||
                    tracked.RemoteBook?.Author?.Id != item.Author?.Id ||
                    !DownloadQueueService.MappedIds(new NzbDrone.Core.Queue.Queue { RemoteBook = tracked.RemoteBook }).SequenceEqual(DownloadQueueService.MappedIds(item)))
                {
                    throw StaleSelection();
                }

                var client = request.RemoveFromClient || request.ChangeCategory ? _clients.Get(tracked.DownloadClient) : null;
                if ((request.RemoveFromClient || request.ChangeCategory) && client == null)
                {
                    throw StaleSelection();
                }

                return (Row: row, Tracked: tracked, Client: client);
            }).ToList();

            foreach (var action in actions)
            {
                var row = action.Row;
                var tracked = action.Tracked;
                if (row.Kind == QueueDownloadKind.Pending)
                {
                    if (request.Blocklist)
                    {
                        _blocklist.Block(row.Item.RemoteBook, "Pending release manually blocklisted");
                    }

                    _pending.RemovePendingQueueRelease(row.Item.PendingReleaseId.Value);
                    continue;
                }

                if (request.RemoveFromClient)
                {
                    action.Client.RemoveItem(tracked.DownloadItem, true);
                }
                else if (request.ChangeCategory)
                {
                    action.Client.MarkItemAsImported(tracked.DownloadItem);
                }

                if (request.Blocklist)
                {
                    _failed.BlockQueueDownload(row.GrabbedItems, tracked, request.SkipRedownload);
                }
                else if (!request.RemoveFromClient && !request.ChangeCategory && !_ignored.IgnoreDownload(tracked))
                {
                    throw StaleSelection();
                }

                _tracked.StopTracking(tracked.DownloadItem.DownloadId, tracked.DownloadClient);
            }

            return NoContent();
        }

        private List<QueueDownload> Resolve(DownloadQueueActionRequest request)
        {
            if (request?.Targets is not { Count: > 0 and <= 500 } ||
                request.Targets.Any(target => target == null || string.IsNullOrEmpty(target.Id) || string.IsNullOrEmpty(target.Revision)) ||
                request.Targets.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count() != request.Targets.Count)
            {
                throw new BadRequestException(new { message = "Invalid queue targets." });
            }

            var current = _queue.GetDownloads().ToDictionary(row => row.Id, StringComparer.Ordinal);
            return request.Targets.Select(target =>
            {
                if (!current.TryGetValue(target.Id, out var row) || row.Revision != target.Revision)
                {
                    throw StaleSelection();
                }

                return row;
            }).ToList();
        }

        private static NzbDroneClientException StaleSelection() => new NzbDroneClientException(HttpStatusCode.Conflict, "Queue selection has changed or the action is no longer available. Refresh and review the current download.");
    }
}
