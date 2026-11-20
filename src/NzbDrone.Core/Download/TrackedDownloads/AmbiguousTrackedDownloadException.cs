using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class AmbiguousTrackedDownloadException : NzbDroneException
    {
        public AmbiguousTrackedDownloadException(string downloadId)
            : base("Download ID '{0}' belongs to multiple download clients. Specify downloadClientId.", downloadId)
        {
        }
    }
}
