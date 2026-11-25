using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandExecutionShutdown
    {
        bool WaitForShutdown(TimeSpan timeout, CancellationToken cancellationToken);
    }

    public class CommandExecutor : ICommandExecutionShutdown,
                                   IHandle<ApplicationStartedEvent>,
                                   IHandle<ApplicationShutdownRequested>
    {
        private const int THREAD_LIMIT = 3;

        private readonly Logger _logger;
        private readonly IServiceFactory _serviceFactory;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IEventAggregator _eventAggregator;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly List<Thread> _threads = new List<Thread>();
        private int _started;

        public CommandExecutor(IServiceFactory serviceFactory,
                               IManageCommandQueue commandQueueManager,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _logger = logger;
            _serviceFactory = serviceFactory;
            _commandQueueManager = commandQueueManager;
            _eventAggregator = eventAggregator;
        }

        private void ExecuteCommands()
        {
            try
            {
                foreach (var command in _commandQueueManager.Queue(_cancellationTokenSource.Token))
                {
                    try
                    {
                        ExecuteCommand((dynamic)command.Body, command);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error occurred while executing task {0}", command.Name);
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                _logger.Trace("Stopped one command execution pipeline");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unknown error in thread");
            }
        }

        private void ExecuteCommand<TCommand>(TCommand command, CommandModel commandModel)
            where TCommand : Command
        {
            IExecute<TCommand> handler = null;
            var cancellationToken = commandModel.CancellationState.Start(_cancellationTokenSource.Token);
            using var scope = CommandExecutionContext.Begin(cancellationToken);

            try
            {
                handler = (IExecute<TCommand>)_serviceFactory.Build(typeof(IExecute<TCommand>));

                _logger.Trace("{0} -> {1}", command.GetType().Name, handler.GetType().Name);

                _commandQueueManager.Start(commandModel);
                BroadcastCommandUpdate(commandModel);

                if (ProgressMessageContext.CommandModel == null)
                {
                    ProgressMessageContext.CommandModel = commandModel;
                }

                cancellationToken.ThrowIfCancellationRequested();
                handler.Execute(command);

                _commandQueueManager.Complete(commandModel, command.CompletionMessage ?? commandModel.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _commandQueueManager.Cancelled(commandModel);
                _logger.Trace("Cancelled task {0}", command.Name);
            }
            catch (CommandFailedException ex)
            {
                _commandQueueManager.SetMessage(commandModel, "Failed");
                _commandQueueManager.Fail(commandModel, ex.Message, ex);
                throw;
            }
            catch (Exception ex)
            {
                _commandQueueManager.SetMessage(commandModel, "Failed");
                _commandQueueManager.Fail(commandModel, "Failed", ex);
                throw;
            }
            finally
            {
                commandModel.CancellationState.Finish();
                BroadcastCommandUpdate(commandModel);

                _eventAggregator.PublishEvent(new CommandExecutedEvent(commandModel));

                if (ProgressMessageContext.CommandModel == commandModel)
                {
                    ProgressMessageContext.CommandModel = null;
                }

                if (handler != null)
                {
                    _logger.Trace("{0} <- {1} [{2}]", command.GetType().Name, handler.GetType().Name, commandModel.Duration.ToString());
                }
            }
        }

        private void BroadcastCommandUpdate(CommandModel command)
        {
            if (command.Body.SendUpdatesToClient)
            {
                _eventAggregator.PublishEvent(new CommandUpdatedEvent(command));
            }
        }

        public void Handle(ApplicationStartedEvent message)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }

            lock (_threads)
            {
                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                for (var i = 0; i < THREAD_LIMIT; i++)
                {
                    var thread = new Thread(ExecuteCommands);
                    thread.Start();
                    _threads.Add(thread);
                }
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _logger.Info("Shutting down task execution");
            _cancellationTokenSource.Cancel(true);
        }

        public bool WaitForShutdown(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Thread[] threads;
            lock (_threads)
            {
                threads = _threads.ToArray();
            }

            // Do not dispose the command database while cooperative commands
            // are persisting their final cancelled status. Bound the wait for
            // older, non-cooperative handlers, and never wait on ourselves.
            var deadline = Stopwatch.StartNew();
            foreach (var thread in threads)
            {
                if (thread == Thread.CurrentThread)
                {
                    continue;
                }

                while (thread.IsAlive)
                {
                    var remaining = timeout - deadline.Elapsed;
                    if (remaining <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    thread.Join(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100));
                }
            }

            return true;
        }
    }
}
