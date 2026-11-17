using System;
using System.Threading;

namespace NzbDrone.Core.Messaging.Commands
{
    internal sealed class CommandCancellationState
    {
        private readonly object _gate = new object();
        private CancellationTokenSource _source;
        private CancellationTokenRegistration _shutdownRegistration;
        private bool _requested;
        private bool _finished;
        private int _notifications;

        public CancellationToken Start(CancellationToken shutdown)
        {
            CancellationToken token;
            lock (_gate)
            {
                if (_source != null || _finished)
                {
                    throw new InvalidOperationException("A command execution cannot be started twice.");
                }

                _source = new CancellationTokenSource();
                token = _source.Token;
            }

            var registration = shutdown.Register(() =>
            {
                MarkRequested();
                NotifyCancellation();
            });
            bool finished;
            lock (_gate)
            {
                finished = _finished;
                if (!finished)
                {
                    _shutdownRegistration = registration;
                }
            }

            if (finished)
            {
                registration.Unregister();
            }

            NotifyCancellation();
            return token;
        }

        public void MarkRequested()
        {
            lock (_gate)
            {
                _requested = true;
            }
        }

        public void NotifyCancellation()
        {
            CancellationTokenSource source;
            lock (_gate)
            {
                if (!_requested || _source == null || _finished)
                {
                    return;
                }

                source = _source;
                _notifications++;
            }

            try
            {
                // Callbacks must run outside both the state and command-queue locks.
                source.Cancel();
            }
            finally
            {
                CancellationTokenSource dispose = null;
                lock (_gate)
                {
                    _notifications--;
                    if (_finished && _notifications == 0)
                    {
                        dispose = _source;
                        _source = null;
                    }
                }

                dispose?.Dispose();
            }
        }

        public void Finish()
        {
            CancellationTokenSource dispose = null;
            CancellationTokenRegistration registration;
            lock (_gate)
            {
                _finished = true;
                registration = _shutdownRegistration;
                _shutdownRegistration = default;
                if (_notifications == 0)
                {
                    dispose = _source;
                    _source = null;
                }
            }

            // Do not wait for a shutdown callback that may still be notifying
            // this command. The last notifier owns disposal of its source.
            registration.Unregister();
            dispose?.Dispose();
        }
    }
}
