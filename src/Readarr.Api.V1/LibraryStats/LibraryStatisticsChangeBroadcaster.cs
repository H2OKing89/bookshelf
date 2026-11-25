using NzbDrone.Common.TPL;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.LibraryStats;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Readarr.Api.V1.LibraryStats
{
    public class LibraryStatisticsChangeBroadcaster : IHandleAsync<LibraryStatisticsChangedEvent>, IHandleAsync<BookAddedEvent>, IHandleAsync<EditionDeletedEvent>
    {
        private readonly IBroadcastSignalRMessage _broadcaster;

        public LibraryStatisticsChangeBroadcaster(IBroadcastSignalRMessage broadcaster)
        {
            _broadcaster = broadcaster;
        }

        public void HandleAsync(LibraryStatisticsChangedEvent message)
        {
            Broadcast();
        }

        public void HandleAsync(BookAddedEvent message)
        {
            Broadcast();
        }

        public void HandleAsync(EditionDeletedEvent message)
        {
            Broadcast();
        }

        private void Broadcast()
        {
            if (_broadcaster.IsConnected)
            {
                _broadcaster.BroadcastMessage(new SignalRMessage
                {
                    Name = "librarystatistics",
                    Action = ModelAction.Sync,
                    Body = new { action = "sync" }
                }).LogExceptions();
            }
        }
    }
}
