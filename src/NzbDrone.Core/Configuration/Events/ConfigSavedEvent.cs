using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Configuration.Events
{
    public class ConfigSavedEvent : IEvent
    {
        public ConfigSavedEvent(IReadOnlySet<string> changedKeys = null)
        {
            ChangedKeys = changedKeys;
        }

        public IReadOnlySet<string> ChangedKeys { get; }
    }
}
