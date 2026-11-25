using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ImportLists
{
    public interface IImportListMembershipRepository
    {
        ImportListMembershipAttempt Begin(ImportListDefinition definition);
        bool Complete(ImportListMembershipAttempt attempt, IReadOnlyList<ImportListMembershipItem> items);
        ImportListMembershipSnapshot Get(int importListId);
    }

    public class ImportListMembershipRepository : IImportListMembershipRepository
    {
        private readonly IMainDatabase _database;

        public ImportListMembershipRepository(IMainDatabase database)
        {
            _database = database;
        }

        public ImportListMembershipAttempt Begin(ImportListDefinition definition)
        {
            using var connection = _database.OpenConnection();
            using var transaction = BeginWrite(connection);
            var current = LockDefinition(connection, transaction, definition.Id);
            if (current == null || !current.EnableAutomaticAdd || definition.MembershipRevision != current.MembershipRevision)
            {
                return null;
            }

            var attempt = new ImportListMembershipAttempt(definition.Id, current.MembershipRevision, Guid.NewGuid().ToString("N"));
            const string beginSql = """
                INSERT INTO "ImportListMembershipSnapshots" ("ImportListId", "AttemptToken") VALUES (@ImportListId, @Token)
                ON CONFLICT ("ImportListId") DO UPDATE SET "AttemptToken" = @Token
                """;
            connection.Execute(beginSql, attempt, transaction);
            transaction.Commit();
            return attempt;
        }

        public bool Complete(ImportListMembershipAttempt attempt, IReadOnlyList<ImportListMembershipItem> items)
        {
            using var connection = _database.OpenConnection();
            using var transaction = BeginWrite(connection);
            var current = LockDefinition(connection, transaction, attempt.ImportListId);
            if (current == null || !current.EnableAutomaticAdd || current.MembershipRevision != attempt.Revision)
            {
                return false;
            }

            // Claim only the most recently started fetch for this revision.
            // Consume its token in the same transaction as the membership rows.
            const string replaceSql = """
                UPDATE "ImportListMembershipSnapshots" SET "SuccessfulRevision" = @Revision, "LastSuccessfulSync" = @Now, "AttemptToken" = ''
                WHERE "ImportListId" = @ImportListId AND "AttemptToken" = @Token
                """;
            var changed = connection.Execute(replaceSql, new { attempt.ImportListId, attempt.Revision, attempt.Token, Now = DateTime.UtcNow }, transaction);
            if (changed != 1)
            {
                return false;
            }

            connection.Execute("DELETE FROM \"ImportListMembershipItems\" WHERE \"ImportListId\" = @ImportListId", attempt, transaction);
            const string insertSql = """
                INSERT INTO "ImportListMembershipItems" ("ImportListId", "SourceAuthor", "SourceTitle", "SourceAuthorId", "SourceBookId", "SourceEditionId", "AuthorId", "BookId", "EditionId")
                VALUES (@ImportListId, @SourceAuthor, @SourceTitle, @SourceAuthorId, @SourceBookId, @SourceEditionId, @AuthorId, @BookId, @EditionId)
                """;
            var parameters = items.Select(item => new
            {
                attempt.ImportListId,
                item.SourceAuthor,
                item.SourceTitle,
                item.SourceAuthorId,
                item.SourceBookId,
                item.SourceEditionId,
                item.AuthorId,
                item.BookId,
                item.EditionId
            });
            connection.Execute(insertSql, parameters, transaction);
            transaction.Commit();
            return true;
        }

        public ImportListMembershipSnapshot Get(int importListId)
        {
            using var connection = _database.OpenConnection();

            // One consistent read of definition, snapshot and exclusion state.
            using var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.PostgreSQL ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable);
            const string headerSql = """
                SELECT l."Id" AS "ImportListId", s."LastSuccessfulSync",
                    CASE WHEN l."EnableAutomaticAdd" AND s."SuccessfulRevision" = l."MembershipRevision" THEN TRUE ELSE FALSE END AS "IsCurrent"
                FROM "ImportLists" l LEFT JOIN "ImportListMembershipSnapshots" s ON s."ImportListId" = l."Id"
                WHERE l."Id" = @ImportListId
                """;
            var header = connection.QuerySingleOrDefault<ImportListMembershipSnapshot>(headerSql, new { ImportListId = importListId }, transaction);
            if (header == null)
            {
                throw new ModelNotFoundException(typeof(ImportListDefinition), importListId);
            }

            const string itemsSql = """
                SELECT m.* FROM "ImportListMembershipItems" m WHERE m."ImportListId" = @ImportListId ORDER BY m."Id"
                """;
            header.Items = connection.Query<ImportListMembershipItem>(itemsSql, new { ImportListId = importListId }, transaction).ToList();
            var exclusions = connection.Query<string>("SELECT \"ForeignId\" FROM \"ImportListExclusions\"", transaction: transaction).ToHashSet(StringComparer.Ordinal);
            foreach (var item in header.Items)
            {
                item.MarkExcluded(exclusions);
            }

            transaction.Commit();
            return header;
        }

        private IDbTransaction BeginWrite(IDbConnection connection) => connection.BeginTransaction(_database.DatabaseType == DatabaseType.PostgreSQL ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);

        private ImportListDefinition LockDefinition(IDbConnection connection, IDbTransaction transaction, int importListId)
        {
            const string sql = "SELECT \"Id\", \"EnableAutomaticAdd\", \"MembershipRevision\" FROM \"ImportLists\" WHERE \"Id\" = @ImportListId";
            return connection.QuerySingleOrDefault<ImportListDefinition>(sql + (_database.DatabaseType == DatabaseType.PostgreSQL ? " FOR UPDATE" : ""), new { ImportListId = importListId }, transaction);
        }
    }
}
