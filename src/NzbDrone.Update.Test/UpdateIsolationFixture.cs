using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Processes;
using NzbDrone.Test.Common;
using NzbDrone.Update.UpdateEngine;

namespace NzbDrone.Update.Test
{
    [TestFixture]
    public class UpdateEngineIsolationFixture : TestBase<InstallUpdateService>
    {
        [Test]
        public void engine_should_refuse_before_touching_a_target_or_existing_process()
        {
            Mocker.SetConstant<IBookshelfUpdatePolicy>(new BookshelfUpdatePolicy());

            Assert.Throws<InvalidOperationException>(() => Subject.Start("owned target must remain unchanged", 1));

            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IDiskTransferService>().VerifyNoOtherCalls();
            Mocker.GetMock<IDetectApplicationType>().VerifyNoOtherCalls();
            Mocker.GetMock<IDetectExistingVersion>().VerifyNoOtherCalls();
            Mocker.GetMock<ITerminateNzbDrone>().VerifyNoOtherCalls();
            Mocker.GetMock<IAppFolderInfo>().VerifyNoOtherCalls();
            Mocker.GetMock<IBackupAndRestore>().VerifyNoOtherCalls();
            Mocker.GetMock<IBackupAppData>().VerifyNoOtherCalls();
            Mocker.GetMock<IStartNzbDrone>().VerifyNoOtherCalls();
            Mocker.GetMock<IProcessProvider>().VerifyNoOtherCalls();
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class UpdateAppIsolationFixture : TestBase<UpdateApp>
    {
        [Test]
        public void app_should_refuse_before_parsing_arguments_or_looking_up_a_process()
        {
            Mocker.SetConstant<IBookshelfUpdatePolicy>(new BookshelfUpdatePolicy());

            Assert.Throws<InvalidOperationException>(() => Subject.Start(null));

            Mocker.GetMock<IProcessProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IInstallUpdateService>().VerifyNoOtherCalls();
        }

        [Test]
        public void entrypoint_should_fail_without_rendering_supplied_arguments()
        {
            var previousError = Console.Error;
            var previousExitCode = Environment.ExitCode;
            using var error = new StringWriter();
            try
            {
                Console.SetError(error);

                UpdateApp.Main(new[] { "owned-secret-canary-that-must-not-be-rendered" });

                Environment.ExitCode.Should().Be(1);
                error.ToString().Should().Contain(BookshelfUpdatePolicy.DisabledReason)
                    .And.NotContain("owned-secret-canary");
            }
            finally
            {
                Console.SetError(previousError);
                Environment.ExitCode = previousExitCode;
            }
        }
    }
}
