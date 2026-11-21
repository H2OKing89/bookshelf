using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.Qualities
{
    public class MinimumUpgradeFormatScoreChangedEvent : IEvent
    {
        public MinimumUpgradeFormatScoreChangedEvent(int qualityProfileId)
        {
            QualityProfileId = qualityProfileId;
        }

        public int QualityProfileId { get; }
    }
}
