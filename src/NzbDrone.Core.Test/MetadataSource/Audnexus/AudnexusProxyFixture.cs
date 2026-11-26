using System;
using System.Net;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audnexus
{
    [TestFixture]
    public class AudnexusProxyFixture : CoreTest<AudnexusProxy>
    {
        private const string Asin = "B08G9PRS1K";
        private const string BookJson = @"{
            ""asin"": ""B08G9PRS1K"", ""region"": ""us"", ""title"": ""Project Hail Mary"",
            ""description"": ""A lone astronaut"", ""language"": ""english"",
            ""publisherName"": ""Audible Studios"", ""runtimeLengthMin"": 970,
            ""releaseDate"": ""2021-05-04T00:00:00.000Z"",
            ""narrators"": [{""name"": ""Ray Porter""}]
        }";
        private MetadataProviderStatusService _status;

        [SetUp]
        public void Setup()
        {
            _status = new MetadataProviderStatusService();
            Mocker.SetConstant<IMetadataProviderStatusService>(_status);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudnexusUrl).Returns("https://audnexus.example/api/");
            GivenResponse(BookJson);
        }

        private void GivenResponse(string body, HttpStatusCode status = HttpStatusCode.OK, string retryAfter = null, DateTime? cachedAtUtc = null)
        {
            var headers = new HttpHeader();
            if (retryAfter != null)
            {
                headers["Retry-After"] = retryAfter;
            }

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), true, TimeSpan.FromDays(1)))
                .Returns((HttpRequest request, bool cache, TimeSpan ttl) => new HttpResponse(request, headers, body, status) { CachedAtUtc = cachedAtUtc });
        }

        [Test]
        public void expected_cancellation_should_escape_without_disabling_the_provider()
        {
            using var source = new CancellationTokenSource();
            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>(), true, TimeSpan.FromDays(1), source.Token))
                .Returns(() =>
                {
                    source.Cancel();
                    throw new OperationCanceledException(source.Token);
                });

            using (CommandExecutionContext.Begin(source.Token))
            {
                Assert.Throws<OperationCanceledException>(() => Subject.GetBook(Asin, "us"));
            }

            _status.GetStatus().Should().ContainSingle().Which.State.Should().Be(MetadataProviderState.Cancelled);

            Subject.GetBook(Asin, "us").Title.Should().Be("Project Hail Mary");
            _status.GetStatus().Should().ContainSingle().Which.State.Should().Be(MetadataProviderState.Complete);
        }

        [Test]
        public void should_read_recording_metadata_and_use_marketplace_in_cache_key()
        {
            var book = Subject.GetBook(Asin, "us");

            book.Title.Should().Be("Project Hail Mary");
            book.RuntimeLengthMin.Should().Be(970);
            book.Narrators.Should().ContainSingle(x => x.Name == "Ray Porter");
            book.ReleaseDate.Should().Be(new DateTime(2021, 5, 4, 0, 0, 0, DateTimeKind.Utc));
            Mocker.GetMock<ICachedHttpResponseService>().Verify(
                x => x.Get(
                    It.Is<HttpRequest>(r => r.Url.FullUri == "https://audnexus.example/api/books/B08G9PRS1K?region=us" &&
                        r.RequestTimeout == TimeSpan.FromSeconds(15) && !r.AllowAutoRedirect),
                    true,
                    TimeSpan.FromDays(1)),
                Times.Once());
        }

        [Test]
        public void should_read_chapters_using_the_same_provider_marketplace_and_cache_policy()
        {
            GivenResponse(@"{""asin"":""B08G9PRS1K"",""region"":""us"",""runtimeLengthMs"":2000,""chapters"":[{""title"":""Opening Credits"",""startOffsetMs"":0,""lengthMs"":2000}]}");

            var chapters = Subject.GetChapters(Asin, "us");

            chapters.RuntimeLengthMs.Should().Be(2000);
            chapters.Chapters.Should().ContainSingle(chapter => chapter.Title == "Opening Credits");
            Mocker.GetMock<ICachedHttpResponseService>().Verify(
                x => x.Get(
                    It.Is<HttpRequest>(request => request.Url.FullUri == "https://audnexus.example/api/books/B08G9PRS1K/chapters?region=us"),
                    true,
                    TimeSpan.FromDays(1)),
                Times.Once());
        }

        [TestCase("B000000000", "us")]
        [TestCase(Asin, "uk")]
        public void should_reject_chapters_for_a_different_recording_or_marketplace(string asin, string region)
        {
            GivenResponse(@"{""asin"":""B08G9PRS1K"",""region"":""us"",""chapters"":[]}");
            Assert.Throws<BookInfoException>(() => Subject.GetChapters(asin, region));
        }

        [Test]
        public void should_allow_missing_chapters_without_failing_book_metadata()
        {
            GivenResponse("{}", HttpStatusCode.NotFound);
            Subject.GetChapters(Asin, "us").Should().BeNull();
        }

        [TestCase(null, "us")]
        [TestCase("not-an-asin", "us")]
        [TestCase("B08G9PRS1K/other", "us")]
        public void should_not_request_invalid_identifiers(string asin, string region)
        {
            Subject.GetBook(asin, region).Should().BeNull();
            Mocker.GetMock<ICachedHttpResponseService>().Verify(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void should_support_numeric_asins()
        {
            GivenResponse(@"{""asin"":""1978650817"",""region"":""us"",""title"":""Randomize""}");
            Subject.GetBook("1978650817", "us").Title.Should().Be("Randomize");
        }

        [TestCase("B000000000", "us")]
        [TestCase(Asin, "uk")]
        public void should_reject_metadata_for_different_recordings_or_marketplaces(string asin, string region)
        {
            Assert.Throws<BookInfoException>(() => Subject.GetBook(asin, region));
        }

        [Test]
        public void should_treat_missing_recordings_as_optional()
        {
            GivenResponse(@"{""error"":{""code"":""REGION_UNAVAILABLE""}}", HttpStatusCode.NotFound);
            Subject.GetBook(Asin, "us").Should().BeNull();
            var observed = _status.GetStatus().Should().ContainSingle().Which;
            observed.State.Should().Be(MetadataProviderState.Missing);
            observed.Reason.Should().Be(MetadataProviderReason.ConfirmedMissing);
            observed.LastCompleteAtUtc.Should().BeNull();
        }

        [TestCase(HttpStatusCode.TooManyRequests, MetadataProviderState.RateLimited, MetadataProviderReason.TooManyRequests)]
        [TestCase(HttpStatusCode.InternalServerError, MetadataProviderState.Unavailable, MetadataProviderReason.ServerError)]
        [TestCase(HttpStatusCode.ServiceUnavailable, MetadataProviderState.Unavailable, MetadataProviderReason.ServerError)]
        public void should_back_off_after_provider_errors(HttpStatusCode status, MetadataProviderState expected, MetadataProviderReason reason)
        {
            GivenResponse("unavailable", status, "120");

            Assert.Throws<BookInfoException>(() => Subject.GetBook(Asin, "us"));
            Assert.Throws<BookInfoException>(() => Subject.GetBook("1978650817", "us"));

            Mocker.GetMock<ICachedHttpResponseService>().Verify(x => x.Get(It.IsAny<HttpRequest>(), true, TimeSpan.FromDays(1)), Times.Once());
            var observed = _status.GetStatus().Should().ContainSingle().Which;
            observed.State.Should().Be(expected);
            observed.Reason.Should().Be(reason);
            observed.RetryAfterUtc.Should().BeAfter(observed.ObservedAtUtc);
        }

        [Test]
        public void a_cached_response_preserves_when_the_source_was_read()
        {
            var fetched = DateTime.UtcNow.AddMinutes(-10);
            GivenResponse(BookJson, cachedAtUtc: fetched);

            Subject.GetBook(Asin, "us").Should().NotBeNull();
            var observed = _status.GetStatus().Should().ContainSingle().Which;
            observed.State.Should().Be(MetadataProviderState.Cached);
            observed.LastCompleteAtUtc.Should().Be(fetched);
        }

        [Test]
        public void should_report_a_timeout_distinct_from_an_empty_result()
        {
            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), true, TimeSpan.FromDays(1)))
                .Throws(new WebException("Synthetic timeout", WebExceptionStatus.Timeout));

            Assert.Throws<BookInfoException>(() => Subject.GetBook(Asin, "us"));
            var observed = _status.GetStatus().Should().ContainSingle().Which;
            observed.State.Should().Be(MetadataProviderState.Unavailable);
            observed.Reason.Should().Be(MetadataProviderReason.Timeout);
        }

        [TestCase("not json")]
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("{\"asin\":\"B08G9PRS1K\",\"region\":\"us\",\"releaseDate\":\"bad-date\"}")]
        public void should_report_invalid_payloads(string body)
        {
            GivenResponse(body);
            Assert.Throws<BookInfoException>(() => Subject.GetBook(Asin, "us"));
        }

        [Test]
        public void should_report_transport_errors_instead_of_empty_search_results()
        {
            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), true, TimeSpan.FromDays(1)))
                .Throws(new WebException("Connection failed"));

            Assert.Throws<BookInfoException>(() => Subject.GetBook(Asin, "us"));
        }
    }
}
