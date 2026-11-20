using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Download
{
    public class ManualInteractionRequiredEvent : IEvent
    {
        public TrackedDownload TrackedDownload { get; }

        public ManualInteractionRequiredEvent(TrackedDownload trackedDownload)
        {
            TrackedDownload = trackedDownload;
        }
    }
}
