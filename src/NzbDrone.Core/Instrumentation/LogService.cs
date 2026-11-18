using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Instrumentation
{
    public interface ILogService
    {
        PagingSpec<Log> Paged(PagingSpec<Log> pagingSpec);
    }

    public class LogService : ILogService, IExecute<ClearLogCommand>
    {
        private readonly ILogRepository _logRepository;
        private readonly ILogDatabase _logDatabase;

        public LogService(ILogRepository logRepository, ILogDatabase logDatabase)
        {
            _logRepository = logRepository;
            _logDatabase = logDatabase;
        }

        public PagingSpec<Log> Paged(PagingSpec<Log> pagingSpec)
        {
            if (!_logDatabase.IsEnabled)
            {
                pagingSpec.TotalRecords = 0;
                pagingSpec.Records = new List<Log>();
                return pagingSpec;
            }

            return _logRepository.GetPaged(pagingSpec);
        }

        public void Execute(ClearLogCommand message)
        {
            if (_logDatabase.IsEnabled)
            {
                _logRepository.Purge(vacuum: true);
            }
        }
    }
}
