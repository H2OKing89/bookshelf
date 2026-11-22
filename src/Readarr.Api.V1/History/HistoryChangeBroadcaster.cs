using NzbDrone.Common.TPL;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Readarr.Http;

namespace Readarr.Api.V1.History
{
    public class HistoryChangeBroadcaster : IHandleAsync<HistoryChangedEvent>
    {
        private readonly IBroadcastSignalRMessage _broadcaster;

        public HistoryChangeBroadcaster(IBroadcastSignalRMessage broadcaster)
        {
            _broadcaster = broadcaster;
        }

        public void HandleAsync(HistoryChangedEvent message)
        {
            if (_broadcaster.IsConnected)
            {
                _broadcaster.BroadcastMessage(new SignalRMessage
                {
                    Name = "history",
                    Action = ModelAction.Sync,
                    Body = new ResourceChangeMessage<HistoryResource>(ModelAction.Sync)
                }).LogExceptions();
            }
        }
    }
}
