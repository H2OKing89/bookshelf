using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Http.HappyEyeballs;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    [NonParallelizable]
    public class ManagedHttpDispatcherCancellationFixture : TestBase
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
        private TrackingDispatcher _dispatcher;

        [SetUp]
        public void Setup()
        {
            var agent = new Mock<IUserAgentBuilder>();
            agent.Setup(builder => builder.GetUserAgent(It.IsAny<bool>())).Returns("BookshelfCancellationTest/1");
            _dispatcher = new TrackingDispatcher(agent.Object, TestLogger);
        }

        [TearDown]
        public void Cleanup()
        {
            _dispatcher.Dispose();
        }

        [Test]
        public void already_cancelled_request_should_not_create_a_client()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await _dispatcher.GetResponseAsync(new HttpRequest("http://127.0.0.1:1/"), new CookieContainer(), cancellation.Token));

            error.CancellationToken.Should().Be(cancellation.Token);
            _dispatcher.Clients.Should().BeEmpty();
        }

        [Test]
        public async Task cancellation_while_waiting_for_headers_should_close_the_request()
        {
            var entered = Signal();
            var closed = Signal();
            await using var server = new LoopbackServer(async (stream, path, stop) =>
            {
                entered.TrySetResult(true);
                await ObserveClosure(stream, closed, stop);
            });
            using var cancellation = new CancellationTokenSource();
            var pending = _dispatcher.GetResponseAsync(new HttpRequest(server.Url), new CookieContainer(), cancellation.Token);
            await entered.Task.WaitAsync(Deadline);

            cancellation.Cancel();

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            error.CancellationToken.Should().Be(cancellation.Token);
            await closed.Task.WaitAsync(Deadline);
        }

        [Test]
        public async Task cancellation_during_a_pending_connect_callback_should_preserve_the_callers_token()
        {
            var entered = Signal();
            using var handler = new System.Net.Http.SocketsHttpHandler
            {
                UseProxy = false,
                ConnectCallback = async (context, token) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Stream.Null;
                }
            };
            using var client = new System.Net.Http.HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var dispatcher = WithClient(client);
            using var cancellation = new CancellationTokenSource();
            var pending = dispatcher.GetResponseAsync(new HttpRequest("http://127.0.0.1:1/"), new CookieContainer(), cancellation.Token);
            await entered.Task.WaitAsync(Deadline);

            cancellation.Cancel();

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            error.CancellationToken.Should().Be(cancellation.Token);
        }

        [Test]
        public async Task caller_cancellation_should_take_precedence_when_the_request_timeout_also_fires()
        {
            using var cancellation = new CancellationTokenSource();
            var entered = Signal();
            using var handler = new CallbackHandler(async (message, token) =>
            {
                var completion = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() =>
                {
                    // This callback runs because the request timeout cancels the linked token.
                    // Cancel the caller before completing the send so both causes are observable.
                    cancellation.Cancel();
                    completion.TrySetCanceled(token);
                });
                entered.TrySetResult(true);
                return await completion.Task;
            });
            using var client = new System.Net.Http.HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var dispatcher = WithClient(client);
            var request = new HttpRequest("http://127.0.0.1:1/") { RequestTimeout = TimeSpan.FromMilliseconds(300) };
            var pending = dispatcher.GetResponseAsync(request, new CookieContainer(), cancellation.Token);
            await entered.Task.WaitAsync(Deadline);

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));

            cancellation.IsCancellationRequested.Should().BeTrue();
            error.CancellationToken.Should().Be(cancellation.Token);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task cancellation_during_response_body_should_remain_cancellation(bool streaming)
        {
            var bodyStarted = Signal();
            var closed = Signal();
            await using var server = new LoopbackServer(async (stream, path, stop) =>
            {
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 12\r\nConnection: close\r\n\r\nabc", stop);
                bodyStarted.TrySetResult(true);
                await ObserveClosure(stream, closed, stop);
            });
            using var cancellation = new CancellationTokenSource();
            using var destination = new MemoryStream();
            var request = new HttpRequest(server.Url);
            if (streaming)
            {
                request.ResponseStream = destination;
            }

            var pending = _dispatcher.GetResponseAsync(request, new CookieContainer(), cancellation.Token);
            await bodyStarted.Task.WaitAsync(Deadline);

            cancellation.Cancel();

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            error.CancellationToken.Should().Be(cancellation.Token);
            destination.CanWrite.Should().BeTrue("the caller owns its response stream");
            await closed.Task.WaitAsync(Deadline);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task request_timeout_during_body_should_be_reported_as_timeout(bool streaming)
        {
            var closed = Signal();
            await using var server = new LoopbackServer(async (stream, path, stop) =>
            {
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 12\r\nConnection: close\r\n\r\nabc", stop);
                await ObserveClosure(stream, closed, stop);
            });
            using var destination = new MemoryStream();
            var request = new HttpRequest(server.Url) { RequestTimeout = TimeSpan.FromMilliseconds(300) };
            if (streaming)
            {
                request.ResponseStream = destination;
            }

            var error = Assert.ThrowsAsync<WebException>(async () =>
                await _dispatcher.GetResponseAsync(request, new CookieContainer(), CancellationToken.None).WaitAsync(Deadline));

            error.Status.Should().Be(WebExceptionStatus.Timeout);
            destination.CanWrite.Should().BeTrue();
            await closed.Task.WaitAsync(Deadline);
        }

        [Test]
        public async Task cancelling_one_request_should_not_cancel_a_concurrent_request()
        {
            var firstStarted = Signal();
            var closed = Signal();
            await using var server = new LoopbackServer(async (stream, path, stop) =>
            {
                if (path == "/held")
                {
                    firstStarted.TrySetResult(true);
                    await ObserveClosure(stream, closed, stop);
                }
                else
                {
                    await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok", stop);
                }
            });
            using var cancellation = new CancellationTokenSource();
            var held = _dispatcher.GetResponseAsync(new HttpRequest(server.Url + "held"), new CookieContainer(), cancellation.Token);
            await firstStarted.Task.WaitAsync(Deadline);
            var other = _dispatcher.GetResponseAsync(new HttpRequest(server.Url + "other"), new CookieContainer());

            cancellation.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () => await held.WaitAsync(Deadline));
            (await other.WaitAsync(Deadline)).Content.Should().Be("ok");
            _dispatcher.Clients.Should().ContainSingle("both requests share the cached client");
        }

        [Test]
        public async Task truncated_response_should_still_be_a_receive_failure()
        {
            await using var server = new LoopbackServer((stream, path, stop) =>
                Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 12\r\nConnection: close\r\n\r\nabc", stop));

            var error = Assert.ThrowsAsync<WebException>(async () =>
                await _dispatcher.GetResponseAsync(new HttpRequest(server.Url), new CookieContainer()).WaitAsync(Deadline));

            error.Status.Should().Be(WebExceptionStatus.ReceiveFailure);
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private ManagedHttpDispatcher WithClient(System.Net.Http.HttpClient client)
        {
            var agent = new Mock<IUserAgentBuilder>();
            agent.Setup(builder => builder.GetUserAgent(It.IsAny<bool>())).Returns("BookshelfCancellationTest/1");
            return new SuppliedClientDispatcher(client, agent.Object, TestLogger);
        }

        private static async Task ObserveClosure(NetworkStream stream, TaskCompletionSource<bool> closed, CancellationToken stop)
        {
            try
            {
                var buffer = new byte[1];
                while (await stream.ReadAsync(buffer, stop) != 0)
                {
                }
            }
            catch (IOException)
            {
                // A reset also proves that the cancelled request released its socket.
            }

            closed.TrySetResult(true);
        }

        private static async Task Write(NetworkStream stream, string content, CancellationToken stop)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(content), stop);
            await stream.FlushAsync(stop);
        }

        private sealed class TrackingDispatcher : ManagedHttpDispatcher, IDisposable
        {
            public TrackingDispatcher(IUserAgentBuilder agent, NLog.Logger logger)
                : base(Mock.Of<IHttpProxySettingsProvider>(),
                       Mock.Of<ICreateManagedWebProxy>(),
                       Mock.Of<ICertificateValidationService>(),
                       agent,
                       new CacheManager(),
                       new HttpHappyEyeballs(logger))
            {
            }

            public List<System.Net.Http.HttpClient> Clients { get; } = new List<System.Net.Http.HttpClient>();

            public void Dispose()
            {
                foreach (var client in Clients)
                {
                    client.Dispose();
                }
            }

            protected override System.Net.Http.HttpClient CreateHttpClient(HttpProxySettings settings)
            {
                var client = base.CreateHttpClient(settings);
                Clients.Add(client);
                return client;
            }
        }

        private sealed class SuppliedClientDispatcher : ManagedHttpDispatcher
        {
            private readonly System.Net.Http.HttpClient _client;

            public SuppliedClientDispatcher(System.Net.Http.HttpClient client, IUserAgentBuilder agent, NLog.Logger logger)
                : base(Mock.Of<IHttpProxySettingsProvider>(),
                       Mock.Of<ICreateManagedWebProxy>(),
                       Mock.Of<ICertificateValidationService>(),
                       agent,
                       new CacheManager(),
                       new HttpHappyEyeballs(logger))
            {
                _client = client;
            }

            protected override System.Net.Http.HttpClient GetClient(HttpUri uri)
            {
                return _client;
            }
        }

        private sealed class CallbackHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> _callback;

            public CallbackHandler(Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> callback)
            {
                _callback = callback;
            }

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _callback(request, cancellationToken);
            }
        }

        private sealed class LoopbackServer : IAsyncDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
            private readonly ConcurrentBag<Task> _connections = new ConcurrentBag<Task>();
            private readonly Func<NetworkStream, string, CancellationToken, Task> _handler;
            private readonly Task _accept;

            public LoopbackServer(Func<NetworkStream, string, CancellationToken, Task> handler)
            {
                _handler = handler;
                _listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";
                _accept = Accept();
            }

            public string Url { get; }

            public async ValueTask DisposeAsync()
            {
                await _stopping.CancelAsync();
                _listener.Stop();
                await _accept.WaitAsync(Deadline);
                await Task.WhenAll(_connections).WaitAsync(Deadline);
                _stopping.Dispose();
            }

            private async Task Accept()
            {
                try
                {
                    while (!_stopping.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                        _connections.Add(Handle(client));
                    }
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                }
            }

            private async Task Handle(TcpClient client)
            {
                using (client)
                await using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    try
                    {
                        var first = await reader.ReadLineAsync(_stopping.Token);
                        if (string.IsNullOrEmpty(first))
                        {
                            return;
                        }

                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stopping.Token)))
                        {
                        }

                        await _handler(stream, first.Split(' ')[1], _stopping.Token);
                    }
                    catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                    {
                    }
                }
            }
        }
    }
}
