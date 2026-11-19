using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.History;
using Readarr.Api.V1.Update;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    public class UpdateApiIsolationFixture
    {
        [Test]
        public void disabled_update_api_should_not_mark_a_legacy_resource_installable()
        {
            var recent = new Mock<IRecentUpdateProvider>(MockBehavior.Strict);
            var history = new Mock<IUpdateHistoryService>(MockBehavior.Strict);
            var controller = new UpdateController(recent.Object, history.Object, new BookshelfUpdatePolicy());

            controller.GetRecentUpdates().Should().BeEmpty();

            recent.VerifyNoOtherCalls();
            history.VerifyNoOtherCalls();
        }
    }
}
