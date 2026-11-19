using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateReadIsolationFixture
    {
        [Test]
        public void available_update_should_not_query_a_provider_or_config()
        {
            var provider = new Mock<IUpdatePackageProvider>(MockBehavior.Strict);
            var config = new Mock<IConfigFileProvider>(MockBehavior.Strict);
            var service = new CheckUpdateService(provider.Object, config.Object, new BookshelfUpdatePolicy());

            service.AvailableUpdate().Should().BeNull();

            provider.VerifyNoOtherCalls();
            config.VerifyNoOtherCalls();
        }

        [Test]
        public void recent_updates_should_not_query_a_provider_history_or_config()
        {
            var provider = new Mock<IUpdatePackageProvider>(MockBehavior.Strict);
            var config = new Mock<IConfigFileProvider>(MockBehavior.Strict);
            var history = new Mock<IUpdateHistoryService>(MockBehavior.Strict);
            var service = new RecentUpdateProvider(config.Object, provider.Object, history.Object, new BookshelfUpdatePolicy());

            service.GetRecentUpdatePackages().Should().BeEmpty();

            provider.VerifyNoOtherCalls();
            history.VerifyNoOtherCalls();
            config.VerifyNoOtherCalls();
        }

        [Test]
        public void startup_should_preserve_saved_branch_and_mechanism_including_external_deployments()
        {
            var deployment = new Mock<IDeploymentInfoProvider>(MockBehavior.Strict);
            var config = new Mock<IConfigFileProvider>(MockBehavior.Strict);
            var service = new UpdaterConfigProvider(deployment.Object, config.Object, NLog.LogManager.GetLogger("Update isolation"), new BookshelfUpdatePolicy());

            service.Handle(new ApplicationStartedEvent());

            deployment.VerifyNoOtherCalls();
            config.VerifyNoOtherCalls();
        }

        [Test]
        public void health_should_not_report_irrelevant_builtin_writability_or_contact_a_provider()
        {
            var disk = new Mock<IDiskProvider>(MockBehavior.Strict);
            var folders = new Mock<IAppFolderInfo>(MockBehavior.Strict);
            var updates = new Mock<ICheckUpdateService>(MockBehavior.Strict);
            var config = new Mock<IConfigFileProvider>(MockBehavior.Strict);
            var os = new Mock<IOsInfo>(MockBehavior.Strict);
            var localization = new Mock<ILocalizationService>(MockBehavior.Strict);
            var check = new UpdateCheck(disk.Object, folders.Object, updates.Object, config.Object, os.Object, localization.Object, new BookshelfUpdatePolicy());

            check.Check().Type.Should().Be(HealthCheckResult.Ok);

            disk.VerifyNoOtherCalls();
            folders.VerifyNoOtherCalls();
            updates.VerifyNoOtherCalls();
            config.VerifyNoOtherCalls();
            os.VerifyNoOtherCalls();
            localization.VerifyNoOtherCalls();
        }
    }
}
