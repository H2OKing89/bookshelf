using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audible
{
    [TestFixture]
    public class AudibleCatalogProxyFixture : CoreTest<AudibleCatalogProxy>
    {
        private readonly List<JObject> _requests = new List<JObject>();

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsUrl).Returns("https://agent.example");
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsTokenFile).Returns("/tokens/audible");
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsAccount).Returns("My account");
            Mocker.GetMock<IDiskProvider>().Setup(x => x.ReadAllText("/tokens/audible")).Returns("test-secret\n");
        }

        private void GivenPages(params string[] envelopes)
        {
            Mocker.GetMock<IHttpClient>().Setup(x => x.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var index = _requests.Count;
                    _requests.Add(body);
                    request.Url.FullUri.Should().Be("https://agent.example/v1/api/request");
                    request.Headers.GetSingleValue("X-Audible-Marketplace").Should().Be("us");
                    request.Headers.GetSingleValue("X-Audible-Account").Should().Be("My account");
                    request.Headers.GetSingleValue("Authorization").Should().Be("Bearer test-secret");
                    request.AllowAutoRedirect.Should().BeFalse();
                    body.Value<string>("method").Should().Be("GET");
                    return new HttpResponse(request, new HttpHeader(), envelopes[Math.Min(index, envelopes.Length - 1)]);
                });
        }

        [Test]
        public void should_start_at_page_zero_and_continue_until_total_results()
        {
            GivenPages(
                "{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\"}]}}",
                "{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0HMVRXW3C\",\"title\":\"Volume 2\"}]}}");
            var books = Subject.GetAuthorBooks("Yu Omiya", "us");
            books.Should().HaveCount(2);
            _requests[0]["query"].Value<int>("page").Should().Be(0);
            _requests[1]["query"].Value<int>("page").Should().Be(1);
            _requests[0]["query"].Value<string>("author").Should().Be("Yu Omiya");
            _requests[0]["query"].Value<string>("response_groups").Should().Contain("product_desc");
        }

        [Test]
        public void should_reject_agent_http_success_with_upstream_failure()
        {
            GivenPages("{\"status\":429,\"body\":{\"message\":\"Throttled\"}}");
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            _requests.Should().ContainSingle();
        }

        [TestCase("{\"status\":200,\"body\":{\"total_results\":1,\"products\":[]}}")]
        [TestCase("{\"status\":200,\"body\":{\"products\":[]}}")]
        [TestCase("{\"status\":200,\"body\":{\"total_results\":1001,\"products\":[]}}")]
        [TestCase("not json")]
        public void should_reject_incomplete_catalogs(string envelope)
        {
            GivenPages(envelope);
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
        }

        [Test]
        public void should_detect_repeated_pages()
        {
            GivenPages("{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\"}]}}");
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_map_product_details_for_known_asins()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\",\"runtime_length_min\":360,\"release_date\":\"2026-07-10\",\"authors\":[{\"asin\":\"B0GVW7FF1Q\",\"name\":\"Yu Omiya\"}],\"narrators\":[{\"name\":\"David Lee Huynh\"}],\"product_images\":{\"500\":\"https://example/500.jpg\",\"1024\":\"https://example/1024.jpg\"}}}}");
            var book = Subject.GetBook("B0H8F9D4GD", "us");
            book.Region.Should().Be("us");
            book.RuntimeLengthMin.Should().Be(360);
            book.Narrators.Should().ContainSingle(n => n.Name == "David Lee Huynh");
            book.Image.Should().Be("https://example/1024.jpg");
            _requests[0].Value<string>("path").Should().Be("/1.0/catalog/products/B0H8F9D4GD");
        }

        [Test]
        public void should_reject_wrong_product_asin()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0HMVRXW3C\"}}}");
            Assert.Throws<BookInfoException>(() => Subject.GetBook("B0H8F9D4GD", "us"));
        }

        [Test]
        public void should_fail_closed_on_http_error()
        {
            Mocker.GetMock<IHttpClient>().Setup(x => x.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), "Denied", HttpStatusCode.Unauthorized));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
        }

        [Test]
        public void should_refuse_credentials_over_remote_plain_http()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsUrl).Returns("http://agent.example");
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            Mocker.GetMock<IHttpClient>().Verify(x => x.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_not_include_token_in_errors()
        {
            Mocker.GetMock<IHttpClient>().Setup(x => x.Post(It.IsAny<HttpRequest>())).Throws(new Exception("test-secret"));
            var exception = Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            exception.ToString().Should().NotContain("test-secret");
        }
    }
}
