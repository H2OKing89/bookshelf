using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Commands;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class BookshelfUpdateIsolationFixture : CoreTest<InstallUpdateService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IBookshelfUpdatePolicy>(new BookshelfUpdatePolicy());
            Mocker.GetMock<ICheckUpdateService>().Setup(service => service.AvailableUpdate()).Returns(new UpdatePackage
            {
                FileName = "Readarr.legacy.tar.gz",
                Url = "https://legacy.invalid/Readarr.legacy.tar.gz"
            });
        }

        [TestCase(UpdateMechanism.BuiltIn)]
        [TestCase(UpdateMechanism.Script)]
        [TestCase(UpdateMechanism.Docker)]
        [TestCase(UpdateMechanism.External)]
        public void manual_install_should_fail_before_checking_downloading_or_running_scripts(UpdateMechanism mechanism)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.UpdateMechanism).Returns(mechanism);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand { Trigger = CommandTrigger.Manual }))
                .Message.Should().Be(BookshelfUpdatePolicy.DisabledReason);

            VerifyNoUpdateWork();
        }

        [Test]
        public void scheduled_check_should_not_enqueue_an_install()
        {
            Subject.Execute(new ApplicationUpdateCheckCommand());

            VerifyNoUpdateWork();
        }

        [Test]
        public void startup_should_not_consume_an_existing_update_required_marker()
        {
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FileExists(It.IsAny<string>())).Returns(true);

            Subject.Handle(new ApplicationStartingEvent());

            VerifyNoUpdateWork();
        }

        private void VerifyNoUpdateWork()
        {
            Mocker.GetMock<ICheckUpdateService>().VerifyNoOtherCalls();
            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IDiskTransferService>().VerifyNoOtherCalls();
            Mocker.GetMock<IArchiveService>().VerifyNoOtherCalls();
            Mocker.GetMock<IProcessProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IBackupService>().VerifyNoOtherCalls();
            Mocker.GetMock<IVerifyUpdates>().VerifyNoOtherCalls();
            Mocker.GetMock<IManageCommandQueue>().VerifyNoOtherCalls();
            Mocker.GetMock<IConfigFileProvider>().VerifyNoOtherCalls();
        }
    }
}
