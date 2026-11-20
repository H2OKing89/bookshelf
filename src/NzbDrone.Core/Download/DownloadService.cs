using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IDownloadService
    {
        Task DownloadReport(RemoteBook remoteBook, int? downloadClientId);
    }

    public class DownloadService : IDownloadService
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IRateLimitService _rateLimitService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ISeedConfigProvider _seedConfigProvider;
        private readonly Logger _logger;
        private readonly IDownloadSubmissionGuard _submissionGuard;

        public DownloadService(IProvideDownloadClient downloadClientProvider,
                               IDownloadClientStatusService downloadClientStatusService,
                               IIndexerFactory indexerFactory,
                               IIndexerStatusService indexerStatusService,
                               IRateLimitService rateLimitService,
                               IEventAggregator eventAggregator,
                               ISeedConfigProvider seedConfigProvider,
                               IDownloadSubmissionGuard submissionGuard,
                               Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _downloadClientStatusService = downloadClientStatusService;
            _indexerFactory = indexerFactory;
            _indexerStatusService = indexerStatusService;
            _rateLimitService = rateLimitService;
            _eventAggregator = eventAggregator;
            _seedConfigProvider = seedConfigProvider;
            _logger = logger;
            _submissionGuard = submissionGuard;
        }

        public async Task DownloadReport(RemoteBook remoteBook, int? downloadClientId)
        {
            Ensure.That(remoteBook.Author, () => remoteBook.Author).IsNotNull();
            Ensure.That(remoteBook.Books, () => remoteBook.Books).HasItems();
            _submissionGuard.EnsureCanSubmit(remoteBook);

            var filterBlockedClients = remoteBook.Release.PendingReleaseReason == PendingReleaseReason.DownloadClientUnavailable;
            var tags = remoteBook.Author.Tags ?? new HashSet<int>();
            var clients = downloadClientId.HasValue
                ? GetExplicitClient(remoteBook, downloadClientId.Value, tags)
                : _downloadClientProvider.GetDownloadClients(remoteBook.Release.DownloadProtocol, remoteBook.Release.IndexerId, filterBlockedClients, tags).ToList();

            foreach (var client in clients.DistinctBy(candidate => candidate.Definition.Id))
            {
                try
                {
                    // This read-only stage is the only failover boundary. Once
                    // Download is entered, even an UnavailableException can mean
                    // the add succeeded and a later category/priority call failed.
                    client.GetStatus();
                }
                catch (Exception ex) when (IsPreflightUnavailable(ex))
                {
                    _downloadClientStatusService.RecordFailure(client.Definition.Id);
                    _logger.Debug(ex, "Client {0} unavailable before submission; no add was attempted", client.Definition.Name);
                    continue;
                }

                await DownloadReport(remoteBook, client);
                return;
            }

            throw new DownloadClientUnavailableException($"No {remoteBook.Release.DownloadProtocol} download client is available before submission. No add was attempted.");
        }

        private List<IDownloadClient> GetExplicitClient(RemoteBook remoteBook, int downloadClientId, HashSet<int> tags)
        {
            // Explicit routing remains a singleton, and must not bypass author
            // tag isolation. Availability filtering must not redirect it.
            var eligible = _downloadClientProvider.GetDownloadClients()
                .Where(client => client.Protocol == remoteBook.Release.DownloadProtocol).ToList();
            var matching = eligible.Where(client => client.Definition.Tags.Intersect(tags).Any()).ToList();
            eligible = matching.Any() ? matching : eligible.Where(client => client.Definition.Tags.Empty()).ToList();
            var selected = eligible.SingleOrDefault(client => client.Definition.Id == downloadClientId);
            if (selected == null)
            {
                throw new DownloadClientUnavailableException("The selected download client is not eligible for this author and protocol. Please check its tags and settings.");
            }

            return new List<IDownloadClient> { selected };
        }

        private static bool IsPreflightUnavailable(Exception exception)
        {
            return exception is DownloadClientException or HttpException or WebException or HttpRequestException or
                SocketException or IOException or TimeoutException or OperationCanceledException;
        }

        private async Task DownloadReport(RemoteBook remoteBook, IDownloadClient downloadClient)
        {
            Ensure.That(remoteBook.Author, () => remoteBook.Author).IsNotNull();
            Ensure.That(remoteBook.Books, () => remoteBook.Books).HasItems();

            var downloadTitle = remoteBook.Release.Title;

            if (downloadClient == null)
            {
                throw new DownloadClientUnavailableException($"{remoteBook.Release.DownloadProtocol} Download client isn't configured yet");
            }

            // Get the seed configuration for this release.
            remoteBook.SeedConfiguration = _seedConfigProvider.GetSeedConfiguration(remoteBook);

            // Limit grabs to 2 per second.
            if (remoteBook.Release.DownloadUrl.IsNotNullOrWhiteSpace() && !remoteBook.Release.DownloadUrl.StartsWith("magnet:"))
            {
                var url = new HttpUri(remoteBook.Release.DownloadUrl);
                await _rateLimitService.WaitAndPulseAsync(url.Host, TimeSpan.FromSeconds(2));
            }

            IIndexer indexer = null;

            if (remoteBook.Release.IndexerId > 0)
            {
                indexer = _indexerFactory.GetInstance(_indexerFactory.Get(remoteBook.Release.IndexerId));
            }

            var attemptId = _submissionGuard.Begin(remoteBook, downloadClient);
            string downloadClientId;
            try
            {
                downloadClientId = await downloadClient.Download(remoteBook, indexer);
            }
            catch (ReleaseUnavailableException)
            {
                _submissionGuard.Complete(attemptId);
                _logger.Trace("Release {0} no longer available on indexer.", remoteBook);
                throw;
            }
            catch (ReleaseBlockedException)
            {
                _submissionGuard.Complete(attemptId);
                _logger.Trace("Release {0} previously added to blocklist, not sending to download client again.", remoteBook);
                throw;
            }
            catch (DownloadClientRejectedReleaseException)
            {
                _submissionGuard.Complete(attemptId);
                _logger.Trace("Release {0} rejected by download client, possible duplicate.", remoteBook);
                throw;
            }
            catch (ReleaseDownloadException ex)
            {
                _submissionGuard.Complete(attemptId);
                if (ex.InnerException is TooManyRequestsException http429)
                {
                    _indexerStatusService.RecordFailure(remoteBook.Release.IndexerId, http429.RetryAfter);
                }
                else
                {
                    _indexerStatusService.RecordFailure(remoteBook.Release.IndexerId);
                }

                throw;
            }
            catch (Exception ex)
            {
                var expectedId = (ex as DownloadClientSubmissionUncertainException)?.ExpectedDownloadId;
                downloadClientId = ReconcileSubmission(downloadClient, expectedId);
                if (downloadClientId == null)
                {
                    _submissionGuard.MarkUncertain(attemptId, expectedId);
                    _downloadClientStatusService.RecordFailure(downloadClient.Definition.Id);
                    throw new DownloadClientSubmissionUncertainException(
                        $"Submission outcome for {downloadClient.Definition.Name} is unknown. " +
                        $"No fallback add was attempted. Review submission attempt {attemptId} on that client before retrying.",
                        ex,
                        expectedId);
                }

                _logger.Debug("Confirmed accepted download {0} on client {1} after the add response failed", downloadClientId, downloadClient.Definition.Name);
            }

            try
            {
                _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);
                _indexerStatusService.RecordSuccess(remoteBook.Release.IndexerId);
                _downloadClientProvider.ReportSuccessfulDownloadClient(remoteBook.Release.DownloadProtocol, downloadClient.Definition.Id);

                var bookGrabbedEvent = new BookGrabbedEvent(remoteBook);
                bookGrabbedEvent.DownloadClient = downloadClient.Name;
                bookGrabbedEvent.DownloadClientId = downloadClient.Definition.Id;
                bookGrabbedEvent.DownloadClientName = downloadClient.Definition.Name;

                if (downloadClientId.IsNotNullOrWhiteSpace())
                {
                    bookGrabbedEvent.DownloadId = downloadClientId;
                }

                _logger.ProgressInfo("Report sent to {0} from indexer {1}. {2}", downloadClient.Definition.Name, remoteBook.Release.Indexer, downloadTitle);
                _eventAggregator.PublishEvent(bookGrabbedEvent);
            }
            catch (Exception ex)
            {
                _submissionGuard.MarkUncertain(attemptId, downloadClientId);
                throw new DownloadClientSubmissionUncertainException(
                    $"Client {downloadClient.Definition.Name} accepted this download, but recording its result failed. " +
                    $"Review submission attempt {attemptId} before retrying.",
                    ex,
                    downloadClientId);
            }

            _submissionGuard.Complete(attemptId);
        }

        private string ReconcileSubmission(IDownloadClient client, string expectedId)
        {
            // An indexer title, URL, NZB name or guessed response text cannot
            // establish identity. TorrentClientBase captures the actual hash at
            // the add boundary. NZB client IDs are generally unknown on timeout.
            if (client.Protocol != DownloadProtocol.Torrent || expectedId?.Length != 40 || !expectedId.All(Uri.IsHexDigit))
            {
                return null;
            }

            try
            {
                var matches = client.GetItems().Where(item => item != null &&
                    (item.DownloadClientInfo == null || item.DownloadClientInfo.Id == client.Definition.Id) &&
                    string.Equals(item.DownloadId, expectedId, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                return matches.Count == 1 ? matches[0].DownloadId : null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to reconcile submission against client {0}; another add is blocked", client.Definition.Name);
                return null;
            }
        }
    }
}
