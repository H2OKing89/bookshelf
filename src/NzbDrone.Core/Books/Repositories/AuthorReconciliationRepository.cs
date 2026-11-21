using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.Books
{
    public interface IAuthorReconciliationRepository
    {
        List<AuthorReconciliationCandidate> Candidates(int sourceAuthorId);
        AuthorReconciliationPreview Preview(AuthorReconciliationRequest request);
        void Apply(AuthorReconciliationConfirmation confirmation);
    }

    public class AuthorReconciliationRepository : IAuthorReconciliationRepository
    {
        private const string Profiles = """
            SELECT a."Id", a."AuthorMetadataId", a."Path", a."QualityProfileId", a."MetadataProfileId",
                a."Monitored", a."MonitorNewItems", a."Tags", m."Name", m."TitleSlug", m."ForeignAuthorId"
            FROM "Authors" a INNER JOIN "AuthorMetadata" m ON m."Id" = a."AuthorMetadataId"
            """;

        private static readonly string[] AuthorReferenceTables =
        {
            "Blocklist", "DownloadHistory", "ExtraFiles", "History", "MetadataFiles", "PendingReleases"
        };

        private readonly IMainDatabase _database;
        private readonly IEventAggregator _events;
        private readonly ITrackedDownloadService _trackedDownloads;

        public AuthorReconciliationRepository(IMainDatabase database, IEventAggregator events, ITrackedDownloadService trackedDownloads)
        {
            _database = database;
            _events = events;
            _trackedDownloads = trackedDownloads;
        }

        public List<AuthorReconciliationCandidate> Candidates(int sourceAuthorId)
        {
            using var connection = _database.OpenConnection();
            var source = GetProfile(connection, null, sourceAuthorId);
            if (!TryIdentity(source.ForeignAuthorId, out var identity) || !identity.IsName)
            {
                return new List<AuthorReconciliationCandidate>();
            }

            var matches = MatchingIdentities(connection, null, identity.Region, identity.Value).ToArray();
            var predicate = _database.DatabaseType == DatabaseType.PostgreSQL ? "= ANY(@Matches)" : "IN @Matches";
            var sql = Profiles + $" WHERE m.\"ForeignAuthorId\" {predicate} ORDER BY m.\"Name\", a.\"Id\"";
            return connection.Query<AuthorReconciliationCandidate>(sql, new { Matches = matches }).AsList();
        }

        public AuthorReconciliationPreview Preview(AuthorReconciliationRequest request)
        {
            ValidateRequest(request);
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            return ReadSnapshot(connection, transaction, request).Preview;
        }

        public void Apply(AuthorReconciliationConfirmation confirmation)
        {
            ValidateRequest(confirmation);
            var databaseType = _database.DatabaseType;
            var overrideRepository = new BasicRepository<AudiobookMetadataOverride>(_database, _events);
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            if (databaseType == DatabaseType.PostgreSQL)
            {
                // Writers on either side of this merge must not add references
                // to a disappearing author between the snapshot and deletion.
                connection.Execute("""
                    LOCK TABLE "AuthorMetadata", "Authors", "AudiobookMetadataOverrides", "Blocklist", "BookFiles", "Books",
                        "DownloadHistory", "Editions", "ExtraFiles", "History", "MetadataFiles", "PendingReleases"
                    IN SHARE ROW EXCLUSIVE MODE
                    """, transaction: transaction);
            }

            var snapshot = ReadSnapshot(connection, transaction, confirmation);
            var preview = snapshot.Preview;
            preview.ValidateConfirmation(confirmation);

            var source = preview.Source;
            var target = preview.Target;
            var affected = new { SourceId = source.Id, TargetId = target.Id, SourceMetadataId = source.AuthorMetadataId, TargetMetadataId = target.AuthorMetadataId };
            var movedBooks = connection.Execute(
                """UPDATE "Books" SET "AuthorMetadataId" = @TargetMetadataId WHERE "AuthorMetadataId" = @SourceMetadataId""",
                affected,
                transaction);
            RequireCount(movedBooks, preview.Recordings.Select(recording => recording.BookId).Distinct().Count());
            foreach (var reference in preview.References)
            {
                var moved = connection.Execute(
                    $"UPDATE \"{reference.Table}\" SET \"AuthorId\" = @TargetId WHERE \"AuthorId\" = @SourceId", affected, transaction);
                RequireCount(moved, reference.Count);
            }

            foreach (var receipt in snapshot.Receipts)
            {
                var rebound = connection.Execute(
                    """UPDATE "DownloadHistory" SET "Data" = @Data WHERE "Id" = @Id AND "AuthorId" = @AuthorId""",
                    new { receipt.Id, Data = STJson.ToJson(receipt.Data), AuthorId = target.Id },
                    transaction);
                RequireCount(rebound, 1);
            }

            var mapping = snapshot.Override;
            mapping.UpdatedAt = DateTime.UtcNow;
            if (mapping.Id == 0)
            {
                overrideRepository.Insert(connection, transaction, mapping);
            }
            else
            {
                var updated = connection.Execute(
                    """UPDATE "AudiobookMetadataOverrides" SET "Aliases" = @Aliases, "Reason" = @Reason, "UpdatedAt" = @UpdatedAt WHERE "Id" = @Id""",
                    new { mapping.Id, mapping.Reason, mapping.UpdatedAt, Aliases = STJson.ToJson(mapping.Aliases) },
                    transaction);
                RequireCount(updated, 1);
            }

            RequireCount(connection.Execute("""DELETE FROM "Authors" WHERE "Id" = @SourceId AND "AuthorMetadataId" = @SourceMetadataId""", affected, transaction), 1);
            RequireCount(connection.Execute("""DELETE FROM "AuthorMetadata" WHERE "Id" = @SourceMetadataId""", affected, transaction), 1);
            transaction.Commit();
        }

        private Snapshot ReadSnapshot(IDbConnection connection, IDbTransaction transaction, AuthorReconciliationRequest request)
        {
            var source = GetProfile(connection, transaction, request.SourceAuthorId);
            var target = GetProfile(connection, transaction, request.TargetAuthorId);
            if (!TryIdentity(source.ForeignAuthorId, out var sourceIdentity) || !sourceIdentity.IsName ||
                !TryIdentity(target.ForeignAuthorId, out var targetIdentity) || targetIdentity.IsName ||
                targetIdentity.Region != sourceIdentity.Region)
            {
                throw Conflict("Choose a name-only Audible author and a saved canonical ASIN in the same marketplace.");
            }

            var mappings = ReadOverrides(connection, transaction, sourceIdentity.Region);
            var matches = MatchingIdentities(connection, transaction, sourceIdentity.Region, sourceIdentity.Value, mappings);
            var targetOverride = mappings.SingleOrDefault(mapping => mapping.Asin == targetIdentity.Value);
            var blockers = new List<string>();
            if (matches.Count != 1 || matches[0] != target.ForeignAuthorId)
            {
                blockers.Add("The name credit does not resolve uniquely to the selected canonical author.");
            }

            if (!MatchingPath(source.Path, target.Path))
            {
                blockers.Add("The author folders differ. Reconciliation requires the same folder to preserve relative sidecar paths.");
            }

            var namesToClaim = new[] { NormalName(sourceIdentity.Value), targetOverride?.Name ?? NormalName(target.Name) }.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (targetOverride?.Enabled == false || string.IsNullOrWhiteSpace(target.Name) ||
                mappings.Any(mapping => mapping.Enabled && mapping.Id != targetOverride?.Id &&
                    (mapping.AlternateAsins.Contains(targetIdentity.Value) || OverrideNames(mapping).Any(namesToClaim.Contains))))
            {
                blockers.Add("An existing metadata correction conflicts with the selected canonical identity.");
            }

            var recordings = ReadRecordings(connection, transaction, source.AuthorMetadataId);
            var canonicalRecordings = ReadRecordings(connection, transaction, target.AuthorMetadataId);
            if (RecordingIds(recordings).Intersect(RecordingIds(canonicalRecordings), StringComparer.Ordinal).Any())
            {
                blockers.Add("Both profiles already contain a recording with the same catalog identity. Review that recording before reconciliation.");
            }

            var files = connection.Query<AuthorReconciliationFile>(
                """
                SELECT f."Id", f."EditionId", f."Path" FROM "BookFiles" f
                JOIN "Editions" e ON e."Id" = f."EditionId"
                JOIN "Books" b ON b."Id" = e."BookId"
                WHERE b."AuthorMetadataId" = @MetadataId ORDER BY f."Id"
                """,
                new { MetadataId = source.AuthorMetadataId },
                transaction).AsList();
            var referenceIds = AuthorReferenceTables.ToDictionary(table => table,
                table => connection.Query<int>($"SELECT \"Id\" FROM \"{table}\" WHERE \"AuthorId\" = @Id ORDER BY \"Id\"", new { source.Id }, transaction).AsList());
            var holds = connection.Query<DownloadHistory>(
                """SELECT "Id", "AuthorId", "Data" FROM "DownloadHistory" WHERE "EventType" = @Pending""",
                new { Pending = DownloadHistoryEventType.DownloadSubmissionPending },
                transaction);
            var bookIds = recordings.Concat(canonicalRecordings).Select(recording => recording.BookId).ToHashSet();
            if (_trackedDownloads.GetTrackedDownloads().Any(download => download.RemoteBook != null &&
                (download.RemoteBook.Author?.Id == source.Id || download.RemoteBook.Author?.Id == target.Id ||
                    download.RemoteBook.Books?.Any(book => bookIds.Contains(book.Id)) == true)))
            {
                blockers.Add("Finish or remove downloads for these authors from the queue before reconciliation.");
            }

            if (holds.Any(row => IsRelevantHold(row, source.Id, target.Id, bookIds)))
            {
                blockers.Add("Resolve pending download submissions for these authors before reconciliation.");
            }

            var downloads = connection.Query<DownloadHistory>(
                """
                SELECT "Id", "DownloadId", "DownloadClientId", "Data" FROM "DownloadHistory"
                WHERE "AuthorId" = @Id AND "EventType" = @Imported ORDER BY "Id"
                """,
                new { source.Id, Imported = DownloadHistoryEventType.DownloadImported },
                transaction).AsList();
            var preview = new AuthorReconciliationPreview
            {
                Source = source,
                Target = target,
                Recordings = recordings,
                Files = files,
                References = referenceIds.Select(reference => new AuthorReconciliationReference { Table = reference.Key, Count = reference.Value.Count }).ToList(),
                SettingsDifferences = SettingsDifferences(source, target),
                Blockers = blockers,
                ExistingOverrideId = targetOverride?.Id
            };
            var mappingStatus = STJson.ToJson(new { request.EvidenceUrl, request.Reason, preview, matches, mappings, referenceIds, downloads, canonicalRecordings });
            var receipts = RebindReceipts(downloads, source.Id, target.Id, blockers);
            preview.PreviewToken = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mappingStatus)));
            return new Snapshot(preview, BuildOverride(target, targetOverride, sourceIdentity.Value, targetIdentity.Region, targetIdentity.Value, request), receipts);
        }

        private static List<DownloadHistory> RebindReceipts(List<DownloadHistory> downloads, int sourceId, int targetId, List<string> blockers)
        {
            var receipts = new List<DownloadHistory>();
            foreach (var row in downloads)
            {
                var keys = row.Data?.Keys.Where(key => string.Equals(key, DownloadHistoryService.ImportReceiptKey, StringComparison.OrdinalIgnoreCase)).ToList();
                var key = keys?.FirstOrDefault();
                if (key == null)
                {
                    continue;
                }

                // Preserve the exact historical JSON and source policy. Patch
                // only the ownership binding used for live receipt validation.
                if (keys.Count != 1 || string.IsNullOrWhiteSpace(row.Data[key]) ||
                    !STJson.TryDeserialize<DownloadImportReceipt>(row.Data[key], out var receipt) || receipt == null || receipt.Version != 1 ||
                    receipt.AuthorId != sourceId || receipt.DownloadId != row.DownloadId || receipt.DownloadClientId != row.DownloadClientId)
                {
                    blockers.Add("A historical import receipt has an unexpected author or download identity. Reconciliation cannot safely rebind it.");
                    break;
                }

                JsonObject document;
                List<string> authorKeys;
                List<string> originalAuthorKeys;
                try
                {
                    document = JsonNode.Parse(row.Data[key], documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }).AsObject();
                    authorKeys = MatchingKeys(document, "authorId");
                    originalAuthorKeys = MatchingKeys(document, "originalAuthorId");
                }
                catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
                {
                    blockers.Add("A historical import receipt is malformed. Reconciliation cannot safely rebind it.");
                    break;
                }

                if (authorKeys.Count != 1 || originalAuthorKeys.Count > 1)
                {
                    blockers.Add("A historical import receipt has an ambiguous author identity. Reconciliation cannot safely rebind it.");
                    break;
                }

                document[authorKeys[0]] = targetId;
                if (receipt.OriginalAuthorId == null)
                {
                    document[originalAuthorKeys.SingleOrDefault() ?? "originalAuthorId"] = sourceId;
                }

                row.Data[key] = document.ToJsonString();
                receipts.Add(row);
            }

            return receipts;
        }

        private static List<string> MatchingKeys(JsonObject document, string name)
        {
            return document.Select(entry => entry.Key).Where(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static bool IsRelevantHold(DownloadHistory row, int sourceId, int targetId, HashSet<int> bookIds)
        {
            if (row.AuthorId <= 0 || row.AuthorId == sourceId || row.AuthorId == targetId)
            {
                return true;
            }

            var data = DownloadImportCompletionService.DataValue(row.Data, "bookIds");
            return data != null && STJson.TryDeserialize<List<int>>(data, out var held) && held?.Any(bookIds.Contains) == true;
        }

        private static AuthorReconciliationProfile GetProfile(IDbConnection connection, IDbTransaction transaction, int id)
        {
            return connection.QuerySingleOrDefault<AuthorReconciliationProfile>(
                Profiles + """

                WHERE a."Id" = @Id
                """,
                new { Id = id },
                transaction) ?? throw Conflict("One of the author profiles is no longer in the library.");
        }

        private static List<AudiobookMetadataOverride> ReadOverrides(IDbConnection connection, IDbTransaction transaction, string region)
        {
            return connection.Query<AudiobookMetadataOverride>(
                """SELECT * FROM "AudiobookMetadataOverrides" WHERE "Kind" = 'author' AND "Region" = @Region ORDER BY "Id" """,
                new { Region = region },
                transaction).AsList();
        }

        private static List<string> MatchingIdentities(IDbConnection connection,
                                                       IDbTransaction transaction,
                                                       string region,
                                                       string name,
                                                       List<AudiobookMetadataOverride> mappings = null)
        {
            var key = NormalName(name);
            var authors = connection.Query<AuthorMetadata>(
                """
                SELECT "ForeignAuthorId", "Name", "Aliases" FROM "AuthorMetadata"
                WHERE "ForeignAuthorId" LIKE @Region ORDER BY "ForeignAuthorId"
                """,
                new { Region = $"audible:{region}:%" },
                transaction);
            var identities = authors.Where(author => TryIdentity(author.ForeignAuthorId, out var identity) && !identity.IsName &&
                (author.Aliases ?? new List<string>()).Append(author.Name).Select(NormalName).Contains(key, StringComparer.OrdinalIgnoreCase))
                .Select(author => author.ForeignAuthorId);
            return identities.Concat((mappings ?? ReadOverrides(connection, transaction, region))
                .Where(mapping => mapping.Enabled && OverrideNames(mapping).Contains(key, StringComparer.OrdinalIgnoreCase))
                .Select(mapping => AudiobookId.ForAsin(region, mapping.Asin))).Distinct(StringComparer.Ordinal).Order().ToList();
        }

        private static IEnumerable<string> OverrideNames(AudiobookMetadataOverride mapping)
        {
            return mapping.Aliases.Append(mapping.Name).Select(NormalName);
        }

        private static AudiobookMetadataOverride BuildOverride(AuthorReconciliationProfile target,
                                                                 AudiobookMetadataOverride existing,
                                                                 string name,
                                                                 string region,
                                                                 string asin,
                                                                 AuthorReconciliationRequest request)
        {
            var result = existing ?? new AudiobookMetadataOverride
            {
                Kind = "author",
                Region = region,
                Asin = asin,
                Name = NormalName(target.Name),
                SourceUrl = request.EvidenceUrl.Trim(),
                Reason = request.Reason.Trim()
            };
            var alias = NormalName(name);
            if (!OverrideNames(result).Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                result.Aliases.Add(alias);
            }

            if (existing != null)
            {
                result.Reason += $"\nAuthor reconciliation evidence ({request.EvidenceUrl.Trim()}): {request.Reason.Trim()}";
            }

            return result;
        }

        private static List<AuthorReconciliationRecording> ReadRecordings(IDbConnection connection, IDbTransaction transaction, int metadataId)
        {
            return connection.Query<AuthorReconciliationRecording>(
                """
                SELECT b."Id" AS "BookId", b."ForeignBookId", COALESCE(e."Title", b."Title") AS "Title", b."Monitored",
                    e."Id" AS "EditionId", e."ForeignEditionId", e."Asin", e."AudibleRegion"
                FROM "Books" b LEFT JOIN "Editions" e ON e."BookId" = b."Id"
                WHERE b."AuthorMetadataId" = @MetadataId ORDER BY b."Id", e."Id"
                """,
                new { MetadataId = metadataId },
                transaction).AsList();
        }

        private static IEnumerable<string> RecordingIds(List<AuthorReconciliationRecording> recordings)
        {
            return recordings.SelectMany(recording => new[]
            {
                recording.ForeignBookId,
                recording.ForeignEditionId,
                AudiobookId.IsAsin(recording.Asin) && !string.IsNullOrEmpty(recording.AudibleRegion)
                    ? AudiobookId.ForAsin(recording.AudibleRegion, recording.Asin)
                    : null
            }).Where(id => !string.IsNullOrWhiteSpace(id));
        }

        private static List<string> SettingsDifferences(AuthorReconciliationProfile source, AuthorReconciliationProfile target)
        {
            var differences = new List<string>();
            if (source.QualityProfileId != target.QualityProfileId)
            {
                differences.Add("qualityProfile");
            }

            if (source.MetadataProfileId != target.MetadataProfileId)
            {
                differences.Add("metadataProfile");
            }

            if (source.Monitored != target.Monitored)
            {
                differences.Add("monitoring");
            }

            if (source.MonitorNewItems != target.MonitorNewItems)
            {
                differences.Add("monitorNewItems");
            }

            if (!(source.Tags ?? new HashSet<int>()).SetEquals(target.Tags ?? new HashSet<int>()))
            {
                differences.Add("tags");
            }

            return differences;
        }

        private static bool MatchingPath(string source, string target)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target) &&
                    Path.IsPathFullyQualified(source) && Path.IsPathFullyQualified(target) && source.PathEquals(target);
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
            {
                return false;
            }
        }

        private static bool TryIdentity(string id, out (string Region, string Value, bool IsName) identity)
        {
            try
            {
                identity = AudiobookId.Parse(id);
                return !string.IsNullOrWhiteSpace(identity.Value);
            }
            catch (BookInfoException)
            {
                identity = default;
                return false;
            }
        }

        private static string NormalName(string name) => AudiobookTextNormalizer.Text(name);

        private static void ValidateRequest(AuthorReconciliationRequest request)
        {
            if (request == null || request.SourceAuthorId <= 0 || request.TargetAuthorId <= 0 || request.SourceAuthorId == request.TargetAuthorId ||
                string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000 || request.EvidenceUrl?.Length > 2048 ||
                !Uri.TryCreate(request.EvidenceUrl?.Trim(), UriKind.Absolute, out var url) || url.Scheme is not "https" and not "http" ||
                !string.IsNullOrEmpty(url.UserInfo))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Provide two different authors, an HTTP or HTTPS evidence URL without credentials, and a reason.");
            }
        }

        private static void RequireCount(int changed, int expected)
        {
            if (changed != expected)
            {
                throw Conflict("The author reconciliation changed while applying. Review the current author profiles again.");
            }
        }

        private static NzbDroneClientException Conflict(string message) => new NzbDroneClientException(HttpStatusCode.Conflict, message);

        private sealed class Snapshot
        {
            public AuthorReconciliationPreview Preview { get; }
            public AudiobookMetadataOverride Override { get; }
            public List<DownloadHistory> Receipts { get; }

            public Snapshot(AuthorReconciliationPreview preview, AudiobookMetadataOverride mapping, List<DownloadHistory> receipts)
            {
                Preview = preview;
                Override = mapping;
                Receipts = receipts;
            }
        }
    }
}
