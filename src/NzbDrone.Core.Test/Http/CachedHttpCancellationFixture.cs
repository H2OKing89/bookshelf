using System;
using System.Net;
using System.Threading;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Http
{
    [TestFixture]
    public class CachedHttpCancellationFixture : CoreTest<CachedHttpResponseService>
    {
        [Test]
        public void cancelled_context_should_not_read_cache_or_start_http()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            using var scope = CommandExecutionContext.Begin(source.Token);

            Assert.Throws<OperationCanceledException>(() => Subject.Get(new HttpRequest("https://synthetic.example"), true, TimeSpan.FromMinutes(1)));

            Mocker.GetMock<ICachedHttpResponseRepository>().Verify(repository => repository.FindByUrl(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void cancellation_after_response_should_not_publish_cached_success()
        {
            using var source = new CancellationTokenSource();
            var request = new HttpRequest("https://synthetic.example");
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(request, source.Token))
                .Returns(() =>
                {
                    source.Cancel();
                    return new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.OK);
                });

            Assert.Throws<OperationCanceledException>(() => Subject.Get(request, true, TimeSpan.FromMinutes(1), source.Token));

            Mocker.GetMock<ICachedHttpResponseRepository>().Verify(repository => repository.Upsert(It.IsAny<CachedHttpResponse>()), Times.Never());
        }

        [Test]
        public void cancellation_during_cache_lookup_should_not_return_a_cache_hit()
        {
            using var source = new CancellationTokenSource();
            var request = new HttpRequest("https://synthetic.example");
            Mocker.GetMock<ICachedHttpResponseRepository>().Setup(repository => repository.FindByUrl(request.Url.ToString()))
                .Returns(() =>
                {
                    source.Cancel();
                    return new CachedHttpResponse { Expiry = DateTime.UtcNow.AddMinutes(1), Value = "{}", StatusCode = 200 };
                });

            Assert.Throws<OperationCanceledException>(() => Subject.Get(request, true, TimeSpan.FromMinutes(1), source.Token));

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void ambient_scope_should_forward_cancellation_to_http()
        {
            using var source = new CancellationTokenSource();
            var request = new HttpRequest("https://synthetic.example");
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(request, source.Token))
                .Returns(new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.OK));
            using var scope = CommandExecutionContext.Begin(source.Token);

            Subject.Get(request, false, TimeSpan.FromMinutes(1));

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(request, source.Token), Times.Once());
        }
    }
}
