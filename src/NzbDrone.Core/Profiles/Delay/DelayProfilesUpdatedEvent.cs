using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.Delay
{
    public class DelayProfilesUpdatedEvent : IEvent
    {
        public DelayProfilesUpdatedEvent(IReadOnlySet<int> tags = null)
        {
            Tags = tags;
        }

        public IReadOnlySet<int> Tags { get; }
    }
}
