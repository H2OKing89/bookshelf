using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadServiceFixture : CoreTest<DownloadService>
    {
        private const string ExpectedHash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951";

        private RemoteBook _parseResult;
        private List<IDownloadClient> _downloadClients;
        [SetUp]
        public void Setup()
        {
            _downloadClients = new List<IDownloadClient>();

            Mocker.GetMock<IDownloadGrabSpaceService>()
                .Setup(service => service.Check(It.IsAny<RemoteBook>(), It.IsAny<DownloadSpaceBudget>(), It.IsAny<DownloadClientInfo>()))
                .Returns(new GrabSpaceAssessment(Decision.Accept()));

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<bool>()))
                .Returns(_downloadClients);

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<DownloadProtocol>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns<DownloadProtocol, int, bool, HashSet<int>>((v, i, f, t) => _downloadClients.Where(d => d.Protocol == v).ToList());

            var episodes = Builder<Book>.CreateListOfSize(2)
                .TheFirst(1).With(s => s.Id = 12)
                .TheNext(1).With(s => s.Id = 99)
                .All().With(s => s.AuthorId = 5)
                .Build().ToList();

            var releaseInfo = Builder<ReleaseInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Usenet)
                .With(v => v.DownloadUrl = "http://test.site/download1.ext")
                .Build();

            _parseResult = Builder<RemoteBook>.CreateNew()
                   .With(c => c.Author = Builder<Author>.CreateNew().Build())
                   .With(c => c.Release = releaseInfo)
                   .With(c => c.Books = episodes)
                   .Build();
        }

        private Mock<IDownloadClient> WithUsenetClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<DownloadClientDefinition>.CreateNew().With(v => v.Id = _downloadClients.Count + 1).With(v => v.Tags = new HashSet<int>()).Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Usenet);

            return mock;
        }

        private Mock<IDownloadClient> WithTorrentClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<DownloadClientDefinition>.CreateNew().With(v => v.Id = _downloadClients.Count + 1).With(v => v.Tags = new HashSet<int>()).Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Torrent);

            return mock;
        }

        [Test]
        public async Task Download_report_should_publish_on_grab_event()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_parseResult, null);

            VerifyEventPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task Download_report_should_grab_using_client()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_parseResult, null);

            mock.Verify(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public void direct_grab_rechecks_space_before_creating_a_submission_marker_or_contacting_add()
        {
            var client = WithUsenetClient();
            Mocker.GetMock<IDownloadGrabSpaceService>()
                .Setup(service => service.Check(_parseResult, null, It.IsAny<DownloadClientInfo>()))
                .Returns(new GrabSpaceAssessment(Decision.RejectCode(RejectionCode.MinimumFreeSpace, "Not enough space")));

            var failure = Assert.ThrowsAsync<DownloadGrabSpaceException>(
                () => Subject.DownloadReport(_parseResult, client.Object.Definition.Id));

            failure.RejectionCode.Should().Be(RejectionCode.MinimumFreeSpace);
            client.Verify(value => value.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Begin(It.IsAny<RemoteBook>(), It.IsAny<IDownloadClient>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void direct_grab_rechecks_current_size_profile_without_mutating_the_cached_release()
        {
            var client = WithUsenetClient();
            _parseResult.Release.Size = 10 * 1024 * 1024;
            _parseResult.ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.EPUB) };
            var cachedProfile = new QualityProfile { Items = new List<QualityProfileQualityItem> { new () { Quality = Quality.EPUB } } };
            _parseResult.Author.QualityProfile = cachedProfile;
            var currentAuthor = new Author
            {
                Id = _parseResult.Author.Id,
                QualityProfile = new QualityProfile
                {
                    Items = new List<QualityProfileQualityItem> { new () { Quality = Quality.EPUB, MaxSize = 8 } }
                }
            };
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthor(_parseResult.Author.Id)).Returns(currentAuthor);

            var failure = Assert.ThrowsAsync<DownloadSizePolicyException>(
                () => Subject.DownloadReport(_parseResult, client.Object.Definition.Id));

            failure.RejectionCode.Should().Be(RejectionCode.AboveMaximumSize);
            _parseResult.Author.QualityProfile.Value.Should().BeSameAs(cachedProfile);
            client.Verify(value => value.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Begin(It.IsAny<RemoteBook>(), It.IsAny<IDownloadClient>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [TestCase("accepted", 17L)]
        [TestCase("uncertain", 17L)]
        [TestCase("not_added", 0L)]
        public async Task batch_budget_should_count_accepted_and_uncertain_adds_but_not_proven_failures(string outcome, long submittedBytes)
        {
            var client = WithUsenetClient();
            var budget = new DownloadSpaceBudget();
            Mocker.GetMock<IDownloadGrabSpaceService>()
                .Setup(service => service.Check(_parseResult, budget, It.IsAny<DownloadClientInfo>()))
                .Returns(new GrabSpaceAssessment(Decision.Accept(), TempFolder, 17));

            if (outcome == "uncertain")
            {
                client.Setup(value => value.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>())).ThrowsAsync(new TimeoutException());
                Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(() => Subject.DownloadReport(_parseResult, null, budget));
            }
            else if (outcome == "not_added")
            {
                client.Setup(value => value.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                    .ThrowsAsync(new ReleaseUnavailableException(_parseResult.Release, "Release has gone"));
                Assert.ThrowsAsync<ReleaseUnavailableException>(() => Subject.DownloadReport(_parseResult, null, budget));
            }
            else
            {
                await Subject.DownloadReport(_parseResult, null, budget);
            }

            budget.GetSubmittedBytes(TempFolder).Should().Be(submittedBytes);
        }

        [Test]
        public void Download_report_should_not_publish_on_failed_grab_event()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Throws(new WebException());

            Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(async () => await Subject.DownloadReport(_parseResult, null));

            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_indexer_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once());
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_http429_with_long_time()
        {
            var request = new HttpRequest("http://my.indexer.com");
            var response = new HttpResponse(request, new HttpHeader(), new byte[0], (HttpStatusCode)429);
            response.Headers["Retry-After"] = "300";

            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new TooManyRequestsException(request, response));
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), TimeSpan.FromMinutes(5.0)), Times.Once());
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_http429_based_on_date()
        {
            var request = new HttpRequest("http://my.indexer.com");
            var response = new HttpResponse(request, new HttpHeader(), new byte[0], (HttpStatusCode)429);
            response.Headers["Retry-After"] = DateTime.UtcNow.AddSeconds(300).ToString("r");

            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new TooManyRequestsException(request, response));
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(),
                    It.IsInRange<TimeSpan>(TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5.1), Moq.Range.Inclusive)), Times.Once());
        }

        [Test]
        public void Download_report_should_not_trigger_indexer_backoff_on_downloadclient_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Throws(new DownloadClientException("Some Error"));

            Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void Download_report_should_not_trigger_indexer_backoff_on_indexer_404_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseUnavailableException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void should_not_attempt_download_if_client_isnt_configured()
        {
            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IDownloadClient>().Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task should_attempt_download_even_if_client_is_disabled()
        {
            var mockUsenet = WithUsenetClient();

            Mocker.GetMock<IDownloadClientStatusService>()
                    .Setup(v => v.GetBlockedProviders())
                    .Returns(new List<DownloadClientStatus>
                    {
                        new DownloadClientStatus
                        {
                            ProviderId = _downloadClients.First().Definition.Id,
                            DisabledTill = DateTime.UtcNow.AddHours(3)
                        }
                    });

            await Subject.DownloadReport(_parseResult, null);

            Mocker.GetMock<IDownloadClientStatusService>().Verify(c => c.GetBlockedProviders(), Times.Never());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
            VerifyEventPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task should_send_download_to_correct_usenet_client()
        {
            var mockTorrent = WithTorrentClient();
            var mockUsenet = WithUsenetClient();

            await Subject.DownloadReport(_parseResult, null);

            mockTorrent.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public async Task should_send_download_to_correct_torrent_client()
        {
            var mockTorrent = WithTorrentClient();
            var mockUsenet = WithUsenetClient();

            _parseResult.Release.DownloadProtocol = DownloadProtocol.Torrent;

            await Subject.DownloadReport(_parseResult, null);

            mockTorrent.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [Test]
        public async Task should_fall_back_only_when_the_read_only_preflight_fails()
        {
            var first = WithUsenetClient();
            var second = WithUsenetClient();
            first.Setup(client => client.GetStatus()).Throws(new DownloadClientUnavailableException("offline"));
            second.Setup(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>())).ReturnsAsync("nzb-2");

            await Subject.DownloadReport(_parseResult, null);

            first.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
            Mocker.GetMock<IDownloadClientStatusService>().Verify(service => service.RecordFailure(first.Object.Definition.Id, It.IsAny<TimeSpan>()), Times.Once());
            Mocker.GetMock<IProvideDownloadClient>().Verify(provider => provider.ReportSuccessfulDownloadClient(DownloadProtocol.Usenet, second.Object.Definition.Id), Times.Once());
            Mocker.GetMock<IProvideDownloadClient>().Verify(provider => provider.ReportSuccessfulDownloadClient(DownloadProtocol.Usenet, first.Object.Definition.Id), Times.Never());
            Mocker.GetMock<IIndexerStatusService>().Verify(service => service.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
            VerifyEventPublished<BookGrabbedEvent>();
        }

        [Test]
        public void unavailable_during_add_should_not_fall_back_or_mark_the_indexer_failed()
        {
            var first = WithUsenetClient();
            var second = WithUsenetClient();
            first.Setup(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Throws(new DownloadClientUnavailableException("Response lost after add"));

            var exception = Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(() => Subject.DownloadReport(_parseResult, null));

            exception.Message.Should().Contain("No fallback add");
            second.Verify(client => client.GetStatus(), Times.Never());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.MarkUncertain(It.IsAny<string>(), null), Times.Once());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Complete(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IProvideDownloadClient>().Verify(provider => provider.ReportSuccessfulDownloadClient(It.IsAny<DownloadProtocol>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IIndexerStatusService>().Verify(service => service.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void selected_client_should_not_fall_back_after_preflight_failure()
        {
            var first = WithUsenetClient();
            var second = WithUsenetClient();
            first.Setup(client => client.GetStatus()).Throws(new DownloadClientUnavailableException("offline"));

            Assert.ThrowsAsync<DownloadClientUnavailableException>(() => Subject.DownloadReport(_parseResult, first.Object.Definition.Id));

            second.Verify(client => client.GetStatus(), Times.Never());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Begin(It.IsAny<RemoteBook>(), It.IsAny<IDownloadClient>()), Times.Never());
        }

        [Test]
        public void selected_client_should_not_bypass_author_tags()
        {
            var first = WithUsenetClient();
            ((DownloadClientDefinition)first.Object.Definition).Tags.Add(9);
            WithUsenetClient();
            _parseResult.Author.Tags = new HashSet<int>();

            Assert.ThrowsAsync<DownloadClientUnavailableException>(() => Subject.DownloadReport(_parseResult, first.Object.Definition.Id));
            first.Verify(client => client.GetStatus(), Times.Never());
        }

        [TestCase("unavailable")]
        [TestCase("blocked")]
        [TestCase("duplicate")]
        [TestCase("fetch")]
        public void release_errors_should_stop_without_trying_another_client(string kind)
        {
            var first = WithUsenetClient();
            var second = WithUsenetClient();
            Exception failure = kind switch
            {
                "unavailable" => new ReleaseUnavailableException(_parseResult.Release, "gone"),
                "blocked" => new ReleaseBlockedException(_parseResult.Release, "blocked"),
                "duplicate" => new DownloadClientRejectedReleaseException(_parseResult.Release, "duplicate"),
                _ => new ReleaseDownloadException(_parseResult.Release, "fetch failed")
            };
            first.Setup(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>())).Throws(failure);

            Assert.ThrowsAsync(failure.GetType(), () => Subject.DownloadReport(_parseResult, null));

            second.Verify(client => client.GetStatus(), Times.Never());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Complete(It.IsAny<string>()), Times.Once());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        private Mock<IDownloadClient> GivenUncertainTorrent()
        {
            var client = WithTorrentClient();
            _parseResult.Release.DownloadProtocol = DownloadProtocol.Torrent;
            client.Setup(value => value.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Throws(new DownloadClientSubmissionUncertainException("lost response", new TimeoutException(), ExpectedHash));
            return client;
        }

        [Test]
        public async Task should_reconcile_exact_hash_on_the_same_client_and_publish_one_grab()
        {
            var first = GivenUncertainTorrent();
            var second = WithTorrentClient();
            first.Setup(client => client.GetItems()).Returns(new[]
            {
                new DownloadClientItem { DownloadId = ExpectedHash.ToLowerInvariant(), Title = "Different title" }
            });

            await Subject.DownloadReport(_parseResult, null);

            first.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
            first.Verify(client => client.GetItems(), Times.Once());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Complete(It.IsAny<string>()), Times.Once());
            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>().Verify(events => events.PublishEvent(
                It.Is<BookGrabbedEvent>(grab => grab.DownloadClientId == first.Object.Definition.Id && grab.DownloadId == ExpectedHash.ToLowerInvariant())), Times.Once());
            VerifyEventPublished<BookGrabbedEvent>();
        }

        [TestCase("empty")]
        [TestCase("duplicate")]
        [TestCase("other-client")]
        [TestCase("title-only")]
        [TestCase("unavailable")]
        public void unproven_reconciliation_should_keep_the_durable_hold_and_never_add_again(string state)
        {
            var first = GivenUncertainTorrent();
            var second = WithTorrentClient();
            var items = state switch
            {
                "duplicate" => new[] { new DownloadClientItem { DownloadId = ExpectedHash }, new DownloadClientItem { DownloadId = ExpectedHash } },
                "other-client" => new[] { new DownloadClientItem { DownloadId = ExpectedHash, DownloadClientInfo = new DownloadClientItemClientInfo { Id = second.Object.Definition.Id } } },
                "title-only" => new[] { new DownloadClientItem { DownloadId = new string('0', 40), Title = _parseResult.Release.Title } },
                _ => Array.Empty<DownloadClientItem>()
            };
            first.Setup(client => client.GetItems()).Returns(items);
            if (state == "unavailable")
            {
                first.Setup(client => client.GetItems()).Throws(new DownloadClientUnavailableException("offline"));
            }

            Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(() => Subject.DownloadReport(_parseResult, null));

            first.Verify(client => client.GetItems(), Times.Once());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.MarkUncertain(It.IsAny<string>(), ExpectedHash), Times.Once());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Complete(It.IsAny<string>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void unknown_add_failure_should_not_trust_indexer_hash_or_title()
        {
            var first = WithTorrentClient();
            _parseResult.Release = new TorrentInfo { DownloadProtocol = DownloadProtocol.Torrent, InfoHash = ExpectedHash, Title = "Same title" };
            first.Setup(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>())).Throws(new TimeoutException());
            first.Setup(client => client.GetItems()).Returns(new[] { new DownloadClientItem { DownloadId = ExpectedHash, Title = "Same title" } });

            Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(() => Subject.DownloadReport(_parseResult, null));

            first.Verify(client => client.GetItems(), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void durable_hold_should_stop_before_any_client_operation()
        {
            var client = WithUsenetClient();
            Mocker.GetMock<IDownloadSubmissionGuard>().Setup(guard => guard.EnsureCanSubmit(_parseResult))
                .Throws(new DownloadClientSubmissionUncertainException("Review previous attempt"));

            Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(() => Subject.DownloadReport(_parseResult, null));

            client.Verify(value => value.GetStatus(), Times.Never());
            client.Verify(value => value.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void post_add_status_or_event_failure_should_release_the_active_hold_for_review(bool eventFailure)
        {
            var first = WithUsenetClient();
            var second = WithUsenetClient();
            first.Setup(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>())).ReturnsAsync("known-nzb-id");
            if (eventFailure)
            {
                Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                    .Setup(events => events.PublishEvent(It.IsAny<BookGrabbedEvent>())).Throws(new InvalidOperationException("Owned event dispatch failure"));
            }
            else
            {
                Mocker.GetMock<IDownloadClientStatusService>()
                    .Setup(status => status.RecordSuccess(first.Object.Definition.Id)).Throws(new InvalidOperationException("Owned status write failure"));
            }

            var exception = Assert.ThrowsAsync<DownloadClientSubmissionUncertainException>(() => Subject.DownloadReport(_parseResult, null));

            exception.Message.Should().Contain("accepted this download");
            exception.ExpectedDownloadId.Should().Be("known-nzb-id");
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.MarkUncertain(It.IsAny<string>(), "known-nzb-id"), Times.Once());
            Mocker.GetMock<IDownloadSubmissionGuard>().Verify(guard => guard.Complete(It.IsAny<string>()), Times.Never());
            second.Verify(client => client.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
        }
    }
}
