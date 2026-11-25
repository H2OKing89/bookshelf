using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface ILocalRecordingRepository
    {
        void Create(Book book, Edition edition, List<BookFile> files, Dictionary<string, string> provenance = null, RootFolder newAuthorRoot = null);
        LocalRecording Get(int bookId);
        void Update(int bookId, LocalRecording recording);
    }

    public class LocalRecordingRepository : ILocalRecordingRepository
    {
        private readonly IMainDatabase _database;
        private readonly IEventAggregator _events;

        public LocalRecordingRepository(IMainDatabase database, IEventAggregator events)
        {
            _database = database;
            _events = events;
        }

        public void Create(Book book, Edition edition, List<BookFile> files, Dictionary<string, string> provenance = null, RootFolder newAuthorRoot = null)
        {
            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.SQLite ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted))
            {
                if (newAuthorRoot != null)
                {
                    InsertLocalAuthor(connection, transaction, book.Author.Value, newAuthorRoot);
                    book.AuthorMetadataId = book.Author.Value.AuthorMetadataId;
                }
                else if (_database.DatabaseType == DatabaseType.PostgreSQL)
                {
                    // Acquire the writer-table lock in the same order as author
                    // reconciliation. The row lock below excludes normal deletes.
                    connection.Execute("LOCK TABLE \"Authors\" IN ROW EXCLUSIVE MODE", transaction: transaction);
                }

                var authorQuery = "SELECT \"Id\" FROM \"Authors\" WHERE \"Id\" = @AuthorId AND \"AuthorMetadataId\" = @AuthorMetadataId";
                if (_database.DatabaseType == DatabaseType.PostgreSQL)
                {
                    authorQuery += " FOR UPDATE";
                }

                var authorExists = connection.ExecuteScalar<int>(
                    authorQuery,
                    new { book.AuthorId, book.AuthorMetadataId },
                    transaction);
                if (authorExists != book.AuthorId || authorExists <= 0)
                {
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "The selected author changed. Refresh your library and try again.");
                }

                new BasicRepository<Book>(_database, _events).Insert(connection, transaction, book);
                edition.BookId = book.Id;
                new BasicRepository<Edition>(_database, _events).Insert(connection, transaction, edition);
                connection.Execute(
                    "UPDATE \"Editions\" SET \"LocalMetadataRevision\" = @Revision, \"LocalMetadataProvenance\" = @Provenance WHERE \"Id\" = @Id",
                    new { edition.Id, Revision = Guid.NewGuid().ToString("N"), Provenance = provenance },
                    transaction);

                foreach (var file in files)
                {
                    // Guard against a scan or another request claiming/changing the file
                    // between validation and commit. Preserve its history and provenance.
                    // Older SQLite databases may store an equivalent timestamp with a
                    // different text representation. Compare the instant, then retain
                    // the exact storage value for the guarded update.
                    var modified = connection.QuerySingleOrDefault<StoredModification>(
                        "SELECT \"Modified\", CAST(\"Modified\" AS TEXT) AS \"StorageValue\" FROM \"BookFiles\" WHERE \"Id\" = @Id",
                        new { file.Id },
                        transaction);
                    if (modified == null || modified.Modified != file.Modified || modified.StorageValue == null)
                    {
                        throw Conflict("A selected file changed or was mapped. Refresh Unmapped and try again.");
                    }

                    var changed = connection.Execute(
                        "UPDATE \"BookFiles\" SET \"EditionId\" = @EditionId, \"Part\" = @Part " +
                        "WHERE \"Id\" = @Id AND \"EditionId\" = 0 AND \"Path\" = @Path " +
                        "AND \"Size\" = @Size AND CAST(\"Modified\" AS TEXT) = @Modified",
                        new { EditionId = edition.Id, file.Part, file.Id, file.Path, file.Size, Modified = modified.StorageValue },
                        transaction);
                    if (changed != 1)
                    {
                        throw new NzbDroneClientException(HttpStatusCode.Conflict, "A selected file changed or was mapped. Refresh Unmapped and try again.");
                    }
                }

                transaction.Commit();
            }
        }

        private void InsertLocalAuthor(IDbConnection connection, IDbTransaction transaction, Author author, RootFolder root)
        {
            // The existing Authors table has non-unique paths. Serialize the
            // check and insert on PostgreSQL as SQLite does for writers.
            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                connection.Execute("LOCK TABLE \"AuthorMetadata\", \"Authors\" IN SHARE ROW EXCLUSIVE MODE", transaction: transaction);
            }

            var rootExists = connection.ExecuteScalar<int>(
                """SELECT COUNT(*) FROM "RootFolders" WHERE "Id" = @Id AND "Path" = @Path AND "DefaultQualityProfileId" = @DefaultQualityProfileId AND "DefaultMetadataProfileId" = @DefaultMetadataProfileId AND "IsCalibreLibrary" = @IsCalibreLibrary""",
                root,
                transaction);
            var qualityProfileExists = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"QualityProfiles\" WHERE \"Id\" = @QualityProfileId", new { author.QualityProfileId }, transaction);
            var metadataProfileExists = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"MetadataProfiles\" WHERE \"Id\" = @MetadataProfileId", new { author.MetadataProfileId }, transaction);
            if (rootExists != 1 || qualityProfileExists != 1 || metadataProfileExists != 1)
            {
                throw Conflict("The root folder or its default profiles changed. Refresh the selected files and try again.");
            }

            var authors = connection.Query<Author>("SELECT \"Path\", \"CleanName\" FROM \"Authors\"", transaction: transaction);
            if (authors.Any(existing => existing.CleanName == author.CleanName || existing.Path.PathEquals(author.Path) ||
                existing.Path.IsParentPath(author.Path) || author.Path.IsParentPath(existing.Path)))
            {
                throw Conflict("A library author already has this name or an overlapping folder. Select the existing author instead.");
            }

            new BasicRepository<AuthorMetadata>(_database, _events).Insert(connection, transaction, author.Metadata.Value);
            author.AuthorMetadataId = author.Metadata.Value.Id;
            new BasicRepository<Author>(_database, _events).Insert(connection, transaction, author);
        }

        public LocalRecording Get(int bookId)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.PostgreSQL ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted);
            var recording = Read(connection, transaction, bookId);
            var files = connection.Query<int>("SELECT \"Id\" FROM \"BookFiles\" WHERE \"EditionId\" = @EditionId ORDER BY \"Part\", \"Id\"", new { recording.EditionId }, transaction).ToList();
            transaction.Commit();

            return new LocalRecording
            {
                BookId = recording.BookId,
                EditionId = recording.EditionId,
                RecordingId = recording.RecordingId,
                Revision = recording.Revision,
                FileIds = files,
                AuthorId = recording.AuthorId,
                AuthorName = recording.AuthorName,
                Title = recording.Title,
                Subtitle = recording.Subtitle,
                Narrators = recording.Narrators,
                Publisher = recording.Publisher,
                Language = recording.Language,
                Year = recording.Year,
                RuntimeLengthMinutes = recording.RuntimeLengthMinutes,
                ChapterMetadata = recording.ChapterMetadata,
                Provenance = recording.Provenance
            };
        }

        public void Update(int bookId, LocalRecording recording)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(_database.DatabaseType == DatabaseType.SQLite ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);

            // The two metadata rows and revision are committed together. Claim
            // the book first so two editors serialize on the same recording.
            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                connection.Execute("LOCK TABLE \"Books\", \"Editions\" IN ROW EXCLUSIVE MODE", transaction: transaction);
            }

            var lockSql = _database.DatabaseType == DatabaseType.PostgreSQL
                ? "SELECT \"Id\" FROM \"Books\" WHERE \"Id\" = @bookId FOR UPDATE"
                : "SELECT \"Id\" FROM \"Books\" WHERE \"Id\" = @bookId";
            connection.ExecuteScalar<int>(lockSql, new { bookId }, transaction);
            var previous = Read(connection, transaction, bookId);
            if (previous.Revision != recording.Revision ||
                (recording.RecordingId != null && recording.RecordingId != previous.RecordingId) ||
                (recording.EditionId != 0 && recording.EditionId != previous.EditionId) ||
                (recording.AuthorId != 0 && recording.AuthorId != previous.AuthorId) ||
                !string.IsNullOrWhiteSpace(recording.NewAuthorName))
            {
                throw Conflict("The local recording changed. Reload its metadata before saving again.");
            }

            // Keep existing precise/unknown dates when the year was not edited.
            var releaseDate = previous.Year == recording.Year ? previous.ReleaseDate : LocalRecordingMetadata.ReleaseDate(recording.Year);
            var bookReleaseDate = previous.Year == recording.Year ? previous.BookReleaseDate : releaseDate;
            var placeholder = previous.Year == recording.Year ? previous.ReleaseDateIsPlaceholder : recording.Year.HasValue;
            var updated = connection.Execute(
                """
                UPDATE "Editions" SET "Title" = @Title, "Subtitle" = @Subtitle, "Narrators" = @Narrators,
                    "Language" = @Language, "Publisher" = @Publisher, "ChapterMetadata" = @ChapterMetadata,
                    "ReleaseDate" = @ReleaseDate, "SourceReleaseDate" = @SourceReleaseDate, "ReleaseDateIsPlaceholder" = @Placeholder,
                    "LocalMetadataRevision" = @NewRevision, "LocalMetadataProvenance" = @Provenance
                WHERE "Id" = @EditionId AND "BookId" = @bookId AND "ForeignEditionId" = @RecordingId
                    AND (("LocalMetadataRevision" IS NULL AND @WasLegacy) OR "LocalMetadataRevision" = @ExpectedRevision)
                """,
                new
                {
                    recording.Title, recording.Subtitle, recording.Narrators, recording.Language, recording.Publisher, recording.ChapterMetadata,
                    ReleaseDate = releaseDate, SourceReleaseDate = previous.Year == recording.Year ? previous.SourceReleaseDate : null,
                    Placeholder = placeholder, NewRevision = Guid.NewGuid().ToString("N"),
                    Provenance = LocalRecordingMetadata.EditSources(previous, recording),
                    previous.EditionId, bookId, previous.RecordingId, ExpectedRevision = previous.Revision, previous.WasLegacy
                },
                transaction);

            if (updated != 1 || connection.Execute(
                    "UPDATE \"Books\" SET \"Title\" = @Title, \"CleanTitle\" = @CleanTitle, \"ReleaseDate\" = @ReleaseDate WHERE \"Id\" = @bookId AND \"ForeignBookId\" = @RecordingId",
                    new { recording.Title, CleanTitle = recording.Title.CleanBookTitle(), ReleaseDate = bookReleaseDate, bookId, previous.RecordingId },
                    transaction) != 1)
            {
                throw Conflict("The local recording changed. Reload its metadata before saving again.");
            }

            transaction.Commit();
        }

        private static LocalRecordingRow Read(IDbConnection connection, IDbTransaction transaction, int bookId)
        {
            var rows = connection.Query<LocalRecordingRow>(
                """
                SELECT b."Id" AS "BookId", b."ForeignBookId" AS "RecordingId", b."Title" AS "Title", b."ReleaseDate" AS "BookReleaseDate",
                    e."Id" AS "EditionId", e."ForeignEditionId" AS "EditionIdentity", e."Asin", e."AudibleRegion",
                    e."IsEbook", e."Subtitle", e."Narrators", e."Publisher", e."Language", e."ReleaseDate", e."SourceReleaseDate", e."ReleaseDateIsPlaceholder",
                    e."RuntimeLengthMinutes", e."ChapterMetadata", e."LocalMetadataRevision" AS "Revision",
                    e."LocalMetadataProvenance" AS "Provenance", a."Id" AS "AuthorId", m."Name" AS "AuthorName"
                FROM "Books" b JOIN "Editions" e ON e."BookId" = b."Id"
                    JOIN "AuthorMetadata" m ON m."Id" = b."AuthorMetadataId"
                    JOIN "Authors" a ON a."AuthorMetadataId" = m."Id"
                WHERE b."Id" = @bookId
                """,
                new { bookId },
                transaction).ToList();

            if (rows.Count == 0 || !LocalRecordingId.IsLocal(rows[0].RecordingId))
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Local recording not found.");
            }

            if (rows.Count != 1 || rows[0].EditionIdentity != rows[0].RecordingId || rows[0].IsEbook ||
                !string.IsNullOrEmpty(rows[0].Asin) || !string.IsNullOrEmpty(rows[0].AudibleRegion))
            {
                throw Conflict("This recording has conflicting catalog identities. Review the recording before changing its metadata.");
            }

            var current = rows[0];
            current.Year = current.ReleaseDate?.Year;
            current.WasLegacy = current.Revision == null;
            current.Revision ??= $"legacy:{bookId}:{current.RecordingId}";
            if (current.Provenance == null || current.Provenance.Count == 0)
            {
                current.Provenance = LocalRecordingMetadata.Sources(current, "legacy");
            }

            return current;
        }

        private static NzbDroneClientException Conflict(string message) => new NzbDroneClientException(HttpStatusCode.Conflict, message);

        private sealed class StoredModification
        {
            public DateTime? Modified { get; set; }
            public string StorageValue { get; set; }
        }

        private class LocalRecordingRow : LocalRecording
        {
            public string EditionIdentity { get; set; }
            public bool WasLegacy { get; set; }
            public string Asin { get; set; }
            public string AudibleRegion { get; set; }
            public bool IsEbook { get; set; }
            public DateTime? BookReleaseDate { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public DateTime? SourceReleaseDate { get; set; }
            public bool ReleaseDateIsPlaceholder { get; set; }
        }
    }
}
