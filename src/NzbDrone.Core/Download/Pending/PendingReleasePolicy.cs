using System.Linq;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Pending
{
    public interface IPendingReleasePolicy
    {
        bool IsEligible(RemoteBook book);
    }

    public class PendingReleasePolicy : IPendingReleasePolicy
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IProvideDownloadClient _downloadClients;
        private readonly IDecisionEngineSpecification[] _checks;

        public PendingReleasePolicy(IIndexerFactory indexerFactory,
                                    IProvideDownloadClient downloadClients,
                                    MonitoredBookSpecification monitored,
                                    QualityAllowedByProfileSpecification quality,
                                    CustomFormatAllowedbyProfileSpecification score,
                                    AcceptableSizeSpecification size,
                                    MaximumSizeSpecification globalSize,
                                    ProtocolSpecification protocol,
                                    IndexerTagSpecification tags,
                                    ReleaseRestrictionsSpecification restrictions,
                                    CutoffSpecification cutoff,
                                    UpgradeDiskSpecification diskUpgrade,
                                    UpgradeAllowedSpecification upgradesAllowed,
                                    QueueSpecification queueUpgrade,
                                    HistorySpecification historyUpgrade)
        {
            _indexerFactory = indexerFactory;
            _downloadClients = downloadClients;
            _checks = new IDecisionEngineSpecification[]
            {
                monitored, quality, score, size, globalSize, protocol, tags, restrictions, cutoff, diskUpgrade,
                upgradesAllowed, queueUpgrade, historyUpgrade
            };
        }

        public bool IsEligible(RemoteBook book)
        {
            var release = book.Release;
            if (book.Author.QualityProfile?.Value == null || book.ParsedBookInfo?.Quality?.Quality == null ||
                (release.IndexerId > 0 && _indexerFactory.Find(release.IndexerId)?.Enable != true))
            {
                return false;
            }

            try
            {
                if (!_downloadClients.GetDownloadClients(release.DownloadProtocol, release.IndexerId, tags: book.Author.Tags).Any())
                {
                    return false;
                }
            }
            catch (DownloadClientUnavailableException)
            {
                return false;
            }

            return _checks.All(specification => specification.IsSatisfiedBy(book, null).Accepted);
        }

        internal static bool CanReconsider(string code)
        {
            return code is RejectionCode.MinimumFreeSpace or RejectionCode.GrabFreeSpaceUnverified or
                RejectionCode.QualityNotWanted or RejectionCode.CustomFormatMinimumScore or RejectionCode.CutoffMet or
                RejectionCode.BelowMinimumSize or RejectionCode.AboveMaximumSize or RejectionCode.MaximumSizeExceeded or
                RejectionCode.ProtocolDisabled or RejectionCode.NoMatchingIndexerTag or RejectionCode.RequiredTermMissing or
                RejectionCode.IgnoredTermPresent or RejectionCode.AuthorNotMonitored or RejectionCode.BookNotMonitored or
                RejectionCode.UpgradesNotAllowed or RejectionCode.QueueUpgradesNotAllowed or RejectionCode.QueueCutoffMet or
                RejectionCode.HistoryCutoffMet or RejectionCode.DiskHigherPreference or RejectionCode.QueueHigherPreference or
                RejectionCode.HistoryHigherPreference or RejectionCode.CustomFormatUpgradeBelowMinimum or
                RejectionCode.PropersDisabled or RejectionCode.RepackDisabled;
        }
    }
}
