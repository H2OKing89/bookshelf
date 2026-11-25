using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.ImportLists
{
    public enum ImportListCleanupMode
    {
        Preview,
        Unmonitor,
        Tag
    }

    public class ImportListCleanupRequest
    {
        public ImportListCleanupMode Mode { get; set; }
        public int? TagId { get; set; }
        public string ConfirmationToken { get; set; }
    }

    public class ImportListCleanupCandidate
    {
        public int BookId { get; set; }
        public string RecordingId { get; set; }
        public string Title { get; set; }
        public int AuthorId { get; set; }
        public string AuthorName { get; set; }
        public bool Monitored { get; set; }
    }

    public class ImportListCleanupPreview
    {
        public ImportListCleanupMode Mode { get; set; }
        public int? TagId { get; set; }
        public List<string> Blockers { get; set; } = new ();
        public List<ImportListCleanupCandidate> Candidates { get; set; } = new ();
        public string ConfirmationToken { get; set; }
    }

    public interface IImportListPolicyRepository
    {
        void ReconcileTags(IReadOnlyCollection<int> refreshedLists);
        ImportListCleanupPreview Preview(ImportListCleanupMode mode, int? tagId);
        ImportListCleanupPreview Apply(ImportListCleanupRequest request);
        void ReleaseBookOwnership(IEnumerable<int> bookIds);
        void ReleaseAuthorOwnership(int authorId, bool releaseBooks, IEnumerable<int> manualTags);
    }

    public class ImportListPolicyRepository : IImportListPolicyRepository
    {
        // An unattempted old snapshot is not proof of current absence. A manual
        // refresh of each list can provide newer evidence without provider changes.
        private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);
        private readonly IMainDatabase _database;
        private readonly IEventAggregator _events;
        private readonly IAuthorService _authors;
        private readonly IBookService _books;

        public ImportListPolicyRepository(IMainDatabase database, IEventAggregator events, IAuthorService authors, IBookService books)
        {
            _database = database;
            _events = events;
            _authors = authors;
            _books = books;
        }

        public void ReconcileTags(IReadOnlyCollection<int> refreshedLists)
        {
            if (refreshedLists.Count == 0)
            {
                return;
            }

            using var connection = _database.OpenConnection();
            using var transaction = Start(connection);
            var lists = ReadLists(connection, transaction, true);
            var relevant = lists.Where(list => list.Available).ToList();
            var validTags = connection.Query<int>("SELECT \"Id\" FROM \"Tags\"", transaction: transaction).ToHashSet();
            var authors = ReadAuthors(connection, transaction, true);
            var ambiguousAuthors = authors.GroupBy(author => author.ForeignAuthorId, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            var owners = connection.Query<TagOwner>("SELECT * FROM \"ImportListTagOwners\"", transaction: transaction)
                .GroupBy(owner => (owner.AuthorId, owner.TagId)).ToDictionary(group => group.Key, group => group.Select(owner => owner.ImportListId).ToHashSet());
            var changed = new List<int>();

            foreach (var author in authors.Where(author => !ambiguousAuthors.Contains(author.ForeignAuthorId)))
            {
                author.Tags ??= new HashSet<int>();
                var before = new HashSet<int>(author.Tags);
                var previousClaims = owners.Where(pair => pair.Key.AuthorId == author.AuthorId).ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet());
                var claimedTags = owners.Keys.Where(key => key.AuthorId == author.AuthorId).Select(key => key.TagId).ToHashSet();
                var currentLists = relevant.Where(list => list.IsSuccessful && refreshedLists.Contains(list.Id) &&
                    list.TagExisting != ImportListTagExisting.None && list.ContainsAuthor(author.ForeignAuthorId)).ToList();

                // Add confers a persistent/manual-equivalent tag. Sync may only
                // claim a tag if it actually supplied it or another Sync list did.
                foreach (var list in currentLists.OrderByDescending(list => list.TagExisting))
                {
                    foreach (var tagId in (list.Tags ?? new HashSet<int>()).Where(validTags.Contains))
                    {
                        var key = (author.AuthorId, tagId);
                        var added = author.Tags.Add(tagId);
                        if (list.TagExisting == ImportListTagExisting.Add)
                        {
                            owners.Remove(key);
                            claimedTags.Remove(tagId);
                        }
                        else if (added || owners.ContainsKey(key))
                        {
                            if (!owners.TryGetValue(key, out var claims))
                            {
                                owners[key] = claims = new HashSet<int>();
                            }

                            claims.Add(list.Id);
                            claimedTags.Add(tagId);
                        }
                    }
                }

                foreach (var tagId in claimedTags.ToList())
                {
                    var key = (author.AuthorId, tagId);
                    var claims = owners[key];
                    var previous = lists.Where(list => claims.Contains(list.Id)).ToList();

                    // Deleting/disabling a list or switching it out of Sync
                    // does not constitute permission to remove author tags.
                    if (!author.Tags.Contains(tagId) || !validTags.Contains(tagId) ||
                        previous.Count != claims.Count || previous.Any(list => !list.Available || list.TagExisting != ImportListTagExisting.Sync))
                    {
                        owners.Remove(key);
                        continue;
                    }

                    var sameTag = relevant.Where(list => list.TagExisting != ImportListTagExisting.None && list.Tags?.Contains(tagId) == true).ToList();

                    // A known member on another list is independent ownership;
                    // Add mode protects the tag permanently once it succeeds.
                    if (sameTag.Any(list => list.IsSuccessful && list.TagExisting == ImportListTagExisting.Add && list.ContainsAuthor(author.ForeignAuthorId)))
                    {
                        owners.Remove(key);
                        continue;
                    }

                    var stillOwned = sameTag.Where(list => list.IsSuccessful && list.TagExisting == ImportListTagExisting.Sync && list.ContainsAuthor(author.ForeignAuthorId)).ToList();
                    claims.UnionWith(stillOwned.Select(list => list.Id));

                    if (stillOwned.Count > 0 || !previous.Concat(sameTag).Any(list => refreshedLists.Contains(list.Id)) ||
                        previous.Concat(sameTag).Any(list => !list.IsReliableAbsence))
                    {
                        continue;
                    }

                    // Every old/current potential owner has a complete recent
                    // successful answer, and none retains this author/tag.
                    author.Tags.Remove(tagId);
                    owners.Remove(key);
                }

                if (!before.SetEquals(author.Tags))
                {
                    WriteTags(connection, transaction, author);
                    changed.Add(author.AuthorId);
                }

                // This lock is shared with manual ownership releases; persist only
                // current claims. A manual tag removal trigger removes stale rows.
                var currentClaims = owners.Where(pair => pair.Key.AuthorId == author.AuthorId && author.Tags.Contains(pair.Key.TagId)).ToDictionary();
                if (currentClaims.Count == previousClaims.Count && currentClaims.All(pair => previousClaims.TryGetValue(pair.Key, out var prior) && prior.SetEquals(pair.Value)))
                {
                    continue;
                }

                connection.Execute("DELETE FROM \"ImportListTagOwners\" WHERE \"AuthorId\" = @AuthorId", author, transaction);
                foreach (var pair in currentClaims)
                {
                    foreach (var listId in pair.Value)
                    {
                        const string insertOwnerSql = """
                            INSERT INTO "ImportListTagOwners" ("AuthorId", "TagId", "ImportListId") VALUES (@AuthorId, @TagId, @ImportListId)
                            """;
                        connection.Execute(insertOwnerSql, new { author.AuthorId, pair.Key.TagId, ImportListId = listId }, transaction);
                    }
                }
            }

            transaction.Commit();
            NotifyAuthors(changed);
        }

        public ImportListCleanupPreview Preview(ImportListCleanupMode mode, int? tagId)
        {
            using var connection = _database.OpenConnection();
            using var transaction = Start(connection, false);
            var preview = BuildPreview(connection, transaction, mode, tagId, false);
            transaction.Commit();
            return preview;
        }

        public ImportListCleanupPreview Apply(ImportListCleanupRequest request)
        {
            if (request == null || request.Mode == ImportListCleanupMode.Preview || string.IsNullOrEmpty(request.ConfirmationToken))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Select an action and confirm its current preview.");
            }

            using var connection = _database.OpenConnection();
            using var transaction = Start(connection);
            var preview = BuildPreview(connection, transaction, request.Mode, request.TagId, true);
            if (preview.ConfirmationToken == null || preview.ConfirmationToken != request.ConfirmationToken)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "List cleanup evidence changed. Refresh the preview before applying it.");
            }

            var changedAuthors = new List<int>();
            var changedBooks = new List<int>();
            if (request.Mode == ImportListCleanupMode.Unmonitor)
            {
                foreach (var candidate in preview.Candidates)
                {
                    connection.Execute("UPDATE \"Books\" SET \"Monitored\" = FALSE WHERE \"Id\" = @BookId", candidate, transaction);
                    changedBooks.Add(candidate.BookId);
                }
            }
            else
            {
                // Tags are author-level in Bookshelf; the preview explicitly
                // names these authors. No unrelated book monitoring is changed.
                var authorIds = preview.Candidates.Select(item => item.AuthorId).ToHashSet();
                foreach (var author in ReadAuthors(connection, transaction, true).Where(author => authorIds.Contains(author.AuthorId)))
                {
                    author.Tags ??= new HashSet<int>();
                    if (author.Tags.Add(request.TagId.Value))
                    {
                        WriteTags(connection, transaction, author);
                        changedAuthors.Add(author.AuthorId);
                    }

                    connection.Execute("DELETE FROM \"ImportListTagOwners\" WHERE \"AuthorId\" = @AuthorId AND \"TagId\" = @TagId", new { author.AuthorId, TagId = request.TagId.Value }, transaction);
                }
            }

            transaction.Commit();
            NotifyAuthors(changedAuthors);
            foreach (var bookId in changedBooks)
            {
                var book = _books.GetBook(bookId);
                _events.PublishEvent(new BookEditedEvent(book, book));
            }

            // Receipt identifies exactly the applied preview, not another
            // actionable confirmation.
            preview.ConfirmationToken = null;
            return preview;
        }

        public void ReleaseBookOwnership(IEnumerable<int> bookIds)
        {
            var ids = bookIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            using var connection = _database.OpenConnection();
            using var transaction = Start(connection);
            LockBooks(connection, transaction, ids);
            connection.Execute($"DELETE FROM \"ImportListManagedRecordings\" WHERE \"BookId\" {IdsPredicate}", new { Ids = ids.ToArray() }, transaction);
            transaction.Commit();
        }

        public void ReleaseAuthorOwnership(int authorId, bool releaseBooks, IEnumerable<int> manualTags)
        {
            using var connection = _database.OpenConnection();
            using var transaction = Start(connection);
            const string authorBookIds = """
                SELECT b."Id" FROM "Books" b INNER JOIN "Authors" a ON b."AuthorMetadataId" = a."AuthorMetadataId"
                WHERE a."Id" = @AuthorId
                """;
            var ids = releaseBooks ? connection.Query<int>(authorBookIds, new { AuthorId = authorId }, transaction).ToList() : new List<int>();
            LockBooks(connection, transaction, ids);
            LockAuthor(connection, transaction, authorId);
            if (ids.Count > 0)
            {
                connection.Execute($"DELETE FROM \"ImportListManagedRecordings\" WHERE \"BookId\" {IdsPredicate}", new { Ids = ids.ToArray() }, transaction);
            }

            if (manualTags != null)
            {
                var tags = manualTags.Distinct().ToList();
                if (tags.Count > 0)
                {
                    connection.Execute($"DELETE FROM \"ImportListTagOwners\" WHERE \"AuthorId\" = @AuthorId AND \"TagId\" {IdsPredicate}", new { AuthorId = authorId, Ids = tags.ToArray() }, transaction);
                }
            }

            transaction.Commit();
        }

        private ImportListCleanupPreview BuildPreview(IDbConnection connection, IDbTransaction transaction, ImportListCleanupMode mode, int? tagId, bool write)
        {
            if (!Enum.IsDefined(mode) || (mode == ImportListCleanupMode.Tag && (!tagId.HasValue ||
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Tags\" WHERE \"Id\" = @TagId", new { TagId = tagId }, transaction) != 1)) ||
                (mode != ImportListCleanupMode.Tag && tagId.HasValue))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Invalid cleanup action or tag.");
            }

            var result = new ImportListCleanupPreview { Mode = mode, TagId = tagId };
            var lists = ReadLists(connection, transaction, write);
            var active = lists.Where(list => list.Available).ToList();
            if (active.Count == 0)
            {
                result.Blockers.Add("No enabled import lists.");
            }

            result.Blockers.AddRange(active.Where(list => !list.IsReliableAbsence)
                .Select(list => $"{list.Name}: a complete, current, successful and resolved snapshot within 24 hours is required."));
            if (result.Blockers.Count > 0)
            {
                return result;
            }

            // Foreign identity and exact matching edition are revalidated. No
            // author-wide expansion, historical manual or local book inference.
            var candidateSql = """
                SELECT b."Id" AS "BookId", b."ForeignBookId" AS "RecordingId", b."Title", a."Id" AS "AuthorId",
                    m."Name" AS "AuthorName", b."Monitored", a."Tags", m."ForeignAuthorId", r."ImportListId"
                FROM "ImportListManagedRecordings" r
                JOIN "Books" b ON b."Id" = r."BookId" AND b."ForeignBookId" = r."ForeignBookId" AND b."AuthorMetadataId" = r."AuthorMetadataId"
                JOIN "Authors" a ON a."AuthorMetadataId" = b."AuthorMetadataId"
                JOIN "AuthorMetadata" m ON m."Id" = a."AuthorMetadataId"
                WHERE EXISTS (SELECT 1 FROM "Editions" e WHERE e."BookId" = b."Id" AND e."ForeignEditionId" = b."ForeignBookId" AND NOT COALESCE(e."IsEbook", FALSE) AND
                    b."ForeignBookId" = 'audible:' || e."AudibleRegion" || ':' || e."Asin")
                ORDER BY b."Id"
                """;
            var candidates = connection.Query<CleanupRow>(candidateSql + (write && IsPostgres ? " FOR UPDATE OF b" : ""), transaction: transaction).ToList();
            if (write)
            {
                // Recheck after locking the author rows since manual tag edits
                // can release claims, and a stale preview must not win a race.
                foreach (var authorId in candidates.Select(row => row.AuthorId).Distinct().Order())
                {
                    LockAuthor(connection, transaction, authorId);
                }

                if (IsPostgres && candidates.Count > 0)
                {
                    connection.Query<int>("SELECT \"Id\" FROM \"Editions\" WHERE \"BookId\" = ANY(@Ids) ORDER BY \"Id\" FOR UPDATE", new { Ids = candidates.Select(row => row.BookId).Distinct().ToArray() }, transaction).ToList();
                }

                candidates = connection.Query<CleanupRow>(candidateSql, transaction: transaction).ToList();
            }

            var exclusions = ReadExclusions(connection, transaction);
            var activeIds = active.Select(list => list.Id).ToHashSet();
            var existingManagedTag = mode == ImportListCleanupMode.Tag
                ? connection.Query<int>("SELECT DISTINCT \"AuthorId\" FROM \"ImportListTagOwners\" WHERE \"TagId\" = @TagId", new { TagId = tagId }, transaction).ToHashSet()
                : new HashSet<int>();
            var eligible = candidates.Where(row => IsNativeRecording(row.RecordingId) &&
                    activeIds.Contains(row.ImportListId) &&
                    !exclusions.Contains(row.RecordingId) && !exclusions.Contains(row.ForeignAuthorId) &&
                    !lists.Any(list => list.Items.Any(item => item.BookId == row.RecordingId || item.SourceBookId == row.RecordingId || item.SourceEditionId == row.RecordingId ||
                        (item.BookId == null && item.AuthorId == row.ForeignAuthorId))) &&
                    (mode != ImportListCleanupMode.Unmonitor || row.Monitored) &&
                    (mode != ImportListCleanupMode.Tag || row.Tags?.Contains(tagId.Value) != true || existingManagedTag.Contains(row.AuthorId)))
                .ToList();
            result.Candidates.AddRange(eligible);
            if (mode != ImportListCleanupMode.Preview && eligible.Count > 0)
            {
                var evidence = new
                {
                    Mode = mode,
                    TagId = tagId,
                    Lists = lists.Select(list => new { list.Id, list.MembershipRevision, list.LastSuccessfulSync, list.Items }),
                    Exclusions = exclusions.Order(StringComparer.Ordinal),
                    ManagedTagAuthors = existingManagedTag.Order(),
                    Candidates = eligible.Select(row => new { row.BookId, row.RecordingId, row.Title, row.AuthorId, row.AuthorName, row.Monitored, Tags = (row.Tags ?? new HashSet<int>()).Order(), row.ForeignAuthorId, row.ImportListId })
                };
                result.ConfirmationToken = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(evidence)));
            }

            return result;
        }

        private List<ListState> ReadLists(IDbConnection connection, IDbTransaction transaction, bool write)
        {
            if (write && IsPostgres)
            {
                // Prevent new or modified definitions while evaluating a policy.
                // Membership Begin/Complete lock their definition first.
                connection.Execute("LOCK TABLE \"ImportLists\" IN SHARE MODE", transaction: transaction);
                connection.Query<int>("SELECT \"Id\" FROM \"ImportLists\" ORDER BY \"Id\" FOR UPDATE", transaction: transaction).ToList();
                connection.Execute("LOCK TABLE \"ImportListExclusions\", \"ImportListStatus\" IN SHARE MODE", transaction: transaction);
            }

            var lists = connection.Query<ListState>("""
                SELECT l."Id", l."Name", l."EnableAutomaticAdd", l."Implementation", l."TagExisting", l."Tags", l."MembershipRevision",
                    s."AttemptToken", s."SuccessfulRevision", s."LastSuccessfulSync",
                    EXISTS(SELECT 1 FROM "ImportListStatus" st WHERE st."ProviderId" = l."Id" AND st."MostRecentFailure" IS NOT NULL
                        AND (s."LastSuccessfulSync" IS NULL OR st."MostRecentFailure" >= s."LastSuccessfulSync")) AS "Failed"
                FROM "ImportLists" l LEFT JOIN "ImportListMembershipSnapshots" s ON s."ImportListId" = l."Id" ORDER BY l."Id"
                """, transaction: transaction).ToDictionary(list => list.Id);
            var exclusions = ReadExclusions(connection, transaction);
            foreach (var item in connection.Query<MembershipRow>("SELECT * FROM \"ImportListMembershipItems\" ORDER BY \"ImportListId\", \"Id\"", transaction: transaction))
            {
                item.MarkExcluded(exclusions);
                lists[item.ImportListId].Items.Add(item);
            }

            var credits = connection.Query<CatalogCredit>("""
                SELECT b."ForeignBookId" AS "BookId", m."ForeignAuthorId" AS "AuthorId"
                FROM "Books" b JOIN "AuthorMetadata" m ON m."Id" = b."AuthorMetadataId"
                """, transaction: transaction).ToLookup(row => row.BookId, StringComparer.Ordinal);
            foreach (var list in lists.Values)
            {
                foreach (var item in list.Items.Where(item => !item.Excluded && item.AuthorId != null))
                {
                    if (item.BookId == null)
                    {
                        if (!IsBookMembership(item))
                        {
                            list.EffectiveAuthors.Add(item.AuthorId);
                        }

                        continue;
                    }

                    var matching = credits[item.BookId].ToList();
                    if (matching.Count == 1 && matching[0].AuthorId == item.AuthorId)
                    {
                        list.EffectiveAuthors.Add(item.AuthorId);
                    }
                    else
                    {
                        list.CatalogConflict = true;
                    }
                }
            }

            return lists.Values.ToList();
        }

        private static HashSet<string> ReadExclusions(IDbConnection connection, IDbTransaction transaction) =>
            connection.Query<string>("SELECT \"ForeignId\" FROM \"ImportListExclusions\"", transaction: transaction).ToHashSet(StringComparer.Ordinal);

        private List<AuthorRow> ReadAuthors(IDbConnection connection, IDbTransaction transaction, bool write)
        {
            var sql = """
                SELECT a."Id" AS "AuthorId", m."ForeignAuthorId", a."Tags" FROM "Authors" a JOIN "AuthorMetadata" m ON m."Id" = a."AuthorMetadataId"
                ORDER BY a."Id"
                """;
            return connection.Query<AuthorRow>(sql + (write && IsPostgres ? " FOR UPDATE OF a" : ""), transaction: transaction).ToList();
        }

        private void LockBooks(IDbConnection connection, IDbTransaction transaction, List<int> ids)
        {
            if (IsPostgres && ids.Count > 0)
            {
                connection.Query<int>("SELECT \"Id\" FROM \"Books\" WHERE \"Id\" = ANY(@Ids) ORDER BY \"Id\" FOR UPDATE", new { Ids = ids.ToArray() }, transaction).ToList();
            }
        }

        private void LockAuthor(IDbConnection connection, IDbTransaction transaction, int authorId)
        {
            if (IsPostgres)
            {
                connection.Query<int>("SELECT \"Id\" FROM \"Authors\" WHERE \"Id\" = @AuthorId FOR UPDATE", new { AuthorId = authorId }, transaction).ToList();
            }
        }

        private static void WriteTags(IDbConnection connection, IDbTransaction transaction, AuthorRow author) =>
            connection.Execute("UPDATE \"Authors\" SET \"Tags\" = @Tags WHERE \"Id\" = @AuthorId", new { Tags = JsonSerializer.Serialize(author.Tags.Order()), author.AuthorId }, transaction);

        private void NotifyAuthors(List<int> changed)
        {
            if (changed.Count == 0)
            {
                return;
            }

            _events.PublishEvent(new AuthorsEditedEvent(changed));
            foreach (var authorId in changed)
            {
                _events.PublishEvent(new AuthorUpdatedEvent(_authors.GetAuthor(authorId)));
            }
        }

        private string IdsPredicate => IsPostgres ? "= ANY(@Ids)" : "IN @Ids";

        private bool IsPostgres => _database.DatabaseType == DatabaseType.PostgreSQL;

        private IDbTransaction Start(IDbConnection connection, bool write = true) =>
            connection.BeginTransaction(IsPostgres ? (write ? IsolationLevel.ReadCommitted : IsolationLevel.RepeatableRead) : IsolationLevel.Serializable);

        private class ListState
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool EnableAutomaticAdd { get; set; }
            public string Implementation { get; set; }
            public ImportListTagExisting TagExisting { get; set; }
            public HashSet<int> Tags { get; set; } = new ();
            public long MembershipRevision { get; set; }
            public long? SuccessfulRevision { get; set; }
            public string AttemptToken { get; set; }
            public DateTime? LastSuccessfulSync { get; set; }
            public bool Failed { get; set; }
            public List<ImportListMembershipItem> Items { get; } = new ();
            public HashSet<string> EffectiveAuthors { get; } = new (StringComparer.Ordinal);
            public bool CatalogConflict { get; set; }
            public bool Available => EnableAutomaticAdd && Implementation != null && !Implementation.StartsWith("Goodreads", StringComparison.Ordinal);
            public bool IsSuccessful => Available && SuccessfulRevision == MembershipRevision && LastSuccessfulSync.HasValue &&
                AttemptToken == string.Empty && !Failed;
            public bool IsReliableAbsence => IsSuccessful && !CatalogConflict && LastSuccessfulSync >= DateTime.UtcNow - MaximumAge &&
                Items.All(item => item.Excluded || (item.AuthorId != null &&
                    (!IsBookMembership(item) ||
                     (item.BookId != null && item.BookId == item.EditionId))));
            public bool ContainsAuthor(string foreignId) => EffectiveAuthors.Contains(foreignId);
        }

        private static bool IsBookMembership(ImportListMembershipItem item) => !string.IsNullOrWhiteSpace(item.SourceTitle) ||
            !string.IsNullOrWhiteSpace(item.SourceBookId) || !string.IsNullOrWhiteSpace(item.SourceEditionId);

        private static bool IsNativeRecording(string id)
        {
            if (!AudiobookId.IsNative(id))
            {
                return false;
            }

            try
            {
                return !AudiobookId.Parse(id).IsName;
            }
            catch (BookInfoException)
            {
                return false;
            }
        }

        private class CatalogCredit
        {
            public string BookId { get; set; }
            public string AuthorId { get; set; }
        }

        private class MembershipRow : ImportListMembershipItem
        {
            public int ImportListId { get; set; }
        }

        private class TagOwner
        {
            public int AuthorId { get; set; }
            public int TagId { get; set; }
            public int ImportListId { get; set; }
        }

        private class AuthorRow
        {
            public int AuthorId { get; set; }
            public string ForeignAuthorId { get; set; }
            public HashSet<int> Tags { get; set; } = new ();
        }

        private class CleanupRow : ImportListCleanupCandidate
        {
            public HashSet<int> Tags { get; set; } = new ();
            public string ForeignAuthorId { get; set; }
            public int ImportListId { get; set; }
        }
    }
}
