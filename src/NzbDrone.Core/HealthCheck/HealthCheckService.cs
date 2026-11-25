using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Messaging;
using NzbDrone.Common.Reflection;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.HealthCheck
{
    public interface IHealthCheckService
    {
        List<HealthCheck> Results();
    }

    public class HealthCheckService : IHealthCheckService,
                                      IExecute<CheckHealthCommand>,
                                      IHandleAsync<ApplicationStartedEvent>,
                                      IHandleAsync<IEvent>
    {
        private readonly DateTime _startupGracePeriodEndTime;
        private readonly IProvideHealthCheck[] _healthChecks;
        private readonly IProvideHealthCheck[] _startupHealthChecks;
        private readonly IProvideHealthCheck[] _scheduledHealthChecks;
        private readonly Dictionary<Type, IEventDrivenHealthCheck[]> _eventDrivenHealthChecks;
        private readonly IServerSideNotificationService _serverSideNotificationService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICacheManager _cacheManager;
        private readonly Logger _logger;

        private readonly ICached<HealthCheck> _healthCheckResults;

        // Health checks can be requested by a command, startup and several concurrent
        // event callbacks. Share the run state with the process-local results cache.
        private readonly HealthCheckRunState _runState;

        private class HealthCheckRunState
        {
            public bool HasRunAfterGracePeriod { get; set; }
        }

        public HealthCheckService(IEnumerable<IProvideHealthCheck> healthChecks,
                                  IServerSideNotificationService serverSideNotificationService,
                                  IEventAggregator eventAggregator,
                                  ICacheManager cacheManager,
                                  IRuntimeInfo runtimeInfo,
                                  Logger logger)
        {
            _healthChecks = healthChecks.ToArray();
            _serverSideNotificationService = serverSideNotificationService;
            _eventAggregator = eventAggregator;
            _cacheManager = cacheManager;
            _logger = logger;

            _healthCheckResults = _cacheManager.GetCache<HealthCheck>(GetType());
            _runState = _cacheManager.GetCache<HealthCheckRunState>(GetType(), "runState")
                .Get("state", () => new HealthCheckRunState());

            _startupHealthChecks = _healthChecks.Where(v => v.CheckOnStartup).ToArray();
            _scheduledHealthChecks = _healthChecks.Where(v => v.CheckOnSchedule).ToArray();
            _eventDrivenHealthChecks = GetEventDrivenHealthChecks();
            _startupGracePeriodEndTime = runtimeInfo.StartTime + TimeSpan.FromMinutes(15);
        }

        public List<HealthCheck> Results()
        {
            return _healthCheckResults.Values.ToList();
        }

        private Dictionary<Type, IEventDrivenHealthCheck[]> GetEventDrivenHealthChecks()
        {
            return _healthChecks
                .SelectMany(h => h.GetType().GetAttributes<CheckOnAttribute>().Select(a =>
                {
                    var eventDrivenType = typeof(EventDrivenHealthCheck<>).MakeGenericType(a.EventType);
                    var eventDriven = (IEventDrivenHealthCheck)Activator.CreateInstance(eventDrivenType, h, a.Condition);

                    return Tuple.Create(a.EventType, eventDriven);
                }))
                .GroupBy(t => t.Item1, t => t.Item2)
                .ToDictionary(g => g.Key, g => g.ToArray());
        }

        private void PerformHealthCheck(IProvideHealthCheck[] healthChecks, IEvent message = null, bool performServerChecks = false)
        {
            var results = new List<HealthCheck>();

            foreach (var healthCheck in healthChecks)
            {
                if (healthCheck is IProvideHealthCheckWithMessage && message != null)
                {
                    results.Add(((IProvideHealthCheckWithMessage)healthCheck).Check(message));
                }
                else
                {
                    results.Add(healthCheck.Check());
                }
            }

            if (performServerChecks)
            {
                results.AddRange(_serverSideNotificationService.GetServerChecks());
            }

            foreach (var result in results)
            {
                var previous = _healthCheckResults.Find(result.Source.Name);
                if (result.Type == HealthCheckResult.Ok)
                {
                    _healthCheckResults.Remove(result.Source.Name);
                    if (previous != null)
                    {
                        _eventAggregator.PublishEvent(new HealthCheckRestoredEvent(previous, !_runState.HasRunAfterGracePeriod));
                    }
                }
                else
                {
                    _healthCheckResults.Set(result.Source.Name, result);
                    if (previous == null)
                    {
                        _eventAggregator.PublishEvent(new HealthCheckFailedEvent(result, !_runState.HasRunAfterGracePeriod));
                    }
                }
            }

            _eventAggregator.PublishEvent(new HealthCheckCompleteEvent());
        }

        public void Execute(CheckHealthCommand message)
        {
            lock (_runState)
            {
                CheckAfterGracePeriod();
                var healthChecks = message.Trigger == CommandTrigger.Manual ? _healthChecks : _scheduledHealthChecks;
                PerformHealthCheck(healthChecks, null, true);
            }
        }

        public void HandleAsync(ApplicationStartedEvent message)
        {
            lock (_runState)
            {
                PerformHealthCheck(_startupHealthChecks, null, true);
            }
        }

        private void CheckAfterGracePeriod()
        {
            if (_runState.HasRunAfterGracePeriod || DateTime.UtcNow <= _startupGracePeriodEndTime)
            {
                return;
            }

            // Re-test before reporting startup failures. A check that recovered during
            // grace is removed but must not produce an unpaired external notification.
            var previouslyFailed = _healthCheckResults.Values.Select(result => result.Source).ToHashSet();
            PerformHealthCheck(_healthChecks.Where(check => check.CheckOnStartup || previouslyFailed.Contains(check.GetType())).ToArray());
            _runState.HasRunAfterGracePeriod = true;

            foreach (var result in _healthCheckResults.Values.ToList())
            {
                _eventAggregator.PublishEvent(new HealthCheckFailedEvent(result, false));
            }
        }

        public void HandleAsync(IEvent message)
        {
            if (message is HealthCheckCompleteEvent or HealthCheckFailedEvent or HealthCheckRestoredEvent)
            {
                return;
            }

            lock (_runState)
            {
                CheckAfterGracePeriod();

                if (!_eventDrivenHealthChecks.TryGetValue(message.GetType(), out var checks))
                {
                    return;
                }

                var filteredChecks = new List<IProvideHealthCheck>();
                var healthCheckResults = _healthCheckResults.Values.ToList();

                foreach (var eventDrivenHealthCheck in checks)
                {
                    var healthCheckType = eventDrivenHealthCheck.HealthCheck.GetType();
                    var previouslyFailed = healthCheckResults.Any(r => r.Source == healthCheckType);

                    if (eventDrivenHealthCheck.ShouldExecute(message, previouslyFailed))
                    {
                        filteredChecks.Add(eventDrivenHealthCheck.HealthCheck);
                    }
                }

                // TODO: Add debounce
                PerformHealthCheck(filteredChecks.ToArray(), message);
            }
        }
    }
}
