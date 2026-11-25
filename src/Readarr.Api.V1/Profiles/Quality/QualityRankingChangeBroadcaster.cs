using NzbDrone.Common.TPL;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.SignalR;

namespace Readarr.Api.V1.Profiles.Quality
{
    public class QualityRankingChangeBroadcaster : IHandleAsync<QualityRankingChangedEvent>
    {
        private readonly IBroadcastSignalRMessage _broadcaster;

        public QualityRankingChangeBroadcaster(IBroadcastSignalRMessage broadcaster)
        {
            _broadcaster = broadcaster;
        }

        public void HandleAsync(QualityRankingChangedEvent message)
        {
            if (_broadcaster.IsConnected)
            {
                _broadcaster.BroadcastMessage(new SignalRMessage
                {
                    Name = "qualityrank",
                    Action = ModelAction.Sync,
                    Body = new { action = "sync" }
                }).LogExceptions();
            }
        }
    }
}
