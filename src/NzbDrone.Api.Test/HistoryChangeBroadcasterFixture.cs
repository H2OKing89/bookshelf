using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.History;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Readarr.Api.V1.History;
using Readarr.Http;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class HistoryChangeBroadcasterFixture : TestBase<HistoryChangeBroadcaster>
    {
        [Test]
        public void should_broadcast_sync_without_history_data()
        {
            var broadcaster = Mocker.GetMock<IBroadcastSignalRMessage>();
            broadcaster.SetupGet(mock => mock.IsConnected).Returns(true);
            SignalRMessage message = null;
            broadcaster.Setup(mock => mock.BroadcastMessage(It.IsAny<SignalRMessage>()))
                .Callback<SignalRMessage>(value => message = value)
                .Returns(Task.CompletedTask);

            Subject.HandleAsync(new HistoryChangedEvent());

            broadcaster.Verify(mock => mock.BroadcastMessage(It.IsAny<SignalRMessage>()), Times.Once());
            message.Name.Should().Be("history");
            message.Action.Should().Be(ModelAction.Sync);
            var body = message.Body.Should().BeOfType<ResourceChangeMessage<HistoryResource>>().Subject;
            body.Action.Should().Be(ModelAction.Sync);
            body.Resource.Should().BeNull();
        }

        [Test]
        public void should_skip_broadcast_without_connected_clients()
        {
            var broadcaster = Mocker.GetMock<IBroadcastSignalRMessage>();
            broadcaster.SetupGet(mock => mock.IsConnected).Returns(false);

            Subject.HandleAsync(new HistoryChangedEvent());

            broadcaster.Verify(mock => mock.BroadcastMessage(It.IsAny<SignalRMessage>()), Times.Never());
        }
    }
}
