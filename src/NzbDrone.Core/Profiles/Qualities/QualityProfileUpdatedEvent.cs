using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.Qualities
{
    public class QualityProfileUpdatedEvent : IEvent
    {
        public QualityProfileUpdatedEvent(int qualityProfileId)
        {
            QualityProfileId = qualityProfileId;
        }

        public int QualityProfileId { get; }
    }
}
