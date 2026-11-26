using System;

namespace NzbDrone.Core.Download.Clients
{
    // Deliberately not an UnavailableException: pending-release retries could
    // otherwise submit an already accepted download again.
    public class DownloadClientSubmissionUncertainException : DownloadClientException
    {
        public string ExpectedDownloadId { get; }
        public string ExpectedTorrentInfoHash { get; }

        public DownloadClientSubmissionUncertainException(string message, Exception innerException = null, string expectedDownloadId = null, string expectedTorrentInfoHash = null)
            : base(message, innerException)
        {
            ExpectedDownloadId = expectedDownloadId;
            ExpectedTorrentInfoHash = expectedTorrentInfoHash;
        }
    }
}
