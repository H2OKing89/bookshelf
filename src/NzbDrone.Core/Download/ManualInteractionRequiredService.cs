using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface IManualInteractionRequiredService
    {
        void Required(TrackedDownload trackedDownload);
        void Recovered(TrackedDownload trackedDownload);
    }

    public class ManualInteractionRequiredService : IManualInteractionRequiredService
    {
        public const string ContextKey = "manualInteractionContextV1";
        public const string PhaseKey = "manualInteractionPhase";
        private readonly IDownloadHistoryRepository _history;
        private readonly IEventAggregator _events;
        private readonly object _gate = new object();

        public ManualInteractionRequiredService(IDownloadHistoryRepository history, IEventAggregator events)
        {
            _history = history;
            _events = events;
        }

        public void Required(TrackedDownload trackedDownload)
        {
            if (!IsBound(trackedDownload) || trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            lock (_gate)
            {
                var histories = ScopedHistory(trackedDownload);
                var context = Context(trackedDownload, histories);
                if (LastPhase(histories, context) == "attempted")
                {
                    trackedDownload.HasNotifiedManualInteractionRequired = true;
                    return;
                }

                // This synchronous write is independent of the event bus, which
                // logs and swallows subscriber failures. It records an attempt,
                // not delivery: a crash after this write does not resend alerts.
                Record(trackedDownload, context, "attempted");
                trackedDownload.HasNotifiedManualInteractionRequired = true;
                _events.PublishEvent(new ManualInteractionRequiredEvent(trackedDownload));
            }
        }

        public void Recovered(TrackedDownload trackedDownload)
        {
            if (!IsBound(trackedDownload) || trackedDownload.State != TrackedDownloadState.Imported)
            {
                return;
            }

            lock (_gate)
            {
                var histories = ScopedHistory(trackedDownload);
                var context = Context(trackedDownload, histories);
                if (LastPhase(histories, context) == "attempted")
                {
                    // Only the fully verified, source-retired transition calls
                    // this method. Receipt acknowledgments are not recovery.
                    Record(trackedDownload, context, "recovered");
                }

                trackedDownload.HasNotifiedManualInteractionRequired = false;
            }
        }

        private static bool IsBound(TrackedDownload tracked)
        {
            return tracked?.DownloadItem != null && !string.IsNullOrWhiteSpace(tracked.DownloadItem.DownloadId) &&
                tracked.DownloadClient > 0 &&
                (tracked.DownloadItem.DownloadClientInfo?.Id is not > 0 || tracked.DownloadItem.DownloadClientInfo.Id == tracked.DownloadClient);
        }

        private List<DownloadHistory> ScopedHistory(TrackedDownload tracked)
        {
            return (_history.FindByDownloadId(tracked.DownloadItem.DownloadId) ?? new List<DownloadHistory>())
                .Where(history => history.DownloadClientId == tracked.DownloadClient &&
                    string.Equals(history.DownloadId, tracked.DownloadItem.DownloadId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(history => history.Date).ThenByDescending(history => history.Id).ToList();
        }

        private static string Context(TrackedDownload tracked, List<DownloadHistory> histories)
        {
            var grab = histories.FirstOrDefault(history => history.EventType == DownloadHistoryEventType.DownloadGrabbed);

            // Mapped paths, diagnostics, tags and catalog metadata can change
            // while the same source remains blocked; they do not open episodes.
            var context = STJson.ToJson(new { Source = tracked.DownloadItem.OutputPath.FullPath, GrabId = grab?.Id, GrabDate = grab?.Date });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        }

        private static string LastPhase(List<DownloadHistory> histories, string context)
        {
            var marker = histories.FirstOrDefault(history => history.EventType == DownloadHistoryEventType.ManualInteraction &&
                DataValue(history.Data, ContextKey) == context);
            return DataValue(marker?.Data, PhaseKey);
        }

        private static string DataValue(Dictionary<string, string> data, string key)
        {
            // Dictionary keys retain their case on deserialize. Reject ambiguous
            // aliases rather than treating a damaged attempt as permission to send.
            return data?.Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value).SingleOrDefault();
        }

        private void Record(TrackedDownload tracked, string context, string phase)
        {
            var marker = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.ManualInteraction,
                AuthorId = tracked.RemoteBook?.Author?.Id ?? 0,
                DownloadId = tracked.DownloadItem.DownloadId,
                DownloadClientId = tracked.DownloadClient,
                SourceTitle = tracked.DownloadItem.Title,
                Protocol = tracked.Protocol,
                Date = DateTime.UtcNow,
                Data = new Dictionary<string, string> { { ContextKey, context }, { PhaseKey, phase } }
            };
            if (_history.Insert(marker)?.Id is not > 0)
            {
                throw new InvalidOperationException("The manual-interaction notification attempt was not persisted");
            }
        }
    }
}
