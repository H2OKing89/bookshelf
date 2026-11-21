using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Books.Events
{
    public class BookMetadataRefreshedEvent : IEvent
    {
        public BookMetadataRefreshedEvent(int authorMetadataId)
        {
            AuthorMetadataId = authorMetadataId;
        }

        public int AuthorMetadataId { get; }
    }
}
