using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
    {
        List<DownloadHistory> FindByDownloadId(string downloadId);
        List<DownloadHistory> GetSubmissionAttempts();
        void DeleteByAuthorId(int authorId);
    }

    public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
    {
        public DownloadHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<DownloadHistory> FindByDownloadId(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId)
                .OrderByDescending(h => h.Date)
                .ToList();
        }

        public void DeleteByAuthorId(int authorId)
        {
            Delete(r => r.AuthorId == authorId);
        }

        public List<DownloadHistory> GetSubmissionAttempts()
        {
            return Query(history => history.EventType == DownloadHistoryEventType.DownloadSubmissionPending);
        }
    }
}
