using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class TrimLogDatabase : IHousekeepingTask
    {
        private readonly ILogRepository _logRepo;
        private readonly ILogDatabase _logDatabase;

        public TrimLogDatabase(ILogRepository logRepo, ILogDatabase logDatabase)
        {
            _logRepo = logRepo;
            _logDatabase = logDatabase;
        }

        public void Clean()
        {
            if (_logDatabase.IsEnabled)
            {
                _logRepo.Trim();
            }
        }
    }
}
