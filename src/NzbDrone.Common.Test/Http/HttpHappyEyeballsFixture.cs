using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http.HappyEyeballs;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HttpHappyEyeballsFixture : TestBase
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
        private Connector _connector;

        [SetUp]
        public void Setup()
        {
            _connector = new Connector(TestLogger);
        }

        [Test]
        public void null_endpoint_should_be_rejected_without_dns()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _connector.Connect(null));

            _connector.Resolutions.Should().BeEmpty();
        }

        [Test]
        public void precancellation_should_stop_before_dns()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await _connector.Connect(EndPoint(), cancellation.Token));

            error.CancellationToken.Should().Be(cancellation.Token);
            _connector.Resolutions.Should().BeEmpty();
            _connector.Attempts.Should().BeEmpty();
        }

        [Test]
        public async Task cancellation_during_dns_should_keep_the_original_caller_token()
        {
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connector.Resolve = async (endPoint, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Array.Empty<IPAddress>();
            };
            var pending = _connector.Connect(EndPoint(), cancellation.Token).AsTask();
            await entered.Task.WaitAsync(Deadline);

            cancellation.Cancel();

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            error.CancellationToken.Should().Be(cancellation.Token);
            _connector.Attempts.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void cancellation_at_dns_completion_should_prevent_dialing_and_win_a_dns_failure(bool failure)
        {
            using var cancellation = new CancellationTokenSource();
            _connector.Resolve = (endPoint, token) =>
            {
                cancellation.Cancel();
                return failure
                    ? Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound))
                    : Task.FromResult(new[] { IPAddress.Loopback });
            };

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await _connector.Connect(EndPoint(), cancellation.Token));

            error.CancellationToken.Should().Be(cancellation.Token);
            _connector.Attempts.Should().BeEmpty();
        }

        [Test]
        public void a_dns_failure_should_not_start_a_socket_attempt()
        {
            var failure = new SocketException((int)SocketError.HostNotFound);
            _connector.Resolve = (endPoint, token) => Task.FromException<IPAddress[]>(failure);

            var error = Assert.ThrowsAsync<SocketException>(async () => await _connector.Connect(EndPoint()));

            error.Should().BeSameAs(failure);
            _connector.Attempts.Should().BeEmpty();
        }

        [Test]
        public void an_empty_dns_answer_should_be_a_name_resolution_failure()
        {
            _connector.Resolve = (endPoint, token) => Task.FromResult(Array.Empty<IPAddress>());

            var error = Assert.ThrowsAsync<WebException>(async () => await _connector.Connect(EndPoint()));

            error.Status.Should().Be(WebExceptionStatus.NameResolutionFailure);
            _connector.Attempts.Should().BeEmpty();
        }

        [Test]
        public void an_ipv4_only_platform_should_not_dial_ipv6_addresses()
        {
            _connector.Supported.Remove(AddressFamily.InterNetworkV6);
            _connector.Resolve = (endPoint, token) => Task.FromResult(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback });

            Assert.ThrowsAsync<AggregateException>(async () => await _connector.Connect(EndPoint()));

            _connector.Attempts.Should().Equal(IPAddress.Loopback);
        }

        [Test]
        public void an_ipv6_only_platform_should_not_dial_ipv4_addresses()
        {
            _connector.Supported.Remove(AddressFamily.InterNetwork);
            _connector.Resolve = (endPoint, token) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback });

            Assert.ThrowsAsync<AggregateException>(async () => await _connector.Connect(EndPoint()));

            _connector.Attempts.Should().Equal(IPAddress.IPv6Loopback);
        }

        [TestCase(AddressFamily.InterNetwork)]
        [TestCase(AddressFamily.InterNetworkV6)]
        public void an_explicit_endpoint_family_should_filter_the_dns_answer(AddressFamily family)
        {
            var endPoint = new DnsEndPoint("synthetic.invalid", 80, family);
            _connector.Resolve = (target, token) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback });

            Assert.ThrowsAsync<AggregateException>(async () => await _connector.Connect(endPoint));

            _connector.Resolutions.Should().ContainSingle().Which.Should().BeSameAs(endPoint);
            _connector.Attempts.Should().Equal(family == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback);
        }

        [Test]
        public void unsupported_dns_answers_should_fail_without_allocating_a_socket()
        {
            _connector.Supported.Clear();

            var error = Assert.ThrowsAsync<SocketException>(async () => await _connector.Connect(EndPoint()));

            error.SocketErrorCode.Should().Be(SocketError.AddressFamilyNotSupported);
            _connector.Attempts.Should().BeEmpty();
        }

        [Test]
        public void duplicate_dns_answers_should_not_repeat_failed_connections()
        {
            _connector.Resolve = (endPoint, token) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.Loopback });

            var error = Assert.ThrowsAsync<AggregateException>(async () => await _connector.Connect(EndPoint()));

            _connector.Attempts.Should().Equal(IPAddress.Loopback);
            error.InnerExceptions.Should().ContainSingle();
        }

        [Test]
        public void stream_construction_failure_should_close_the_selected_socket()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _connector.Dial = (address, endPoint, token) => Task.FromResult(socket);

            Assert.ThrowsAsync<IOException>(async () => await _connector.Connect(EndPoint()));

            socket.SafeHandle.IsClosed.Should().BeTrue();
        }

        [Test]
        public void cancellation_at_socket_completion_should_dispose_the_late_socket()
        {
            using var cancellation = new CancellationTokenSource();
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _connector.Dial = (address, endPoint, token) =>
            {
                cancellation.Cancel();
                return Task.FromResult(socket);
            };

            var error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await _connector.Connect(EndPoint(), cancellation.Token));

            error.CancellationToken.Should().Be(cancellation.Token);
            socket.SafeHandle.IsClosed.Should().BeTrue();
        }

        private static DnsEndPoint EndPoint()
        {
            return new DnsEndPoint("synthetic.invalid", 80);
        }

        private sealed class Connector : HttpHappyEyeballs
        {
            public Connector(NLog.Logger logger)
                : base(logger)
            {
            }

            public Func<DnsEndPoint, CancellationToken, Task<IPAddress[]>> Resolve { get; set; } =
                (endPoint, token) => Task.FromResult(new[] { IPAddress.Loopback });

            public Func<IPAddress, DnsEndPoint, CancellationToken, Task<Socket>> Dial { get; set; } =
                (address, endPoint, token) => Task.FromException<Socket>(new SocketException((int)SocketError.ConnectionRefused));

            public HashSet<AddressFamily> Supported { get; } = new HashSet<AddressFamily>
            {
                AddressFamily.InterNetwork,
                AddressFamily.InterNetworkV6
            };

            public List<DnsEndPoint> Resolutions { get; } = new List<DnsEndPoint>();
            public List<IPAddress> Attempts { get; } = new List<IPAddress>();

            protected override Task<IPAddress[]> ResolveAddresses(DnsEndPoint endPoint, CancellationToken cancellationToken)
            {
                Resolutions.Add(endPoint);
                return Resolve(endPoint, cancellationToken);
            }

            protected override bool IsAddressFamilySupported(AddressFamily addressFamily)
            {
                return Supported.Contains(addressFamily);
            }

            protected override Task<Socket> ConnectSocket(IPAddress address, DnsEndPoint endPoint, CancellationToken cancellationToken)
            {
                Attempts.Add(address);
                return Dial(address, endPoint, cancellationToken);
            }
        }
    }
}
