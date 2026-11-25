using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Host
{
    public interface IAppHomeLock
    {
        void Acquire();
        void AcquireForStartup();
    }

    public sealed class AppHomeLock : IAppHomeLock, IDisposable
    {
        private readonly IAppFolderInfo _folders;
        private FileStream _lock;

        public AppHomeLock(IAppFolderInfo folders)
        {
            _folders = folders;
        }

        public void Acquire()
        {
            if (_lock != null)
            {
                return;
            }

            try
            {
                _lock = new FileStream(Path.Combine(_folders.AppDataFolder, "readarr.instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new ReadarrStartupException("The application home is in use or cannot be locked. Stop its server before continuing.");
            }
        }

        public void AcquireForStartup()
        {
            // Retain a bounded retry for externally supervised and legacy
            // updater hand-offs; an internal restart now releases this lock first.
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    Acquire();
                    return;
                }
                catch (ReadarrStartupException) when (timer.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Thread.Sleep(100);
                }
            }
        }

        public void Dispose()
        {
            _lock?.Dispose();
            _lock = null;
        }
    }
}
