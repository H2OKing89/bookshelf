using System.Net;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Exceptions
{
    public abstract class DownloadPolicyRejectionException : NzbDroneClientException
    {
        protected DownloadPolicyRejectionException(Decision decision)
            : base(HttpStatusCode.Conflict, decision.Reason)
        {
            RejectionCode = decision.Code;
        }

        public string RejectionCode { get; }
    }
}
