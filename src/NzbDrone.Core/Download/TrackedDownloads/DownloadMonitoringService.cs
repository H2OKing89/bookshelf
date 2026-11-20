using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class DownloadMonitoringService : IExecute<RefreshMonitoredDownloadsCommand>,
                                             IExecute<CheckForFinishedDownloadCommand>,
                                             IHandle<BookGrabbedEvent>,
                                             IHandle<TrackImportedEvent>,
                                             IHandle<DownloadsProcessedEvent>,
                                             IHandle<TrackedDownloadsRemovedEvent>,
                                             IHandle<ProviderUpdatedEvent<IDownloadClient>>,
                                             IHandle<ProviderDeletedEvent<IDownloadClient>>
    {
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _manageCommandQueue;
        private readonly IConfigService _configService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly Logger _logger;
        private readonly Debouncer _refreshDebounce;

        public DownloadMonitoringService(IDownloadClientStatusService downloadClientStatusService,
                                         IDownloadClientFactory downloadClientFactory,
                                         IEventAggregator eventAggregator,
                                         IManageCommandQueue manageCommandQueue,
                                         IConfigService configService,
                                         IFailedDownloadService failedDownloadService,
                                         ICompletedDownloadService completedDownloadService,
                                         ITrackedDownloadService trackedDownloadService,
                                         Logger logger)
        {
            _downloadClientStatusService = downloadClientStatusService;
            _downloadClientFactory = downloadClientFactory;
            _eventAggregator = eventAggregator;
            _manageCommandQueue = manageCommandQueue;
            _configService = configService;
            _failedDownloadService = failedDownloadService;
            _completedDownloadService = completedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _logger = logger;

            _refreshDebounce = new Debouncer(QueueRefresh, TimeSpan.FromSeconds(5));
        }

        private void QueueRefresh()
        {
            _manageCommandQueue.Push(new RefreshMonitoredDownloadsCommand(), CommandPriority.High);
        }

        private void Refresh()
        {
            _refreshDebounce.Pause();
            try
            {
                var downloadClients = _downloadClientFactory.DownloadHandlingEnabled();
                foreach (var tracked in _trackedDownloadService.GetTrackedDownloads() ?? new List<TrackedDownload>())
                {
                    tracked.IsClientAvailable = false;
                    tracked.IsTrackable = false;
                }

                var trackedDownloads = new List<TrackedDownload>();

                foreach (var downloadClient in downloadClients)
                {
                    var clientTrackedDownloads = ProcessClientDownloads(downloadClient);

                    trackedDownloads.AddRange(clientTrackedDownloads.Where(DownloadIsTrackable));
                }

                _trackedDownloadService.UpdateTrackable(trackedDownloads);

                // Keep the last known queue visible during outages, while IsTrackable prevents
                // imports and client cleanup from acting on stale or partially processed polls.
                var visible = (_trackedDownloadService.GetTrackedDownloads() ?? trackedDownloads).Where(DownloadIsTrackable).ToList();
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(visible));
                _manageCommandQueue.Push(new ProcessMonitoredDownloadsCommand(), CommandPriority.High);
            }
            finally
            {
                _refreshDebounce.Resume();
            }
        }

        private List<TrackedDownload> ProcessClientDownloads(IDownloadClient downloadClient)
        {
            var trackedDownloads = new List<TrackedDownload>();

            try
            {
                // Materialize and validate the entire lazy response before changing cache membership.
                var downloadClientItems = downloadClient.GetItems().ToList();
                if (downloadClientItems.Any(item => item == null || item.DownloadId.IsNullOrWhiteSpace() ||
                    item.DownloadClientInfo?.Id != downloadClient.Definition.Id) ||
                    downloadClientItems.Select(item => item.DownloadId).Distinct(StringComparer.Ordinal).Count() != downloadClientItems.Count)
                {
                    throw new InvalidOperationException("The download client returned an invalid or duplicate download identity.");
                }

                var complete = true;
                foreach (var downloadItem in downloadClientItems)
                {
                    var item = ProcessClientItem(downloadClient, downloadItem);
                    complete &= item != null;
                    trackedDownloads.AddIfNotNull(item);
                }

                if (!complete)
                {
                    _downloadClientStatusService.RecordFailure(downloadClient.Definition.Id);
                    return new List<TrackedDownload>();
                }

                // Validate tracking for the entire response before checking import/failure state.
                foreach (var tracked in trackedDownloads)
                {
                    complete &= CheckClientItem(tracked);
                }

                if (!complete)
                {
                    _downloadClientStatusService.RecordFailure(downloadClient.Definition.Id);
                    return new List<TrackedDownload>();
                }

                _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);
                _trackedDownloadService.StopTrackingMissing(downloadClient.Definition.Id, downloadClientItems.Select(item => item.DownloadId).ToList());
                foreach (var tracked in trackedDownloads)
                {
                    tracked.IsClientAvailable = true;
                }
            }
            catch (Exception ex)
            {
                _downloadClientStatusService.RecordFailure(downloadClient.Definition.Id);
                _logger.Warn(ex, "Unable to retrieve queue and history items from " + downloadClient.Definition.Name);
                return new List<TrackedDownload>();
            }

            return trackedDownloads;
        }

        private TrackedDownload ProcessClientItem(IDownloadClient downloadClient, DownloadClientItem downloadItem)
        {
            try
            {
                return _trackedDownloadService.TrackDownload((DownloadClientDefinition)downloadClient.Definition, downloadItem);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't process tracked download {0}", downloadItem.Title);
                return null;
            }
        }

        private bool CheckClientItem(TrackedDownload trackedDownload)
        {
            try
            {
                if (trackedDownload.State == TrackedDownloadState.Downloading ||
                     trackedDownload.State == TrackedDownloadState.ImportPending ||
                     trackedDownload.State == TrackedDownloadState.ImportBlocked ||
                     trackedDownload.State == TrackedDownloadState.ImportFailed)
                {
                    _failedDownloadService.Check(trackedDownload);
                    _completedDownloadService.Check(trackedDownload);
                }

                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't process tracked download {0}", trackedDownload.DownloadItem.Title);
            }

            return false;
        }

        private bool DownloadIsTrackable(TrackedDownload trackedDownload)
        {
            // If the download has already been imported or failed or the user ignored it don't track it
            if (trackedDownload.State == TrackedDownloadState.Imported ||
                trackedDownload.State == TrackedDownloadState.DownloadFailed ||
                trackedDownload.State == TrackedDownloadState.Ignored)
            {
                return false;
            }

            // If CDH is disabled and the download status is complete don't track it
            if (!_configService.EnableCompletedDownloadHandling && trackedDownload.DownloadItem.Status == DownloadItemStatus.Completed)
            {
                return false;
            }

            return true;
        }

        public void Execute(RefreshMonitoredDownloadsCommand message)
        {
            Refresh();
        }

        public void Execute(CheckForFinishedDownloadCommand message)
        {
            _logger.Warn("A third party app used the deprecated CheckForFinishedDownload command, it should be updated RefreshMonitoredDownloads instead");
            Refresh();
        }

        public void Handle(BookGrabbedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(TrackImportedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(ProviderUpdatedEvent<IDownloadClient> message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(ProviderDeletedEvent<IDownloadClient> message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(DownloadsProcessedEvent message)
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads().Where(DownloadIsTrackable).ToList();

            _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));
        }

        public void Handle(TrackedDownloadsRemovedEvent message)
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads().Where(DownloadIsTrackable).ToList();

            _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));
        }
    }
}
