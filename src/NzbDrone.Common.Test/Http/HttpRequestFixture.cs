using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HttpRequestFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_only_redact_sensitive_request_response_and_exception_details_when_requested(bool redact)
        {
            const string detail = "owned-private-fixture";
            var request = new HttpRequest("http://127.0.0.1:9000/" + detail) { RedactSensitiveDetails = redact, ContentSummary = detail };
            var response = new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, detail, HttpStatusCode.BadRequest);
            var exception = new HttpException(response);
            foreach (var rendered in new[] { request.ToString(), response.ToString(), exception.ToString() })
            {
                rendered.Contains(detail, StringComparison.Ordinal).Should().Be(!redact);
            }
        }

        [Test]
        public async Task private_requests_should_not_share_cookies_or_log_error_bodies_and_redirect_locations()
        {
            const string detail = "owned-private-fixture";
            var logTarget = new MemoryTarget { Layout = "${message} ${exception:format=tostring}" };
            using var factory = new LogFactory();
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(logTarget);
            factory.Configuration = configuration;

            var dispatcher = new Mock<IHttpDispatcher>();
            var observedCookies = new List<string>();
            dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns((HttpRequest request, CookieContainer cookies) =>
                {
                    observedCookies.Add(cookies.GetCookieHeader((Uri)request.Url));
                    var headers = new HttpHeader { ContentType = "application/json" };
                    headers.Add("Set-Cookie", "owned=value; Path=/");
                    if (request.RedactSensitiveDetails)
                    {
                        headers.Add("Location", "http://127.0.0.1:9000/" + detail);
                        return Task.FromResult(new HttpResponse(request, headers, detail, HttpStatusCode.Found));
                    }

                    return Task.FromResult(new HttpResponse(request, headers, "public"));
                });
            var client = new HttpClient(Array.Empty<IHttpRequestInterceptor>(), new CacheManager(), Mock.Of<IRateLimitService>(), dispatcher.Object, factory.GetLogger("OwnedHttp"));
            await client.GetAsync(new HttpRequest("http://127.0.0.1:9000/public") { StoreResponseCookie = true });
            await client.GetAsync(new HttpRequest("http://127.0.0.1:9000/" + detail)
            {
                RedactSensitiveDetails = true,
                AllowAutoRedirect = false,
                UseCookies = false,
                StoreResponseCookie = true,
                LogResponseContent = true
            });
            await client.GetAsync(new HttpRequest("http://127.0.0.1:9000/again"));

            observedCookies.Should().Equal(string.Empty, string.Empty, "owned=value");
            logTarget.Logs.Should().Contain(log => log.Contains("[private endpoint]", StringComparison.Ordinal));
            logTarget.Logs.Should().NotContain(log => log.Contains(detail, StringComparison.Ordinal));
        }
    }
}
