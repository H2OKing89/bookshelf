using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;
using NzbDrone.Test.Common;
using HttpClient = NzbDrone.Common.Http.HttpClient;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HttpClientCancellationFixture : TestBase<HttpClient>
    {
        private Mock<IHttpDispatcher> _dispatcher;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IEnumerable<IHttpRequestInterceptor>>(Array.Empty<IHttpRequestInterceptor>());
            _dispatcher = Mocker.GetMock<IHttpDispatcher>();
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns((HttpRequest request, CookieContainer cookies) => Task.FromResult(Response(request)));
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()))
                .Returns((HttpRequest request, CookieContainer cookies, CancellationToken token) => Task.FromResult(Response(request)));
        }

        [Test]
        public void pre_cancelled_request_should_not_invoke_interceptors_rate_limit_or_dispatcher()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var interceptor = new Mock<IHttpRequestInterceptor>();
            Mocker.SetConstant<IEnumerable<IHttpRequestInterceptor>>(new[] { interceptor.Object });
            var request = Request();
            request.RateLimit = TimeSpan.FromMinutes(1);

            Assert.CatchAsync<OperationCanceledException>(async () => await Subject.ExecuteAsync(request, cancellation.Token));

            interceptor.VerifyNoOtherCalls();
            _dispatcher.VerifyNoOtherCalls();
            Mocker.GetMock<IRateLimitService>().VerifyNoOtherCalls();
        }

        [Test]
        public async Task redirects_should_retain_the_callers_token()
        {
            using var cancellation = new CancellationTokenSource();
            var tokens = new List<CancellationToken>();
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()))
                .Returns((HttpRequest request, CookieContainer cookies, CancellationToken token) =>
                {
                    tokens.Add(token);
                    if (tokens.Count == 1)
                    {
                        return Task.FromResult(new HttpResponse(request, new HttpHeader { { "Location", "/redirected" } }, string.Empty, HttpStatusCode.Found));
                    }

                    return Task.FromResult(Response(request));
                });
            var request = Request();
            request.AllowAutoRedirect = true;

            var response = await Subject.GetAsync(request, cancellation.Token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            request.Url.FullUri.Should().EndWith("/redirected");
            tokens.Should().Equal(cancellation.Token, cancellation.Token);
        }

        [Test]
        public void cancellation_after_redirect_response_should_not_issue_the_next_request()
        {
            using var cancellation = new CancellationTokenSource();
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token))
                .Returns((HttpRequest request, CookieContainer cookies, CancellationToken token) =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(new HttpResponse(request, new HttpHeader { { "Location", "/must-not-run" } }, string.Empty, HttpStatusCode.Found));
                });
            var request = Request();
            request.AllowAutoRedirect = true;

            Assert.CatchAsync<OperationCanceledException>(async () => await Subject.GetAsync(request, cancellation.Token));

            _dispatcher.Verify(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token), Times.Once());
            request.Url.FullUri.Should().NotContain("must-not-run");
        }

        [Test]
        public async Task cancellation_during_rate_limit_wait_should_never_dispatch()
        {
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mocker.GetMock<IRateLimitService>()
                .Setup(service => service.WaitAndPulseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), cancellation.Token))
                .Returns(async (string key, string subKey, TimeSpan interval, CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                });
            var request = Request();
            request.RateLimit = TimeSpan.FromMinutes(1);
            var operation = Subject.GetAsync(request, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            _dispatcher.VerifyNoOtherCalls();
        }

        [Test]
        public async Task cancelling_one_request_should_not_cancel_another_request()
        {
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()))
                .Returns(async (HttpRequest request, CookieContainer cookies, CancellationToken token) =>
                {
                    if (token == first.Token)
                    {
                        entered.TrySetResult(true);
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }

                    return Response(request);
                });
            var cancelled = Subject.GetAsync(Request(), first.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var surviving = Subject.GetAsync(Request(), second.Token);

            first.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await cancelled.WaitAsync(TimeSpan.FromSeconds(5)));
            (await surviving.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode.Should().Be(HttpStatusCode.OK);
            second.IsCancellationRequested.Should().BeFalse();
        }

        [TestCase("GET")]
        [TestCase("POST")]
        public async Task typed_methods_should_forward_the_token_and_parse_the_response(string method)
        {
            using var cancellation = new CancellationTokenSource();
            var request = Request();

            var response = method == "GET"
                ? await Subject.GetAsync<SyntheticResource>(request, cancellation.Token)
                : await Subject.PostAsync<SyntheticResource>(request, cancellation.Token);

            response.Resource.Value.Should().Be("synthetic");
            request.Method.Method.Should().Be(method);
            _dispatcher.Verify(dispatcher => dispatcher.GetResponseAsync(request, It.IsAny<CookieContainer>(), cancellation.Token), Times.Once());
        }

        [Test]
        public void synchronous_typed_method_should_forward_cancellation()
        {
            using var cancellation = new CancellationTokenSource();

            var response = Subject.Get<SyntheticResource>(Request(), cancellation.Token);

            response.Resource.Value.Should().Be("synthetic");
            _dispatcher.Verify(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token), Times.Once());
        }

        [Test]
        public async Task existing_overload_should_still_use_the_existing_dispatcher_contract()
        {
            await Subject.GetAsync(Request());

            _dispatcher.Verify(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());
            _dispatcher.Verify(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void timeout_should_remain_a_timeout_when_the_caller_did_not_cancel()
        {
            using var cancellation = new CancellationTokenSource();
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token))
                .ThrowsAsync(new WebException("Synthetic timeout", WebExceptionStatus.Timeout));

            var error = Assert.ThrowsAsync<WebException>(async () => await Subject.GetAsync(Request(), cancellation.Token));

            error.Status.Should().Be(WebExceptionStatus.Timeout);
            cancellation.IsCancellationRequested.Should().BeFalse();
        }

        [Test]
        public void pre_cancelled_download_should_not_create_a_directory_or_partial_file()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var directory = Path.Combine(TempFolder, "must-not-be-created");

            Assert.CatchAsync<OperationCanceledException>(async () => await Subject.DownloadFileAsync("https://synthetic.invalid/file", Path.Combine(directory, "file.bin"), null, cancellation.Token));

            Directory.Exists(directory).Should().BeFalse();
            _dispatcher.VerifyNoOtherCalls();
        }

        [Test]
        public async Task cancelled_download_should_dispose_and_remove_only_its_owned_partial_file()
        {
            using var cancellation = new CancellationTokenSource();
            var destination = Path.Combine(TempFolder, "existing.bin");
            File.WriteAllText(destination, "complete original");
            File.WriteAllText(destination + ".part", "unrelated partial");
            var entered = new TaskCompletionSource<FileStream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token))
                .Returns(async (HttpRequest request, CookieContainer cookies, CancellationToken token) =>
                {
                    await request.ResponseStream.WriteAsync(new byte[] { 1, 2, 3 }, token);
                    entered.TrySetResult((FileStream)request.ResponseStream);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Response(request);
                });
            var operation = Subject.DownloadFileAsync("https://synthetic.invalid/file", destination, null, cancellation.Token);
            var stream = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            stream.CanWrite.Should().BeFalse();
            File.Exists(stream.Name).Should().BeFalse();
            File.ReadAllText(destination).Should().Be("complete original");
            File.ReadAllText(destination + ".part").Should().Be("unrelated partial");
            Directory.GetFiles(TempFolder, "*.part").Should().Equal(destination + ".part");
        }

        [Test]
        public void failed_download_should_preserve_destination_and_unrelated_partial_file()
        {
            using var cancellation = new CancellationTokenSource();
            var destination = Path.Combine(TempFolder, "existing.bin");
            File.WriteAllText(destination, "complete original");
            File.WriteAllText(destination + ".part", "unrelated partial");
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token))
                .Returns(async (HttpRequest request, CookieContainer cookies, CancellationToken token) =>
                {
                    await request.ResponseStream.WriteAsync(new byte[] { 1 }, token);
                    throw new IOException("Synthetic body failure");
                });

            Assert.ThrowsAsync<IOException>(async () => await Subject.DownloadFileAsync("https://synthetic.invalid/file", destination, null, cancellation.Token));

            File.ReadAllText(destination).Should().Be("complete original");
            File.ReadAllText(destination + ".part").Should().Be("unrelated partial");
            Directory.GetFiles(TempFolder, "*.part").Should().Equal(destination + ".part");
        }

        [Test]
        public async Task successful_download_should_atomically_replace_destination_without_touching_unrelated_partial_file()
        {
            var destination = Path.Combine(TempFolder, "existing.bin");
            File.WriteAllText(destination, "complete original");
            File.WriteAllText(destination + ".part", "unrelated partial");
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns(async (HttpRequest request, CookieContainer cookies) =>
                {
                    await request.ResponseStream.WriteAsync(Encoding.UTF8.GetBytes("complete replacement"));
                    return Response(request);
                });

            await Subject.DownloadFileAsync("https://synthetic.invalid/file", destination);

            File.ReadAllText(destination).Should().Be("complete replacement");
            File.ReadAllText(destination + ".part").Should().Be("unrelated partial");
            Directory.GetFiles(TempFolder, "*.part").Should().Equal(destination + ".part");
        }

        [Test]
        public void publish_failure_should_preserve_destination_contents_and_clean_only_the_owned_partial()
        {
            var destination = Path.Combine(TempFolder, "existing-directory");
            Directory.CreateDirectory(destination);
            var sentinel = Path.Combine(destination, "sentinel.bin");
            File.WriteAllText(sentinel, "preserve existing contents");
            File.WriteAllText(destination + ".part", "unrelated partial");
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns(async (HttpRequest request, CookieContainer cookies) =>
                {
                    await request.ResponseStream.WriteAsync(Encoding.UTF8.GetBytes("complete staged body"));
                    return Response(request);
                });

            Assert.ThrowsAsync<IOException>(async () => await Subject.DownloadFileAsync("https://synthetic.invalid/file", destination));

            Directory.Exists(destination).Should().BeTrue();
            File.ReadAllText(sentinel).Should().Be("preserve existing contents");
            File.ReadAllText(destination + ".part").Should().Be("unrelated partial");
            Directory.GetFiles(TempFolder, "*.part").Should().Equal(destination + ".part");
        }

        [Test]
        public async Task cancelled_download_should_not_remove_a_concurrent_completed_download()
        {
            using var cancellation = new CancellationTokenSource();
            var destination = Path.Combine(TempFolder, "concurrent.bin");
            var entered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), cancellation.Token))
                .Returns(async (HttpRequest request, CookieContainer cookies, CancellationToken token) =>
                {
                    await request.ResponseStream.WriteAsync(new byte[] { 1 }, token);
                    entered.TrySetResult(((FileStream)request.ResponseStream).Name);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Response(request);
                });
            _dispatcher.Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns(async (HttpRequest request, CookieContainer cookies) =>
                {
                    await request.ResponseStream.WriteAsync(Encoding.UTF8.GetBytes("completed sibling"));
                    return Response(request);
                });
            var cancelled = Subject.DownloadFileAsync("https://synthetic.invalid/slow", destination, null, cancellation.Token);
            var firstPart = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Subject.DownloadFileAsync("https://synthetic.invalid/complete", destination);

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await cancelled.WaitAsync(TimeSpan.FromSeconds(5)));
            File.ReadAllText(destination).Should().Be("completed sibling");
            File.Exists(firstPart).Should().BeFalse();
            Directory.GetFiles(TempFolder, "*.part").Should().BeEmpty();
        }

        private static HttpRequest Request()
        {
            return new HttpRequest("https://synthetic.invalid/start");
        }

        private static HttpResponse Response(HttpRequest request)
        {
            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"value\":\"synthetic\"}");
        }

        public class SyntheticResource
        {
            public string Value { get; set; }
        }
    }
}
