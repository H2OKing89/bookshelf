using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public enum RejectedImportRecordResult
    {
        Changed,
        AlreadyFailed,
        Applied
    }

    public interface IRejectedImportRepository
    {
        RejectedImportRecordResult TryRecord(DownloadHistory grab, DownloadFailedEvent failure, RejectedImportAction action);
    }

    // The failure marker and blocklist are one commit. Event handlers cannot
    // acknowledge writes (their exceptions are caught), so record both before
    // announcing failure or allowing a retry search.
    public class RejectedImportRepository : IRejectedImportRepository
    {
        private readonly IMainDatabase _database;
        private readonly IEventAggregator _events;

        public RejectedImportRepository(IMainDatabase database, IEventAggregator events)
        {
            _database = database;
            _events = events;
        }

        public RejectedImportRecordResult TryRecord(DownloadHistory grab, DownloadFailedEvent failure, RejectedImportAction action)
        {
            using var connection = _database.OpenConnection();
            var postgres = _database.DatabaseType == DatabaseType.PostgreSQL;
            using var transaction = connection.BeginTransaction(postgres ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);

            // Recheck the actual saved policy, not a cached provider display.
            var indexer = connection.QuerySingleOrDefault<IndexerDefinition>(
                "SELECT \"Id\", \"EnableRss\", \"EnableAutomaticSearch\", \"EnableInteractiveSearch\", \"DangerousFileAction\", \"ExecutableFileAction\", \"UserRejectedExtensionAction\" FROM \"Indexers\" WHERE \"Id\" = @Id" + (postgres ? " FOR UPDATE" : string.Empty),
                new { Id = grab.IndexerId },
                transaction);
            if (indexer?.Enable != true || RejectedImportService.ActionFor(indexer, failure.ImportRejectionCode) != action || action == RejectedImportAction.Review)
            {
                return RejectedImportRecordResult.Changed;
            }

            if (postgres)
            {
                // A short write transaction excludes a simultaneous grab,
                // completion or rejection insert for this job. No remote I/O.
                connection.Execute("LOCK TABLE \"DownloadHistory\" IN SHARE ROW EXCLUSIVE MODE", transaction: transaction);
            }

            var scoped = connection.Query<DownloadHistory>("""
                SELECT "Id", "EventType", "Date" FROM "DownloadHistory"
                WHERE "DownloadId" = @DownloadId AND "DownloadClientId" = @DownloadClientId
                ORDER BY "Date" DESC, "Id" DESC
                """,
                new { grab.DownloadId, grab.DownloadClientId },
                transaction).ToList();
            var currentGrab = scoped.FirstOrDefault(row => row.EventType == DownloadHistoryEventType.DownloadGrabbed);
            if (currentGrab?.Id != grab.Id)
            {
                return RejectedImportRecordResult.Changed;
            }

            var newer = scoped.TakeWhile(row => row.Id != currentGrab.Id).ToList();
            if (newer.Any(row => row.EventType is DownloadHistoryEventType.FileImported or DownloadHistoryEventType.DownloadImported or DownloadHistoryEventType.DownloadIgnored or DownloadHistoryEventType.DownloadSourceRetained))
            {
                return RejectedImportRecordResult.Changed;
            }

            if (newer.Any(row => row.EventType == DownloadHistoryEventType.DownloadFailed))
            {
                return RejectedImportRecordResult.AlreadyFailed;
            }

            var now = DateTime.UtcNow;
            new BasicRepository<Blocklist>(_database, _events).Insert(connection, transaction, BlocklistService.CreateFailedDownloadBlocklist(failure));
            new BasicRepository<DownloadHistory>(_database, _events).Insert(connection, transaction, new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadFailed,
                AuthorId = grab.AuthorId,
                DownloadId = grab.DownloadId,
                SourceTitle = grab.SourceTitle,
                Date = now,
                Protocol = grab.Protocol,
                DownloadClientId = grab.DownloadClientId,
                IndexerId = grab.IndexerId,
                Data = new Dictionary<string, string>()
                {
                    ["importRejectionCode"] = failure.ImportRejectionCode,
                    ["importRejectionAction"] = action.ToString(),
                    ["grabId"] = grab.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["message"] = failure.Message
                }
            });
            transaction.Commit();
            return RejectedImportRecordResult.Applied;
        }
    }
}
