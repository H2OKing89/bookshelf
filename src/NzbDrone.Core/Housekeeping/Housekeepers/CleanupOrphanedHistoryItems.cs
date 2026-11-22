using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedHistoryItems : IHousekeepingTask
    {
        private readonly IMainDatabase _database;
        private readonly IEventAggregator _eventAggregator;

        public CleanupOrphanedHistoryItems(IMainDatabase database, IEventAggregator eventAggregator)
        {
            _database = database;
            _eventAggregator = eventAggregator;
        }

        public void Clean()
        {
            int deleted;
            using (var connection = _database.OpenConnection())
            {
                deleted = connection.Execute(@"DELETE FROM ""History""
                                               WHERE ""Id"" IN (
                                                   SELECT ""History"".""Id"" FROM ""History""
                                                   LEFT JOIN ""Authors"" ON ""History"".""AuthorId"" = ""Authors"".""Id""
                                                   LEFT JOIN ""Books"" ON ""History"".""BookId"" = ""Books"".""Id""
                                                   WHERE ""Authors"".""Id"" IS NULL OR ""Books"".""Id"" IS NULL)");
            }

            if (deleted > 0)
            {
                _eventAggregator.PublishEvent(new HistoryChangedEvent());
            }
        }
    }
}
