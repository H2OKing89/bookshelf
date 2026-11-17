using System;
using System.Threading;

namespace NzbDrone.Core.Messaging.Commands
{
    public static class CommandExecutionContext
    {
        private static readonly AsyncLocal<CancellationToken> CurrentToken = new AsyncLocal<CancellationToken>();

        public static CancellationToken CancellationToken => CurrentToken.Value;

        public static IDisposable Begin(CancellationToken cancellationToken)
        {
            var scope = new Scope(CurrentToken.Value);
            CurrentToken.Value = cancellationToken;
            return scope;
        }

        private sealed class Scope : IDisposable
        {
            private readonly CancellationToken _previous;
            private bool _disposed;

            public Scope(CancellationToken previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    CurrentToken.Value = _previous;
                    _disposed = true;
                }
            }
        }
    }
}
