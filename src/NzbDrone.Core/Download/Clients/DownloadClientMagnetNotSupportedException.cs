using System;

namespace NzbDrone.Core.Download.Clients
{
    // Only a proven capability rejection before an add may switch from a
    // magnet to a torrent file on the same client.
    public class DownloadClientMagnetNotSupportedException : NotSupportedException
    {
        public DownloadClientMagnetNotSupportedException(string message)
            : base(message)
        {
        }
    }
}
