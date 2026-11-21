using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.CustomFormats.Events
{
    public class CustomFormatUpdatedEvent : IEvent
    {
        public CustomFormatUpdatedEvent(int formatId, bool removed = false)
        {
            FormatId = formatId;
            Removed = removed;
        }

        public int FormatId { get; }
        public bool Removed { get; }
    }
}
