using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    public class ManagedHttpDispatcherConnectionFixture : TestBase
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        [TestCase(false)]
        [TestCase(true)]
        public async Task should_connect_to_a_listener_with_only_one_address_family(bool ipv6)
        {
            await using var server = Server(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback,
                (stream, stop) => ServeHttp(stream, stop, request => "native-family"));
            using var dispatcher = Dispatcher();

            var response = await dispatcher.GetResponseAsync(new HttpRequest(server.Url), new CookieContainer()).WaitAsync(Deadline);

            response.Content.Should().Be("native-family");
            server.AcceptedConnections.Should().Be(1);
        }

        [Test]
        public async Task an_ipv4_only_host_should_not_disable_a_subsequent_ipv6_host()
        {
            await using var ipv4 = Server(IPAddress.Loopback,
                (stream, stop) => ServeHttp(stream, stop, request => "ipv4"));
            using var unavailable = RefusingIPv6Port(ipv4.Port);
            await using var ipv6 = Server(IPAddress.IPv6Loopback,
                (stream, stop) => ServeHttp(stream, stop, request => "ipv6"));
            var connector = new OriginResolver(ipv4.Port, TestLogger);
            using var dispatcher = Dispatcher(connector: connector);

            var first = await dispatcher.GetResponseAsync(new HttpRequest("http://first.invalid:" + ipv4.Port + "/"), new CookieContainer()).WaitAsync(Deadline);
            var second = await dispatcher.GetResponseAsync(new HttpRequest("http://second.invalid:" + ipv6.Port + "/"), new CookieContainer()).WaitAsync(Deadline);

            first.Content.Should().Be("ipv4");
            second.Content.Should().Be("ipv6");
            connector.Attempts.Should().Equal(IPAddress.IPv6Loopback, IPAddress.Loopback, IPAddress.IPv6Loopback);
            dispatcher.Clients.Should().ContainSingle("both origins use the same cached client without shared address-family failure state");
        }

        [Test]
        public async Task should_connect_to_the_http_proxy_without_resolving_the_target_locally()
        {
            var observed = Signal<Request>();
            await using var proxy = Server(IPAddress.Loopback, (stream, stop) => ServeHttp(stream, stop, request =>
            {
                observed.TrySetResult(request);
                return "http-proxy";
            }));
            var settings = new HttpProxySettings(ProxyType.Http, "127.0.0.1", proxy.Port, null, false);
            using var dispatcher = Dispatcher(settings);

            var response = await dispatcher.GetResponseAsync(new HttpRequest("http://unresolvable.invalid:3210/through-proxy"), new CookieContainer()).WaitAsync(Deadline);
            var request = await observed.Task.WaitAsync(Deadline);

            response.Content.Should().Be("http-proxy");
            request.Line.Should().Be("GET http://unresolvable.invalid:3210/through-proxy HTTP/1.1");
            request.Headers["Host"].Should().Be("unresolvable.invalid:3210");
            proxy.AcceptedConnections.Should().Be(1);
        }

        [Test]
        public async Task should_preserve_socks5_remote_name_resolution_and_tunnel_http()
        {
            var observed = Signal<string>();
            await using var proxy = Server(IPAddress.Loopback, async (stream, stop) =>
            {
                var greeting = new byte[2];
                await stream.ReadExactlyAsync(greeting, stop);
                greeting[0].Should().Be(5);
                var methods = new byte[greeting[1]];
                await stream.ReadExactlyAsync(methods, stop);
                methods.Should().Contain((byte)0);
                await stream.WriteAsync(new byte[] { 5, 0 }, stop);

                var connect = new byte[5];
                await stream.ReadExactlyAsync(connect, stop);
                connect.Should().StartWith(new byte[] { 5, 1, 0, 3 });
                var destination = new byte[connect[4]];
                await stream.ReadExactlyAsync(destination, stop);
                var port = new byte[2];
                await stream.ReadExactlyAsync(port, stop);
                ((port[0] << 8) | port[1]).Should().Be(3210);
                observed.TrySetResult(Encoding.ASCII.GetString(destination));
                await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 }, stop);
                await ServeHttp(stream, stop, request => "socks-proxy");
            });
            var settings = new HttpProxySettings(ProxyType.Socks5, "127.0.0.1", proxy.Port, null, false);
            using var dispatcher = Dispatcher(settings);

            var response = await dispatcher.GetResponseAsync(new HttpRequest("http://unresolvable.invalid:3210/through-socks"), new CookieContainer()).WaitAsync(Deadline);

            response.Content.Should().Be("socks-proxy");
            (await observed.Task.WaitAsync(Deadline)).Should().Be("unresolvable.invalid");
            proxy.AcceptedConnections.Should().Be(1);
        }

        [Test]
        public async Task https_proxy_tunnel_should_preserve_the_target_host_and_tls_server_name()
        {
            using var certificate = Certificate();
            var tunnelRequest = Signal<Request>();
            var tlsHost = Signal<string>();
            await using var proxy = Server(IPAddress.Loopback, async (stream, stop) =>
            {
                tunnelRequest.TrySetResult(await ReadRequest(stream, stop));
                await Write(stream, "HTTP/1.1 200 Connection Established\r\n\r\n", stop);
                await using var tls = new SslStream(stream, true);
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificateSelectionCallback = (sender, host) =>
                        {
                            tlsHost.TrySetResult(host);
                            return certificate;
                        },
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    },
                    stop);
                await ServeHttp(tls, stop, request => request.Headers["Host"] + " " + request.Line);
            });
            var settings = new HttpProxySettings(ProxyType.Http, "127.0.0.1", proxy.Port, null, false);
            var validator = new Mock<ICertificateValidationService>();
            validator.Setup(service => service.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>())).Returns(true);
            using var dispatcher = Dispatcher(settings, validator.Object);

            var response = await dispatcher.GetResponseAsync(new HttpRequest("https://unresolvable.invalid:3210/secure-proxy"), new CookieContainer()).WaitAsync(Deadline);

            (await tunnelRequest.Task.WaitAsync(Deadline)).Line.Should().Be("CONNECT unresolvable.invalid:3210 HTTP/1.1");
            (await tlsHost.Task.WaitAsync(Deadline)).Should().Be("unresolvable.invalid");
            response.Content.Should().Be("unresolvable.invalid:3210 GET /secure-proxy HTTP/1.1");
            validator.Verify(service => service.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()), Times.AtLeastOnce());
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task tls_should_still_use_the_configured_certificate_validator(bool acceptCertificate)
        {
            using var certificate = Certificate();
            await using var server = Server(IPAddress.Loopback, async (stream, stop) =>
            {
                await using var tls = new SslStream(stream, true);
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    },
                    stop);
                await ServeHttp(tls, stop, request => "tls");
            });
            SslPolicyErrors? observedErrors = null;
            var validator = new Mock<ICertificateValidationService>();
            validator.Setup(service => service.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()))
                .Returns((object sender, X509Certificate peer, X509Chain chain, SslPolicyErrors errors) =>
                {
                    observedErrors = errors;
                    return acceptCertificate;
                });
            using var dispatcher = Dispatcher(validator: validator.Object);
            var request = new HttpRequest("https://127.0.0.1:" + server.Port + "/");

            if (acceptCertificate)
            {
                (await dispatcher.GetResponseAsync(request, new CookieContainer()).WaitAsync(Deadline)).Content.Should().Be("tls");
            }
            else
            {
                await Assert.ThrowsAsync<HttpRequestException>(async () => await dispatcher.GetResponseAsync(request, new CookieContainer()).WaitAsync(Deadline));
            }

            observedErrors.Should().NotBeNull();
            observedErrors.Value.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue("the owned self-signed certificate is not trusted by the host");
            validator.Verify(service => service.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()), Times.AtLeastOnce());
        }

        [Test]
        public async Task cached_clients_should_preserve_explicit_cookie_isolation()
        {
            var requests = new ConcurrentQueue<Request>();
            await using var server = Server(IPAddress.Loopback, (stream, stop) => ServeHttp(
                stream,
                stop,
                request =>
                {
                    requests.Enqueue(request);
                    return "cookies";
                },
                "Set-Cookie: server-cookie=must-not-leak; Path=/\r\n"));
            using var dispatcher = Dispatcher();
            var firstCookies = new CookieContainer();
            firstCookies.Add(new Uri(server.Url), new Cookie("first-only", "owned", "/"));

            await dispatcher.GetResponseAsync(new HttpRequest(server.Url) { ConnectionKeepAlive = true }, firstCookies).WaitAsync(Deadline);
            await dispatcher.GetResponseAsync(new HttpRequest(server.Url) { ConnectionKeepAlive = true }, new CookieContainer()).WaitAsync(Deadline);

            requests.Should().HaveCount(2);
            requests.TryDequeue(out var first).Should().BeTrue();
            requests.TryDequeue(out var second).Should().BeTrue();
            first.Headers["Cookie"].Should().Be("first-only=owned");
            second.Headers.Should().NotContainKey("Cookie");
            server.AcceptedConnections.Should().Be(1);
        }

        [Test]
        public async Task default_pool_lifetime_should_allow_reuse_and_be_thirty_minutes()
        {
            await using var server = Server(IPAddress.Loopback,
                (stream, stop) => ServeHttp(stream, stop, request => "reused"));
            using var dispatcher = Dispatcher();

            await dispatcher.GetResponseAsync(new HttpRequest(server.Url) { ConnectionKeepAlive = true }, new CookieContainer()).WaitAsync(Deadline);
            await dispatcher.GetResponseAsync(new HttpRequest(server.Url) { ConnectionKeepAlive = true }, new CookieContainer()).WaitAsync(Deadline);

            dispatcher.Handlers.Should().ContainSingle();
            dispatcher.Handlers[0].PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(30));
            server.AcceptedConnections.Should().Be(1);
        }

        [Test]
        public async Task completed_expired_connections_should_resolve_again_and_use_the_new_address()
        {
            await using var first = Server(IPAddress.Loopback,
                (stream, stop) => ServeHttp(stream, stop, request => "original"));
            await using var second = Server(IPAddress.Parse("127.0.0.2"),
                (stream, stop) => ServeHttp(stream, stop, request => "renewed"),
                first.Port);
            var connector = new ChangingResolver(TestLogger) { Address = IPAddress.Loopback };
            using var dispatcher = Dispatcher(connector: connector);
            dispatcher.Lifetime = TimeSpan.Zero;
            var url = "http://renewal.invalid:" + first.Port + "/";

            var original = await dispatcher.GetResponseAsync(new HttpRequest(url) { ConnectionKeepAlive = true }, new CookieContainer()).WaitAsync(Deadline);
            connector.Address = IPAddress.Parse("127.0.0.2");
            var renewed = await dispatcher.GetResponseAsync(new HttpRequest(url) { ConnectionKeepAlive = true }, new CookieContainer()).WaitAsync(Deadline);

            original.Content.Should().Be("original");
            renewed.Content.Should().Be("renewed");
            connector.Lookups.Should().Be(2, "the completed idle connection expired before it could be reused");
            first.AcceptedConnections.Should().Be(1);
            second.AcceptedConnections.Should().Be(1);
            dispatcher.Clients.Should().ContainSingle();
        }

        [Test]
        public async Task forced_expiry_should_not_abort_an_active_response_while_another_connection_opens()
        {
            var bodyStarted = Signal<bool>();
            var releaseBody = Signal<bool>();
            await using var first = Server(IPAddress.Loopback, async (stream, stop) =>
            {
                await ReadRequest(stream, stop);
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\n", stop);
                bodyStarted.TrySetResult(true);
                await releaseBody.Task.WaitAsync(stop);
                await Write(stream, "held", stop);
            });
            await using var second = Server(IPAddress.Parse("127.0.0.2"),
                (stream, stop) => ServeHttp(stream, stop, request => "renewed"),
                first.Port);
            var connector = new ChangingResolver(TestLogger) { Address = IPAddress.Loopback };
            using var dispatcher = Dispatcher(connector: connector);
            dispatcher.Lifetime = TimeSpan.Zero;
            var url = "http://renewal.invalid:" + first.Port + "/";
            var pending = dispatcher.GetResponseAsync(new HttpRequest(url) { ConnectionKeepAlive = true }, new CookieContainer());
            try
            {
                await bodyStarted.Task.WaitAsync(Deadline);
                connector.Address = IPAddress.Parse("127.0.0.2");

                var renewed = await dispatcher.GetResponseAsync(new HttpRequest(url) { ConnectionKeepAlive = true }, new CookieContainer()).WaitAsync(Deadline);

                renewed.Content.Should().Be("renewed");
                pending.IsCompleted.Should().BeFalse("pool expiry must leave an active response intact");
                connector.Lookups.Should().Be(2);
                first.AcceptedConnections.Should().Be(1);
                second.AcceptedConnections.Should().Be(1);
                dispatcher.Clients.Should().ContainSingle("expiry renews connections within the cached client");
            }
            finally
            {
                releaseBody.TrySetResult(true);
                (await pending.WaitAsync(Deadline)).Content.Should().Be("held");
            }
        }

        private InspectingDispatcher Dispatcher(HttpProxySettings settings = null, ICertificateValidationService validator = null, HttpHappyEyeballs connector = null)
        {
            var proxySettings = new Mock<IHttpProxySettingsProvider>();
            proxySettings.Setup(provider => provider.GetProxySettings(It.IsAny<HttpUri>())).Returns(settings);
            var agent = new Mock<IUserAgentBuilder>();
            agent.Setup(builder => builder.GetUserAgent(It.IsAny<bool>())).Returns("BookshelfConnectionTest/1");
            var cache = new CacheManager();
            return new InspectingDispatcher(proxySettings.Object,
                new ManagedWebProxyFactory(cache),
                validator ?? Mock.Of<ICertificateValidationService>(),
                agent.Object,
                cache,
                connector ?? new HttpHappyEyeballs(TestLogger));
        }

        private static LoopbackServer Server(IPAddress address, Func<NetworkStream, CancellationToken, Task> handler, int port = 0)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            {
                Assert.Ignore("The host kernel does not support IPv6.");
            }

            try
            {
                return new LoopbackServer(address, port, handler);
            }
            catch (SocketException error) when (address.AddressFamily == AddressFamily.InterNetworkV6 &&
                (error.SocketErrorCode == SocketError.AddressFamilyNotSupported || error.SocketErrorCode == SocketError.AddressNotAvailable || error.SocketErrorCode == SocketError.ProtocolNotSupported))
            {
                Assert.Ignore("IPv6 loopback is unavailable on this host: " + error.SocketErrorCode);
                throw;
            }
        }

        private static X509Certificate2 Certificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }

        private static Socket RefusingIPv6Port(int port)
        {
            if (!Socket.OSSupportsIPv6)
            {
                Assert.Ignore("The host kernel does not support IPv6.");
            }

            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = false };
            try
            {
                // Reserve this port without listening so the first IPv6 dial refuses.
                socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, port));
                return socket;
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressFamilyNotSupported ||
                error.SocketErrorCode == SocketError.AddressNotAvailable || error.SocketErrorCode == SocketError.ProtocolNotSupported)
            {
                socket.Dispose();
                Assert.Ignore("IPv6 loopback is unavailable on this host: " + error.SocketErrorCode);
                throw;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static TaskCompletionSource<T> Signal<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task ServeHttp(Stream stream, CancellationToken stop, Func<Request, string> body, string extraHeaders = "")
        {
            while (!stop.IsCancellationRequested)
            {
                var request = await ReadRequest(stream, stop);
                if (request == null)
                {
                    return;
                }

                var content = body(request);
                var close = request.Headers.TryGetValue("Connection", out var connection) && connection.Equals("close", StringComparison.OrdinalIgnoreCase);
                await Write(stream,
                    "HTTP/1.1 200 OK\r\nContent-Length: " + Encoding.ASCII.GetByteCount(content) + "\r\n" + extraHeaders +
                    (close ? "Connection: close\r\n" : string.Empty) + "\r\n" + content,
                    stop);
                if (close)
                {
                    return;
                }
            }
        }

        private static async Task<Request> ReadRequest(Stream stream, CancellationToken stop)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var line = await reader.ReadLineAsync(stop);
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string header;
            while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(stop)))
            {
                var colon = header.IndexOf(':');
                headers.Add(header.Substring(0, colon), header.Substring(colon + 1).Trim());
            }

            return new Request { Line = line, Headers = headers };
        }

        private static async Task Write(Stream stream, string value, CancellationToken stop)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(value), stop);
            await stream.FlushAsync(stop);
        }

        private sealed class Request
        {
            public string Line { get; set; }
            public Dictionary<string, string> Headers { get; set; }
        }

        private sealed class ChangingResolver : HttpHappyEyeballs
        {
            private IPAddress _address;
            private int _lookups;

            public ChangingResolver(NLog.Logger logger)
                : base(logger)
            {
            }

            public IPAddress Address
            {
                get => Volatile.Read(ref _address);
                set => Volatile.Write(ref _address, value);
            }

            public int Lookups => Volatile.Read(ref _lookups);

            protected override Task<IPAddress[]> ResolveAddresses(DnsEndPoint endPoint, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _lookups);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new[] { Address });
            }
        }

        private sealed class OriginResolver : HttpHappyEyeballs
        {
            private readonly int _firstPort;

            public OriginResolver(int firstPort, NLog.Logger logger)
                : base(logger)
            {
                _firstPort = firstPort;
            }

            public ConcurrentQueue<IPAddress> Attempts { get; } = new ConcurrentQueue<IPAddress>();

            protected override Task<IPAddress[]> ResolveAddresses(DnsEndPoint endPoint, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(endPoint.Port == _firstPort
                    ? new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }
                    : new[] { IPAddress.IPv6Loopback });
            }

            protected override Task<Socket> ConnectSocket(IPAddress address, DnsEndPoint endPoint, CancellationToken cancellationToken)
            {
                Attempts.Enqueue(address);
                return base.ConnectSocket(address, endPoint, cancellationToken);
            }
        }

        private sealed class InspectingDispatcher : ManagedHttpDispatcher, IDisposable
        {
            public InspectingDispatcher(IHttpProxySettingsProvider proxySettings,
                ICreateManagedWebProxy webProxy,
                ICertificateValidationService validator,
                IUserAgentBuilder agent,
                ICacheManager cache,
                HttpHappyEyeballs connector)
                : base(proxySettings, webProxy, validator, agent, cache, connector)
            {
            }

            public List<System.Net.Http.HttpClient> Clients { get; } = new List<System.Net.Http.HttpClient>();
            public List<SocketsHttpHandler> Handlers { get; } = new List<SocketsHttpHandler>();
            public TimeSpan? Lifetime { get; set; }

            public void Dispose()
            {
                foreach (var client in Clients)
                {
                    client.Dispose();
                }
            }

            protected override SocketsHttpHandler CreateHttpHandler(HttpProxySettings proxySettings)
            {
                var handler = base.CreateHttpHandler(proxySettings);
                handler.UseProxy = proxySettings != null;
                if (Lifetime.HasValue)
                {
                    handler.PooledConnectionLifetime = Lifetime.Value;
                }

                Handlers.Add(handler);
                return handler;
            }

            protected override System.Net.Http.HttpClient CreateHttpClient(HttpProxySettings proxySettings)
            {
                var client = base.CreateHttpClient(proxySettings);
                Clients.Add(client);
                return client;
            }
        }

        private sealed class LoopbackServer : IAsyncDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
            private readonly ConcurrentBag<Task> _connections = new ConcurrentBag<Task>();
            private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
            private readonly Func<NetworkStream, CancellationToken, Task> _handler;
            private readonly Task _accept;
            private int _accepted;

            public LoopbackServer(IPAddress address, int port, Func<NetworkStream, CancellationToken, Task> handler)
            {
                _handler = handler;
                _listener = new TcpListener(address, port);
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _listener.Server.DualMode = false;
                }

                try
                {
                    _listener.Start();
                }
                catch
                {
                    _listener.Stop();
                    _stopping.Dispose();
                    throw;
                }

                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Url = "http://" + (address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString()) + ":" + Port + "/";
                _accept = Accept();
            }

            public string Url { get; }
            public int Port { get; }
            public int AcceptedConnections => Volatile.Read(ref _accepted);

            public async ValueTask DisposeAsync()
            {
                try
                {
                    // Cancel and join the pending accept before closing the listening
                    // socket. Closing it during cancellation can produce a teardown
                    // SocketException even when every test request succeeded.
                    await _stopping.CancelAsync();
                    await _accept.WaitAsync(Deadline);
                }
                finally
                {
                    _listener.Stop();
                    foreach (var client in _clients)
                    {
                        client.Dispose();
                    }

                    try
                    {
                        await Task.WhenAll(_connections).WaitAsync(Deadline);
                    }
                    finally
                    {
                        _stopping.Dispose();
                    }
                }
            }

            private async Task Accept()
            {
                try
                {
                    while (!_stopping.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                        _clients.Add(client);
                        Interlocked.Increment(ref _accepted);
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
                {
                    try
                    {
                        await _handler(stream, _stopping.Token);
                    }
                    catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                    {
                    }
                    catch (ObjectDisposedException) when (_stopping.IsCancellationRequested)
                    {
                    }
                    catch (IOException)
                    {
                        // Peers may close a pooled connection or reject the test certificate.
                    }
                    catch (AuthenticationException)
                    {
                        // The certificate-rejection case intentionally ends the TLS handshake.
                    }
                }
            }
        }
    }
}
