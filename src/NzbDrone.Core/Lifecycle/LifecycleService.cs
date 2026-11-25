using System.Threading;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using IServiceProvider = NzbDrone.Common.IServiceProvider;

namespace NzbDrone.Core.Lifecycle
{
    public interface ILifecycleService
    {
        void Shutdown();
        void Restart();
        void NotifyHostStopping();
    }

    public class LifecycleService : ILifecycleService, IExecute<ShutdownCommand>, IExecute<RestartCommand>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly IServiceProvider _serviceProvider;
        private readonly Logger _logger;
        private int _shutdownRequested;

        public LifecycleService(IEventAggregator eventAggregator,
                                IRuntimeInfo runtimeInfo,
                                IServiceProvider serviceProvider,
                                Logger logger)
        {
            _eventAggregator = eventAggregator;
            _runtimeInfo = runtimeInfo;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public void Shutdown()
        {
            if (!PublishShutdown(false))
            {
                return;
            }

            if (_runtimeInfo.IsWindowsService)
            {
                _serviceProvider.Stop(ServiceProvider.SERVICE_NAME);
            }
        }

        public void Restart()
        {
            if (!PublishShutdown(true))
            {
                return;
            }

            if (_runtimeInfo.IsWindowsService)
            {
                _serviceProvider.Restart(ServiceProvider.SERVICE_NAME);
            }
        }

        // SIGTERM, console Ctrl+C and SCM stop requests use the same domain
        // shutdown notification without recursively stopping/restarting SCM.
        public void NotifyHostStopping()
        {
            PublishShutdown(false);
        }

        private bool PublishShutdown(bool restarting)
        {
            if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
            {
                return false;
            }

            _runtimeInfo.IsExiting = true;
            _logger.Info(restarting ? "Restart requested." : "Shutdown requested.");
            _eventAggregator.PublishEvent(new ApplicationShutdownRequested(restarting));
            return true;
        }

        public void Execute(ShutdownCommand message)
        {
            Shutdown();
        }

        public void Execute(RestartCommand message)
        {
            Restart();
        }
    }
}
