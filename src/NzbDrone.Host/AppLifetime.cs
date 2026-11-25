using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Host
{
    public class AppLifetime : IHostedService, IHandle<ApplicationShutdownRequested>
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly IStartupContext _startupContext;
        private readonly IBrowserService _browserService;
        private readonly ILifecycleService _lifecycleService;
        private readonly ICommandExecutionShutdown _commandExecution;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public AppLifetime(IHostApplicationLifetime appLifetime,
            IConfigFileProvider configFileProvider,
            IRuntimeInfo runtimeInfo,
            IStartupContext startupContext,
            IBrowserService browserService,
            ILifecycleService lifecycleService,
            ICommandExecutionShutdown commandExecution,
            IEventAggregator eventAggregator,
            Logger logger)
        {
            _appLifetime = appLifetime;
            _configFileProvider = configFileProvider;
            _runtimeInfo = runtimeInfo;
            _startupContext = startupContext;
            _browserService = browserService;
            _lifecycleService = lifecycleService;
            _commandExecution = commandExecution;
            _eventAggregator = eventAggregator;
            _logger = logger;

            appLifetime.ApplicationStarted.Register(OnAppStarted);
            appLifetime.ApplicationStopping.Register(_lifecycleService.NotifyHostStopping);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_commandExecution.WaitForShutdown(TimeSpan.FromSeconds(5), cancellationToken))
            {
                _logger.Warn("Some command workers did not finish within the shutdown grace period.");
            }

            return Task.CompletedTask;
        }

        private void OnAppStarted()
        {
            if (_appLifetime.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }

            _runtimeInfo.IsStarting = false;
            _runtimeInfo.IsExiting = false;

            if (!_startupContext.Flags.Contains(StartupContext.NO_BROWSER)
                && _configFileProvider.LaunchBrowser)
            {
                _browserService.LaunchWebUI();
            }

            _eventAggregator.PublishEvent(new ApplicationStartedEvent());
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(ApplicationShutdownRequested message)
        {
            if (!_runtimeInfo.IsWindowsService)
            {
                if (message.Restarting)
                {
                    _runtimeInfo.RestartPending = true;
                }

                if (!_appLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    _logger.Info("Attempting to stop application.");
                    _appLifetime.StopApplication();
                }
            }
        }
    }
}
