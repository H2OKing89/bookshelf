using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.TPL;

namespace NzbDrone.Core.ImportLists
{
    public interface IFetchAndParseImportList
    {
        List<ImportListFetchBatch> Fetch();
        List<ImportListFetchBatch> FetchSingleList(ImportListDefinition definition);
    }

    public class FetchAndParseImportListService : IFetchAndParseImportList
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListStatusService _importListStatusService;
        private readonly IImportListMembershipRepository _membership;
        private readonly Logger _logger;

        public FetchAndParseImportListService(IImportListFactory importListFactory, IImportListStatusService importListStatusService, IImportListMembershipRepository membership, Logger logger)
        {
            _importListFactory = importListFactory;
            _importListStatusService = importListStatusService;
            _membership = membership;
            _logger = logger;
        }

        public List<ImportListFetchBatch> Fetch()
        {
            var result = new List<ImportListFetchBatch>();
            var taskFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);
            var tasks = new List<Task>();
            foreach (var importList in _importListFactory.AutomaticAddEnabled())
            {
                var lastSync = _importListStatusService.GetLastSyncListInfo(importList.Definition.Id);
                if (lastSync.HasValue && DateTime.UtcNow < lastSync.Value + importList.MinRefreshInterval)
                {
                    continue;
                }

                tasks.Add(taskFactory.StartNew(() =>
                {
                    var batch = FetchList(importList);
                    if (batch != null)
                    {
                        lock (result)
                        {
                            result.Add(batch);
                        }
                    }
                }).LogExceptions());
            }

            Task.WaitAll(tasks.ToArray());
            return result.OrderBy(batch => batch.Attempt.ImportListId).ToList();
        }

        public List<ImportListFetchBatch> FetchSingleList(ImportListDefinition definition)
        {
            if (definition == null || !definition.EnableAutomaticAdd || definition.Implementation.StartsWith("Goodreads", StringComparison.Ordinal))
            {
                return new List<ImportListFetchBatch>();
            }

            var importList = _importListFactory.GetInstance(definition);
            var batch = importList == null ? null : FetchList(importList);
            return batch == null ? new List<ImportListFetchBatch>() : new List<ImportListFetchBatch> { batch };
        }

        private ImportListFetchBatch FetchList(IImportList importList)
        {
            try
            {
                var attempt = _membership.Begin((ImportListDefinition)importList.Definition);
                if (attempt == null)
                {
                    return null;
                }

                var result = importList.FetchWithStatus();
                if (result == null || !result.Complete || result.Items == null)
                {
                    _logger.Debug("Import List {0} did not return a complete result; retaining its last successful membership.", importList.Definition.Name);
                    return null;
                }

                var items = ImportListItemIdentity.DistinctItems(result.Items);
                foreach (var item in items)
                {
                    item.ImportListId = importList.Definition.Id;
                    item.ImportList = importList.Definition.Name;
                }

                return new ImportListFetchBatch(attempt, items);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error during Import List Sync of {0}", importList.Definition.Name);
                return null;
            }
        }
    }
}
