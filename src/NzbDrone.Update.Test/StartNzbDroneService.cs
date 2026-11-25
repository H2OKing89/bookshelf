using System;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Test.Common;
using NzbDrone.Update.UpdateEngine;
using IServiceProvider = NzbDrone.Common.IServiceProvider;

namespace NzbDrone.Update.Test
{
    [TestFixture]
    public class StartNzbDroneServiceFixture : TestBase<StartNzbDrone>
    {
        [Test]
        public void should_start_service_if_app_type_was_serivce()
        {
            var targetFolder = "c:\\Readarr\\".AsOsAgnostic();

            Subject.Start(AppType.Service, targetFolder);

            Mocker.GetMock<IServiceProvider>().Verify(c => c.Start(ServiceProvider.SERVICE_NAME), Times.Once());
        }

        [Test]
        public void should_start_console_if_app_type_was_service_but_start_failed_because_of_permissions()
        {
            var targetFolder = "c:\\Readarr\\".AsOsAgnostic();
            var targetProcess = "c:\\Readarr\\Readarr.Console".AsOsAgnostic().ProcessNameToExe();

            Mocker.GetMock<IServiceProvider>().Setup(c => c.Start(ServiceProvider.SERVICE_NAME)).Throws(new InvalidOperationException());

            Subject.Start(AppType.Service, targetFolder);

            Mocker.GetMock<IProcessProvider>().Verify(c => c.SpawnNewProcess(targetProcess, "/" + StartupContext.NO_BROWSER, null, false), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_preserve_exact_home_argument_when_handing_back_to_console()
        {
            var appHome = "c:\\owned fixture=edition\\".AsOsAgnostic();
            var installation = "c:\\Readarr\\".AsOsAgnostic();
            var context = new StartupContext("/data=" + appHome);
            Mocker.SetConstant<IStartupContext>(context);

            Subject.Start(AppType.Console, installation);

            var target = Path.Combine(installation, "Readarr.Console".ProcessNameToExe());
            var arguments = StartupContext.QuoteArgument("/data=" + appHome) + " /nobrowser";
            Mocker.GetMock<IProcessProvider>().Verify(process => process.SpawnNewProcess(target, arguments, null, false), Times.Once());
        }
    }
}
