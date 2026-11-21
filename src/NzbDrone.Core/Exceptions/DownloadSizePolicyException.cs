using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Exceptions
{
    public class DownloadSizePolicyException : DownloadPolicyRejectionException
    {
        public DownloadSizePolicyException(Decision decision)
            : base(decision)
        {
        }
    }
}
