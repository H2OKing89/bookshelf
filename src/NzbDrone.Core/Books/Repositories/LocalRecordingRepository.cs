using System.Collections.Generic;
using System.Data;
using System.Net;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface ILocalRecordingRepository
    {
        void Create(Book book, Edition edition, List<BookFile> files);
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

        public void Create(Book book, Edition edition, List<BookFile> files)
        {
            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var authorExists = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM \"Authors\" WHERE \"Id\" = @AuthorId AND \"AuthorMetadataId\" = @AuthorMetadataId",
                    new { book.AuthorId, book.AuthorMetadataId },
                    transaction);
                if (authorExists != 1)
                {
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "The selected author changed. Refresh your library and try again.");
                }

                new BasicRepository<Book>(_database, _events).Insert(connection, transaction, book);
                edition.BookId = book.Id;
                new BasicRepository<Edition>(_database, _events).Insert(connection, transaction, edition);

                foreach (var file in files)
                {
                    // Guard against a scan or another request claiming/changing the file
                    // between validation and commit. Preserve its history and provenance.
                    var changed = connection.Execute(
                        "UPDATE \"BookFiles\" SET \"EditionId\" = @EditionId, \"Part\" = @Part " +
                        "WHERE \"Id\" = @Id AND \"EditionId\" = 0 AND \"Path\" = @Path " +
                        "AND \"Size\" = @Size AND \"Modified\" = @Modified",
                        new { EditionId = edition.Id, file.Part, file.Id, file.Path, file.Size, file.Modified },
                        transaction);
                    if (changed != 1)
                    {
                        throw new NzbDroneClientException(HttpStatusCode.Conflict, "A selected file changed or was mapped. Refresh Unmapped and try again.");
                    }
                }

                transaction.Commit();
            }
        }
    }
}
