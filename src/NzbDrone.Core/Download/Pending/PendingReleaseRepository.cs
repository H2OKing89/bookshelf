using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Pending
{
    public interface IPendingReleaseRepository : IBasicRepository<PendingRelease>
    {
        void DeleteByAuthorId(int authorId);
        List<PendingRelease> AllByAuthorId(int authorId);
        List<PendingRelease> WithoutFallback();
        List<int> GetAuthorIds();
    }

    public class PendingReleaseRepository : BasicRepository<PendingRelease>, IPendingReleaseRepository
    {
        public PendingReleaseRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void DeleteByAuthorId(int authorId)
        {
            Delete(x => x.AuthorId == authorId);
        }

        public List<PendingRelease> AllByAuthorId(int authorId)
        {
            return Query(p => p.AuthorId == authorId);
        }

        public List<PendingRelease> WithoutFallback()
        {
            return Query(p => p.Reason != PendingReleaseReason.Fallback);
        }

        public List<int> GetAuthorIds()
        {
            using var connection = _database.OpenConnection();
            return connection.Query<int>("SELECT DISTINCT \"AuthorId\" FROM \"PendingReleases\"").ToList();
        }
    }
}
