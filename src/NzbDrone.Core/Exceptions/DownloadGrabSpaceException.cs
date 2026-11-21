using System.Net;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Exceptions
{
    public class DownloadGrabSpaceException : NzbDroneClientException
    {
        public DownloadGrabSpaceException(Decision decision)
            : base(HttpStatusCode.Conflict, decision.Reason)
        {
            RejectionCode = decision.Code;
        }

        public string RejectionCode { get; }
    }
}
