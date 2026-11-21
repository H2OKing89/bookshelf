using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Npgsql;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles.RecordingReview
{
    public interface IRecordingReviewRepository
    {
        List<Edition> FindCatalogEditions(string[] asins, string[] recordingIds, int limit);
        bool Associate(BookFile file, Edition edition, Book book, Author author);
    }

    public class RecordingReviewRepository : IRecordingReviewRepository
    {
        private readonly IMainDatabase _database;

        public RecordingReviewRepository(IMainDatabase database)
        {
            _database = database;
        }

        public List<Edition> FindCatalogEditions(string[] asins, string[] recordingIds, int limit)
        {
            using var connection = _database.OpenConnection();
            var identityPredicate = connection is NpgsqlConnection
                ? @"""Asin"" = ANY(@Asins) OR ""ForeignEditionId"" = ANY(@RecordingIds)"
                : @"""Asin"" IN @Asins OR ""ForeignEditionId"" IN @RecordingIds";
            return connection.Query<Edition>(
                $@"SELECT * FROM ""Editions""
                    WHERE ""IsEbook"" = FALSE AND ({identityPredicate})
                    ORDER BY ""Id"" LIMIT @Limit",
                new { Asins = asins, RecordingIds = recordingIds, Limit = limit }).ToList();
        }

        public bool Associate(BookFile file, Edition edition, Book book, Author author)
        {
            using var connection = _database.OpenConnection();
            var isPostgres = connection is NpgsqlConnection;
            using var transaction = connection.BeginTransaction(isPostgres ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);

            try
            {
                if (isPostgres)
                {
                    // PostgreSQL permits concurrent writes to different files. Guard
                    // the empty-target check against a second file being attached
                    // during this short metadata-only update.
                    connection.Execute(@"LOCK TABLE ""BookFiles"" IN SHARE ROW EXCLUSIVE MODE NOWAIT", transaction: transaction);
                }

                // Compare the timestamp by value first. Older SQLite rows and
                // restored databases can encode the same instant differently.
                // Keep the exact storage value for the guarded update.
                var modified = connection.QuerySingleOrDefault<StoredModification>(
                    @"SELECT ""Modified"", CAST(""Modified"" AS TEXT) AS ""StorageValue"" FROM ""BookFiles"" WHERE ""Id"" = @Id",
                    new { file.Id },
                    transaction);
                if (modified?.Modified != file.Modified || modified.StorageValue == null)
                {
                    return false;
                }

                var changed = connection.Execute(
                    @"UPDATE ""BookFiles"" SET ""EditionId"" = @EditionId, ""Part"" = 1
                        WHERE ""Id"" = @Id AND ""EditionId"" = 0 AND COALESCE(""CalibreId"", 0) = 0
                        AND ""Path"" = @Path AND ""Size"" = @Size AND CAST(""Modified"" AS TEXT) = @Modified
                        AND COALESCE(""Part"", 0) = @Part
                        AND EXISTS (
                            SELECT 1 FROM ""Editions"" e
                            JOIN ""Books"" b ON b.""Id"" = e.""BookId""
                            JOIN ""Authors"" a ON a.""AuthorMetadataId"" = b.""AuthorMetadataId""
                            WHERE e.""Id"" = @EditionId AND e.""ForeignEditionId"" = @ForeignEditionId
                            AND e.""BookId"" = @BookId AND e.""IsEbook"" = FALSE AND e.""AudibleRegion"" = @Region
                            AND COALESCE(e.""Asin"", '') = @Asin
                            AND b.""ForeignBookId"" = @ForeignBookId AND b.""AuthorMetadataId"" = @AuthorMetadataId
                            AND a.""Id"" = @AuthorId AND a.""Path"" = @AuthorPath
                        )
                        AND NOT EXISTS (
                            SELECT 1 FROM ""BookFiles"" f
                            JOIN ""Editions"" e ON f.""EditionId"" = e.""Id""
                            WHERE e.""BookId"" = @BookId
                        )
                        AND NOT EXISTS (
                            SELECT 1 FROM ""BookFiles"" f WHERE f.""Path"" = @Path AND f.""Id"" <> @Id
                        )",
                    new
                    {
                        file.Id,
                        file.Path,
                        file.Size,
                        Modified = modified.StorageValue,
                        file.Part,
                        EditionId = edition.Id,
                        edition.ForeignEditionId,
                        Region = edition.AudibleRegion,
                        Asin = edition.Asin ?? string.Empty,
                        edition.BookId,
                        book.ForeignBookId,
                        book.AuthorMetadataId,
                        AuthorId = author.Id,
                        AuthorPath = author.Path
                    },
                    transaction);

                transaction.Commit();
                return changed == 1;
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                return false;
            }
        }

        private sealed class StoredModification
        {
            public DateTime? Modified { get; set; }
            public string StorageValue { get; set; }
        }
    }
}
