using System;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using IServiceProvider = NzbDrone.Common.IServiceProvider;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    public class AppLifetimeFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void host_stop_and_restart_publish_one_shutdown_then_drain_workers(bool restart)
        {
            using var stopping = new CancellationTokenSource();
            var host = new Mock<IHostApplicationLifetime>();
            host.SetupGet(value => value.ApplicationStopping).Returns(stopping.Token);
            host.Setup(value => value.StopApplication()).Callback(stopping.Cancel);
            var runtime = new Mock<IRuntimeInfo>();
            runtime.SetupAllProperties();
            var events = new Mock<IEventAggregator>();
            var windowsServices = new Mock<IServiceProvider>(MockBehavior.Strict);
            var commands = new Mock<ICommandExecutionShutdown>();
            commands.Setup(value => value.WaitForShutdown(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).Returns(true);
            var logger = LogManager.GetCurrentClassLogger();
            var lifecycle = new LifecycleService(events.Object, runtime.Object, windowsServices.Object, logger);
            var app = new AppLifetime(host.Object, Mock.Of<IConfigFileProvider>(), runtime.Object, new StartupContext(), Mock.Of<IBrowserService>(), lifecycle, commands.Object, events.Object, logger);
            events.Setup(value => value.PublishEvent(It.IsAny<ApplicationShutdownRequested>())).Callback<ApplicationShutdownRequested>(app.Handle);

            if (restart)
            {
                lifecycle.Restart();
            }
            else
            {
                stopping.Cancel();
            }

            lifecycle.Shutdown();
            lifecycle.NotifyHostStopping();
            app.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

            runtime.Object.IsExiting.Should().BeTrue();
            runtime.Object.RestartPending.Should().Be(restart);
            events.Verify(value => value.PublishEvent(It.Is<ApplicationShutdownRequested>(message => message.Restarting == restart)), Times.Once());
            events.VerifyNoOtherCalls();
            windowsServices.VerifyNoOtherCalls();
            commands.Verify(value => value.WaitForShutdown(It.Is<TimeSpan>(timeout => timeout > TimeSpan.Zero && timeout <= TimeSpan.FromSeconds(5)), CancellationToken.None), Times.Once());
        }

        [Test]
        public void external_windows_service_stop_does_not_request_another_scm_operation()
        {
            var runtime = new Mock<IRuntimeInfo>();
            runtime.SetupGet(value => value.IsWindowsService).Returns(true);
            var events = new Mock<IEventAggregator>();
            var windowsServices = new Mock<IServiceProvider>(MockBehavior.Strict);
            var lifecycle = new LifecycleService(events.Object, runtime.Object, windowsServices.Object, LogManager.GetCurrentClassLogger());
            lifecycle.NotifyHostStopping();
            lifecycle.Restart();
            events.Verify(value => value.PublishEvent(It.Is<ApplicationShutdownRequested>(message => !message.Restarting)), Times.Once());
            windowsServices.VerifyNoOtherCalls();
        }
    }
}
