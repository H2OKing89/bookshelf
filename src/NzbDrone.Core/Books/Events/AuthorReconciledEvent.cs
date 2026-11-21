using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Books.Events
{
    public class AuthorReconciledEvent : IEvent
    {
        public int SourceAuthorId { get; }
        public int TargetAuthorId { get; }

        public AuthorReconciledEvent(int sourceAuthorId, int targetAuthorId)
        {
            SourceAuthorId = sourceAuthorId;
            TargetAuthorId = targetAuthorId;
        }
    }
}
