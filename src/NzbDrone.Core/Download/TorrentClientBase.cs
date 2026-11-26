using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
            TorrentIdentity identity;
            try
            {
                identity = _torrentFileInfoReader.GetTorrentIdentity(torrentFile);
            }
            catch (Exception ex)
            {
                throw new ReleaseDownloadException(remoteBook.Release, "Unable to read a valid torrent identity", ex);
            }

            ValidateTorrentIdentity(remoteBook, identity);
            PrepareIdentityAndCheckBlocklist(remoteBook, indexer, identity);
            return SubmitTorrent(() => AddFromTorrentFile(remoteBook, identity, filename, torrentFile), identity);
        }

        private string DownloadFromMagnetUrl(RemoteBook remoteBook, IIndexer indexer, string magnetUrl)
        {
            TorrentIdentity identity;
            try
            {
                identity = TorrentIdentity.ReadMagnet(magnetUrl);
            }
            catch (FormatException ex)
            {
                _logger.Error(ex, "Failed to parse magnetlink for release '{0}'", remoteBook.Release.Title);
                throw new ReleaseDownloadException(remoteBook.Release, "Release contains an invalid magnet download URL", ex);
            }

            ValidateTorrentIdentity(remoteBook, identity);
            PrepareIdentityAndCheckBlocklist(remoteBook, indexer, identity);
            return SubmitTorrent(() => AddFromMagnetLink(remoteBook, identity, magnetUrl), identity);
        }

        // Existing download clients use v1 hashes. Each client that supports
        // v2 must opt in with a verified identity/receipt implementation.
        protected virtual void ValidateTorrentIdentity(RemoteBook remoteBook, TorrentIdentity identity)
        {
            if (identity.IsV2Only)
            {
                throw new DownloadClientRejectedReleaseException(remoteBook.Release, "This download client does not have verified BitTorrent v2-only tracking support.");
            }
        }

        protected virtual bool RequiresFullIdentityProof(TorrentIdentity identity) => false;

        protected virtual string AddFromMagnetLink(RemoteBook remoteBook, TorrentIdentity identity, string magnetLink) => AddFromMagnetLink(remoteBook, identity.V1, magnetLink);
        protected virtual string AddFromTorrentFile(RemoteBook remoteBook, TorrentIdentity identity, string filename, byte[] fileContent) => AddFromTorrentFile(remoteBook, identity.V1, filename, fileContent);

        private string SubmitTorrent(Func<string> submit, TorrentIdentity identity)
        {
            var fullProof = RequiresFullIdentityProof(identity);
            try
            {
                var actual = submit();
                if (!fullProof && actual.IsNotNullOrWhiteSpace() && !TorrentIdentity.EqualsHash(identity.ContentHash, actual))
                {
                    _logger.Debug("{0} returned a download ID distinct from the content hash.", Definition.Implementation);
                }

                return actual;
            }
            catch (ReleaseDownloadException)
            {
                // Explicit pre-submission refusals must not be interpreted as
                // an accepted or ambiguous add.
                throw;
            }
            catch (DownloadClientMagnetNotSupportedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new DownloadClientSubmissionUncertainException(
                    "The torrent add did not finish normally; its submission outcome must be checked on this client.",
                    ex,
                    identity.ContentHash,
                    fullProof ? identity.V2 : null);
            }
        }

        private void PrepareIdentityAndCheckBlocklist(RemoteBook remoteBook, IIndexer indexer, TorrentIdentity identity)
        {
            var indexerSettings = indexer?.Definition?.Settings as ITorrentIndexerSettings;
            if (remoteBook.Release is not TorrentInfo torrentInfo)
            {
                return;
            }

            var advertised = TorrentIdentity.Normalize(torrentInfo.InfoHash, 40) ?? TorrentIdentity.Normalize(torrentInfo.InfoHash, 64);
            if (advertised != null && !identity.IsNativeHandle(advertised))
            {
                throw new ReleaseDownloadException(remoteBook.Release, "The downloaded torrent does not match the advertised hash.");
            }

            // Even if an indexer provided a truncated client handle, the
            // parsed full content hash is authoritative for history/blocklists.
            torrentInfo.InfoHash = identity.ContentHash;
            torrentInfo.InfoHashV1 = identity.V1;
            torrentInfo.InfoHashV2 = identity.V2;
            if (remoteBook.ReleaseSource != ReleaseSourceType.InteractiveSearch &&
                indexerSettings?.RejectBlocklistedTorrentHashesWhileGrabbing == true &&
                identity.Hashes.Any(hash => _blocklistService.BlocklistedTorrentHash(remoteBook.Author.Id, hash)))
            {
                throw new ReleaseBlockedException(remoteBook.Release, "Release previously added to blocklist");
            }
        }
    }
}
