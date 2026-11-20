using System;
using System.Net;
using System.Threading.Tasks;
using MonoTorrent;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public abstract class TorrentClientBase<TSettings> : DownloadClientBase<TSettings>
        where TSettings : IProviderConfig, new()
    {
        protected readonly IHttpClient _httpClient;
        private readonly IBlocklistService _blocklistService;
        protected readonly ITorrentFileInfoReader _torrentFileInfoReader;

        protected TorrentClientBase(ITorrentFileInfoReader torrentFileInfoReader,
            IHttpClient httpClient,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            IBlocklistService blocklistService,
            Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _httpClient = httpClient;
            _blocklistService = blocklistService;
            _torrentFileInfoReader = torrentFileInfoReader;
        }

        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public virtual bool PreferTorrentFile => false;

        protected abstract string AddFromMagnetLink(RemoteBook remoteBook, string hash, string magnetLink);
        protected abstract string AddFromTorrentFile(RemoteBook remoteBook, string hash, string filename, byte[] fileContent);

        public override async Task<string> Download(RemoteBook remoteBook, IIndexer indexer)
        {
            var torrentInfo = remoteBook.Release as TorrentInfo;

            string magnetUrl = null;
            string torrentUrl = null;

            if (remoteBook.Release.DownloadUrl.IsNotNullOrWhiteSpace() && remoteBook.Release.DownloadUrl.StartsWith("magnet:"))
            {
                magnetUrl = remoteBook.Release.DownloadUrl;
            }
            else
            {
                torrentUrl = remoteBook.Release.DownloadUrl;
            }

            if (torrentInfo != null && !torrentInfo.MagnetUrl.IsNullOrWhiteSpace())
            {
                magnetUrl = torrentInfo.MagnetUrl;
            }

            if (PreferTorrentFile)
            {
                if (torrentUrl.IsNotNullOrWhiteSpace())
                {
                    return await DownloadFromWebUrl(remoteBook, indexer, torrentUrl);
                }

                if (magnetUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return DownloadFromMagnetUrl(remoteBook, indexer, magnetUrl);
                    }
                    catch (DownloadClientMagnetNotSupportedException ex)
                    {
                        throw new ReleaseDownloadException(remoteBook.Release, "Magnet not supported by download client. ({0})", ex.Message);
                    }
                }
            }
            else
            {
                if (magnetUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return DownloadFromMagnetUrl(remoteBook, indexer, magnetUrl);
                    }
                    catch (DownloadClientMagnetNotSupportedException ex)
                    {
                        if (torrentUrl.IsNullOrWhiteSpace())
                        {
                            throw new ReleaseDownloadException(remoteBook.Release, "Magnet not supported by download client. ({0})", ex.Message);
                        }

                        _logger.Debug("Magnet not supported by download client, trying torrent. ({0})", ex.Message);
                    }
                }

                if (torrentUrl.IsNotNullOrWhiteSpace())
                {
                    return await DownloadFromWebUrl(remoteBook, indexer, torrentUrl);
                }
            }

            throw new ReleaseDownloadException(remoteBook.Release, "Release has no usable torrent or magnet download URL");
        }

        private async Task<string> DownloadFromWebUrl(RemoteBook remoteBook, IIndexer indexer, string torrentUrl)
        {
            byte[] torrentFile = null;

            try
            {
                var request = indexer?.GetDownloadRequest(torrentUrl) ?? new HttpRequest(torrentUrl);
                request.RateLimitKey = remoteBook?.Release?.IndexerId.ToString();
                request.Headers.Accept = "application/x-bittorrent";
                request.AllowAutoRedirect = false;

                var response = await RetryStrategy
                    .ExecuteAsync(static async (state, _) => await state._httpClient.GetAsync(state.request), (_httpClient, request))
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther)
                {
                    var locationHeader = response.Headers.GetSingleValue("Location");

                    _logger.Trace("Torrent request is being redirected to: {0}", locationHeader);

                    if (locationHeader != null)
                    {
                        if (locationHeader.StartsWith("magnet:"))
                        {
                            return DownloadFromMagnetUrl(remoteBook, indexer, locationHeader);
                        }

                        request.Url += new HttpUri(locationHeader);

                        return await DownloadFromWebUrl(remoteBook, indexer, request.Url.ToString());
                    }

                    throw new WebException("Remote website tried to redirect without providing a location.");
                }

                torrentFile = response.ResponseData;

                _logger.Debug("Downloading torrent for release '{0}' finished ({1} bytes from {2})", remoteBook.Release.Title, torrentFile.Length, torrentUrl);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.Error(ex, "Downloading torrent file for book '{0}' failed since it no longer exists ({1})", remoteBook.Release.Title, torrentUrl);
                    throw new ReleaseUnavailableException(remoteBook.Release, "Downloading torrent failed", ex);
                }

                if ((int)ex.Response.StatusCode == 429)
                {
                    _logger.Error("API Grab Limit reached for {0}", torrentUrl);
                }
                else
                {
                    _logger.Error(ex, "Downloading torrent file for release '{0}' failed ({1})", remoteBook.Release.Title, torrentUrl);
                }

                throw new ReleaseDownloadException(remoteBook.Release, "Downloading torrent failed", ex);
            }
            catch (WebException ex)
            {
                _logger.Error(ex, "Downloading torrent file for release '{0}' failed ({1})", remoteBook.Release.Title, torrentUrl);

                throw new ReleaseDownloadException(remoteBook.Release, "Downloading torrent failed", ex);
            }

            var filename = string.Format("{0}.torrent", FileNameBuilder.CleanFileName(remoteBook.Release.Title));
            var hash = _torrentFileInfoReader.GetHashFromTorrentFile(torrentFile);

            EnsureReleaseIsNotBlocklisted(remoteBook, indexer, hash);

            var actualHash = SubmitTorrent(() => AddFromTorrentFile(remoteBook, hash, filename, torrentFile), hash);

            if (actualHash.IsNotNullOrWhiteSpace() && hash != actualHash)
            {
                _logger.Debug(
                    "{0} did not return the expected InfoHash for '{1}', Readarr could potentially lose track of the download in progress.",
                    Definition.Implementation,
                    remoteBook.Release.DownloadUrl);
            }

            return actualHash;
        }

        private string DownloadFromMagnetUrl(RemoteBook remoteBook, IIndexer indexer, string magnetUrl)
        {
            string hash = null;
            string actualHash = null;

            try
            {
                hash = MagnetLink.Parse(magnetUrl).InfoHash.ToHex();
            }
            catch (FormatException ex)
            {
                _logger.Error(ex, "Failed to parse magnetlink for release '{0}': '{1}'", remoteBook.Release.Title, magnetUrl);

                throw new ReleaseDownloadException(remoteBook.Release, "Release contains an invalid magnet download URL", ex);
            }

            if (hash != null)
            {
                EnsureReleaseIsNotBlocklisted(remoteBook, indexer, hash);

                actualHash = SubmitTorrent(() => AddFromMagnetLink(remoteBook, hash, magnetUrl), hash);
            }

            if (actualHash.IsNotNullOrWhiteSpace() && hash != actualHash)
            {
                _logger.Debug(
                    "{0} did not return the expected InfoHash for '{1}', Readarr could potentially lose track of the download in progress.",
                    Definition.Implementation,
                    remoteBook.Release.DownloadUrl);
            }

            return actualHash;
        }

        private static string SubmitTorrent(Func<string> submit, string hash)
        {
            try
            {
                return submit();
            }
            catch (ReleaseDownloadException)
            {
                // Explicit release-unavailable/blocked/duplicate responses stop
                // the grab. They must not turn into a fallback candidate.
                throw;
            }
            catch (DownloadClientMagnetNotSupportedException)
            {
                // Existing magnet-to-torrent-file capability fallback occurs
                // before an add on clients that do not support magnets.
                throw;
            }
            catch (Exception ex)
            {
                throw new DownloadClientSubmissionUncertainException(
                    "The torrent add did not finish normally; its submission outcome must be checked on this client.", ex, hash);
            }
        }

        private void EnsureReleaseIsNotBlocklisted(RemoteBook remoteBook, IIndexer indexer, string hash)
        {
            var indexerSettings = indexer?.Definition?.Settings as ITorrentIndexerSettings;
            var torrentInfo = remoteBook.Release as TorrentInfo;
            var torrentInfoHash = torrentInfo?.InfoHash;

            // If the release didn't come from an interactive search,
            // the hash wasn't known during processing and the
            // indexer is configured to reject blocklisted releases
            // during grab check if it's already been blocklisted.
            if (torrentInfo != null && torrentInfoHash.IsNullOrWhiteSpace())
            {
                // If the hash isn't known from parsing we set it here so it can be used for blocklisting.
                torrentInfo.InfoHash = hash;

                if (remoteBook.ReleaseSource != ReleaseSourceType.InteractiveSearch &&
                    indexerSettings?.RejectBlocklistedTorrentHashesWhileGrabbing == true &&
                    _blocklistService.BlocklistedTorrentHash(remoteBook.Author.Id, hash))
                {
                    throw new ReleaseBlockedException(remoteBook.Release, "Release previously added to blocklist");
                }
            }
        }
    }
}
