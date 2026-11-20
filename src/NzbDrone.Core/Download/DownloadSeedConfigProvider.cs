using System;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IDownloadSeedConfigProvider
    {
        TorrentSeedConfiguration GetSeedConfiguration(string infoHash);
        TorrentSeedConfiguration GetSeedConfiguration(string infoHash, int downloadClientId);
    }

    public class DownloadSeedConfigProvider : IDownloadSeedConfigProvider, IHandle<BookGrabbedEvent>
    {
        private readonly Logger _logger;
        private readonly ISeedConfigProvider _indexerSeedConfigProvider;
        private readonly IDownloadHistoryService _downloadHistoryService;

        public class CachedSeedConfiguration
        {
            public int IndexerId { get; set; }
            public bool Discography { get; set; }
        }

        private readonly ICached<CachedSeedConfiguration> _cacheDownloads;

        public DownloadSeedConfigProvider(IDownloadHistoryService downloadHistoryService, ISeedConfigProvider indexerSeedConfigProvider, ICacheManager cacheManager, Logger logger)
        {
            _logger = logger;
            _indexerSeedConfigProvider = indexerSeedConfigProvider;
            _downloadHistoryService = downloadHistoryService;

            _cacheDownloads = cacheManager.GetRollingCache<CachedSeedConfiguration>(GetType(), "indexerByHash", TimeSpan.FromHours(1));
        }

        public TorrentSeedConfiguration GetSeedConfiguration(string infoHash)
        {
            // Unknown ownership cannot safely select another client's seed policy.
            return null;
        }

        public TorrentSeedConfiguration GetSeedConfiguration(string infoHash, int downloadClientId)
        {
            if (infoHash.IsNullOrWhiteSpace() || downloadClientId <= 0)
            {
                return null;
            }

            infoHash = infoHash.ToUpperInvariant();

            var cachedConfig = _cacheDownloads.Get(CacheKey(infoHash, downloadClientId), () => FetchIndexer(infoHash, downloadClientId));

            if (cachedConfig == null)
            {
                return null;
            }

            var seedConfig = _indexerSeedConfigProvider.GetSeedConfiguration(cachedConfig.IndexerId, cachedConfig.Discography);

            return seedConfig;
        }

        private static string CacheKey(string infoHash, int downloadClientId)
        {
            return $"{downloadClientId}:{infoHash.ToUpperInvariant()}";
        }

        public void Handle(BookGrabbedEvent message)
        {
            if (message.DownloadClientId > 0 && message.DownloadId.IsNotNullOrWhiteSpace())
            {
                _cacheDownloads.Remove(CacheKey(message.DownloadId, message.DownloadClientId));
            }
        }

        private CachedSeedConfiguration FetchIndexer(string infoHash, int downloadClientId)
        {
            var historyItem = _downloadHistoryService.GetLatestGrab(infoHash, downloadClientId);

            if (historyItem == null || historyItem.DownloadClientId != downloadClientId || !string.Equals(historyItem.DownloadId, infoHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("No download history item for infohash {0}, unable to provide seed configuration", infoHash);
                return null;
            }

            ParsedBookInfo parsedBookInfo = null;
            if (historyItem.Release != null)
            {
                parsedBookInfo = Parser.Parser.ParseBookTitle(historyItem.Release.Title);
            }

            if (parsedBookInfo == null)
            {
                _logger.Debug("No parsed title in download history item for infohash {0}, unable to provide seed configuration", infoHash);
                return null;
            }

            return new CachedSeedConfiguration
            {
                IndexerId = historyItem.IndexerId,
                Discography = parsedBookInfo.Discography
            };
        }
    }
}
