using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.HealthCheck
{
    public class HealthCheckRestoredEvent : IEvent
    {
        public HealthCheck PreviousCheck { get; }
        public bool IsInStartupGracePeriod { get; }

        public HealthCheckRestoredEvent(HealthCheck previousCheck, bool isInStartupGracePeriod)
        {
            PreviousCheck = previousCheck;
            IsInStartupGracePeriod = isInStartupGracePeriod;
        }
    }
}
