using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Exceptions
{
    public class DownloadGrabSpaceException : DownloadPolicyRejectionException
    {
        public DownloadGrabSpaceException(Decision decision)
            : base(decision)
        {
        }
    }
}
