using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Books.Events
{
    public class AuthorsEditedEvent : IEvent
    {
        public AuthorsEditedEvent(IReadOnlyList<int> authorIds)
        {
            AuthorIds = authorIds;
        }

        public IReadOnlyList<int> AuthorIds { get; }
    }
}
