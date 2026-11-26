using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.LibraryStats;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.Books
{
    public interface IRecordingWorkRepository
    {
        List<RecordingWorkRecording> ForBook(int bookId);
        List<RecordingWork> FindWorks(string term);
        RecordingWorkSnapshot Inspect(int editionId);
        RecordingWorkSnapshot Save(int editionId, RecordingWorkEdit edit);
    }

    public class RecordingWorkRepository : IRecordingWorkRepository
    {
        private const string Recordings = """
            SELECT e."Id" AS "EditionId", e."BookId", e."ForeignEditionId", e."Asin", e."AudibleRegion",
                e."Title", m."Name" AS "Author", b."TitleSlug" AS "BookSlug", e."Monitored",
                (SELECT COUNT(*) FROM "BookFiles" f WHERE f."EditionId" = e."Id") AS "FileCount",
                (SELECT COALESCE(SUM(f."Size"), 0) FROM "BookFiles" f WHERE f."EditionId" = e."Id") AS "SizeOnDisk"
            FROM "Editions" e INNER JOIN "Books" b ON e."BookId" = b."Id"
            INNER JOIN "AuthorMetadata" m ON b."AuthorMetadataId" = m."Id"
            WHERE NOT COALESCE(e."IsEbook", FALSE)
            """;
        private readonly IMainDatabase _database;
        private readonly IEventAggregator _events;

        public RecordingWorkRepository(IMainDatabase database, IEventAggregator events)
        {
            _database = database;
            _events = events;
        }

        public List<RecordingWorkRecording> ForBook(int bookId)
        {
            using var connection = _database.OpenConnection();
            return connection.Query<RecordingWorkRecording>(Recordings + """ AND b."Id" = @bookId ORDER BY e."Monitored" DESC, e."Id" """, new { bookId }).AsList();
        }

        public List<RecordingWork> FindWorks(string term)
        {
            term = (term ?? string.Empty).Trim();
            if (term.Length > 512)
            {
                throw Invalid("Work search must not exceed 512 characters.");
            }

            // Treat punctuation in the search literally (including LIKE wildcards).
            var pattern = "%" + term.ToLowerInvariant().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
            using var connection = _database.OpenConnection();
            return connection.Query<RecordingWork>(
                """
                SELECT * FROM "RecordingWorks"
                WHERE LOWER("Title") LIKE @pattern ESCAPE '!' OR LOWER("Author") LIKE @pattern ESCAPE '!'
                    OR LOWER("Series") LIKE @pattern ESCAPE '!' OR LOWER("Position") LIKE @pattern ESCAPE '!'
                ORDER BY "Title", "Author", "Id" LIMIT 50
                """,
                new { pattern }).AsList();
        }

        public RecordingWorkSnapshot Inspect(int editionId)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.SQLite ? IsolationLevel.ReadCommitted : IsolationLevel.RepeatableRead);
            return ReadSnapshot(connection, transaction, editionId);
        }

        public RecordingWorkSnapshot Save(int editionId, RecordingWorkEdit edit)
        {
            Validate(edit);

            using var connection = _database.OpenConnection();

            // SQLite's non-deferred transaction serializes writers. PostgreSQL
            // locks the exact edition first; ReadCommitted then sees the most
            // recent committed membership set, including initially empty sets.
            using var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.SQLite ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                connection.QuerySingleOrDefault<int>(
                    """SELECT "Id" FROM "Editions" WHERE "Id" = @editionId FOR UPDATE""",
                    new { editionId },
                    transaction);
            }

            var current = ReadSnapshot(connection, transaction, editionId);
            if (current.Revision != edit.Revision)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Work coverage changed. Reload this recording before saving.");
            }

            var recordingIdentity = Identity(current.Recording);
            if (recordingIdentity == null)
            {
                throw Invalid("The recording has no catalogue or local identity.");
            }

            var ids = edit.Memberships.Where(item => item.WorkId.HasValue).Select(item => item.WorkId.Value).ToArray();
            if (ids.Length > 0 && ReadWorks(connection, transaction, ids).Count != ids.Length)
            {
                throw Invalid("A selected work no longer exists.");
            }

            // This edit replaces only this recording's associations, never the
            // shared work records, recording metadata, monitoring or file rows.
            connection.Execute("""DELETE FROM "RecordingWorkMemberships" WHERE "EditionId" = @editionId""", new { editionId }, transaction);
            var workRepository = new BasicRepository<RecordingWork>(_database, _events);
            var membershipRepository = new BasicRepository<RecordingWorkMembership>(_database, _events);

            foreach (var member in edit.Memberships)
            {
                var workId = member.WorkId ?? workRepository.Insert(connection, transaction, member.NewWork).Id;
                membershipRepository.Insert(connection, transaction, new RecordingWorkMembership
                {
                    EditionId = editionId,
                    WorkId = workId,
                    RecordingIdentity = recordingIdentity,
                    Confirmed = member.Confirmed.Value
                });
            }

            var result = ReadSnapshot(connection, transaction, editionId);
            transaction.Commit();
            _events.PublishEvent(new LibraryStatisticsChangedEvent());
            return result;
        }

        private RecordingWorkSnapshot ReadSnapshot(IDbConnection connection, IDbTransaction transaction, int editionId)
        {
            var recording = connection.QuerySingleOrDefault<RecordingWorkRecording>(
                Recordings + """ AND e."Id" = @editionId""",
                new { editionId },
                transaction);
            if (recording == null)
            {
                throw new ModelNotFoundException(typeof(Edition), editionId);
            }

            var recordingIdentity = Identity(recording);
            var stored = connection.Query<RecordingWorkMembership>(
                """SELECT * FROM "RecordingWorkMemberships" WHERE "EditionId" = @editionId ORDER BY "WorkId" ASC""",
                new { editionId },
                transaction).AsList();
            var current = stored.Where(member => member.RecordingIdentity == recordingIdentity).ToList();
            var workIds = current.Select(member => member.WorkId).ToArray();

            var result = new RecordingWorkSnapshot
            {
                Recording = recording,
                Revision = Hash(new { recordingIdentity, recording.BookId, Members = stored.Select(member => new { member.WorkId, member.RecordingIdentity, member.Confirmed }) }),
                UnmatchedMembershipCount = stored.Count - current.Count,
                Memberships = new List<RecordingWorkCoverage>()
            };

            if (workIds.Length == 0)
            {
                return result;
            }

            var works = ReadWorks(connection, transaction, workIds).ToDictionary(work => work.Id);
            var related = connection.Query<RecordingWorkMembership>(
                $"""SELECT * FROM "RecordingWorkMemberships" WHERE "WorkId" {InIds}""",
                new { ids = workIds },
                transaction).AsList();

            var recordings = connection.Query<RecordingWorkRecording>(
                Recordings + $""" AND e."Id" IN (SELECT DISTINCT "EditionId" FROM "RecordingWorkMemberships" WHERE "WorkId" {InIds})""",
                new { ids = workIds },
                transaction).ToDictionary(item => item.EditionId);

            var validRelated = related.Where(member => recordings.TryGetValue(member.EditionId, out var item) && member.RecordingIdentity == Identity(item))
                .ToLookup(member => member.WorkId);
            var coveredEditions = new Dictionary<int, RecordingWorkRecording>();

            foreach (var member in current)
            {
                if (!works.TryGetValue(member.WorkId, out var work))
                {
                    continue;
                }

                var references = validRelated[member.WorkId].Select(link => new RecordingWorkReference
                {
                    Recording = recordings[link.EditionId],
                    Confirmed = link.Confirmed
                }).OrderBy(item => item.Recording.EditionId).ToList();

                var hasConfirmedFiles = references.Any(item => item.Confirmed && item.Recording.FileCount > 0);
                result.Memberships.Add(new RecordingWorkCoverage
                {
                    Work = work,
                    Confirmed = member.Confirmed,
                    HasConfirmedFiles = hasConfirmedFiles,
                    Recordings = references
                });
                if (!member.Confirmed)
                {
                    continue;
                }

                if (hasConfirmedFiles)
                {
                    result.CoveredWorkCount++;
                }

                foreach (var reference in references.Where(item => item.Confirmed && item.Recording.FileCount > 0))
                {
                    coveredEditions.TryAdd(reference.Recording.EditionId, reference.Recording);
                }
            }

            // Each persisted file belongs to exactly one edition. Counting
            // related editions once ensures an omnibus contributes its bytes
            // once even when it covers several confirmed works.
            result.RelatedRecordingCount = coveredEditions.Count;
            result.RelatedFileCount = coveredEditions.Values.Sum(item => item.FileCount);
            result.RelatedSizeOnDisk = coveredEditions.Values.Sum(item => item.SizeOnDisk);
            return result;
        }

        // PostgreSQL binds arrays natively, while Dapper expands SQLite IN lists.
        private string InIds => _database.DatabaseType == DatabaseType.PostgreSQL ? "= ANY(@ids)" : "IN @ids";

        private List<RecordingWork> ReadWorks(IDbConnection connection, IDbTransaction transaction, int[] ids)
        {
            return connection.Query<RecordingWork>($"""SELECT * FROM "RecordingWorks" WHERE "Id" {InIds}""", new { ids }, transaction).AsList();
        }

        private static string Identity(RecordingWorkRecording recording) => Identity(recording.ForeignEditionId, recording.Asin, recording.AudibleRegion);

        internal static string Identity(string id, string asin, string audibleRegion)
        {
            // Native IDs already encode marketplace/ASIN or immutable local ID.
            // Legacy catalogue IDs include separately retained region and ASIN.
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return AudiobookId.IsNative(id) || LocalRecordingId.IsLocal(id)
                ? Hash(id)
                : Hash(new { id, Asin = asin, AudibleRegion = audibleRegion });
        }

        private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(STJson.ToJson(value))));

        private static void Validate(RecordingWorkEdit edit)
        {
            if (edit?.Memberships == null || string.IsNullOrWhiteSpace(edit.Revision))
            {
                throw Invalid("An up-to-date recording revision and work memberships are required.");
            }

            var selected = new HashSet<int>();
            foreach (var member in edit.Memberships)
            {
                if (member == null || !member.Confirmed.HasValue || member.WorkId.HasValue == (member.NewWork != null))
                {
                    throw Invalid("Each membership must specify exactly one work and whether it is confirmed or uncertain.");
                }

                if (member.WorkId.HasValue)
                {
                    if (member.WorkId.Value <= 0 || !selected.Add(member.WorkId.Value))
                    {
                        throw Invalid("Select each existing work only once.");
                    }

                    continue;
                }

                var work = member.NewWork;
                if (work.Id != 0)
                {
                    throw Invalid("A new work cannot have an existing ID.");
                }

                work.Title = Normalize(work.Title, 512);
                work.Author = Normalize(work.Author, 512);
                work.Series = Normalize(work.Series, 512);
                work.Position = Normalize(work.Position, 128);
                if (work.Title.Length == 0)
                {
                    throw Invalid("A work title is required.");
                }
            }
        }

        private static string Normalize(string value, int maxLength)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length > maxLength)
            {
                throw Invalid($"Work text must not exceed {maxLength} characters.");
            }

            return value;
        }

        private static NzbDroneClientException Invalid(string message) => new NzbDroneClientException(HttpStatusCode.BadRequest, message);
    }
}
