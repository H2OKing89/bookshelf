using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Common.Http.HappyEyeballs
{
    public class HttpHappyEyeballs
    {
        private const int ConnectionAttemptDelay = 250;
        private readonly Logger _logger;

        public HttpHappyEyeballs(Logger logger)
        {
            _logger = logger;
        }

        public ValueTask<Stream> OnConnect(SocketsHttpConnectionContext context, CancellationToken cancellationToken = default)
        {
            return Connect(context.DnsEndPoint, cancellationToken);
        }

        public async ValueTask<Stream> Connect(DnsEndPoint endPoint, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(endPoint);
            try
            {
                return await ConnectCore(endPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Connection attempt cancelled.", error, cancellationToken);
            }
        }

        protected virtual Task<IPAddress[]> ResolveAddresses(DnsEndPoint endPoint, CancellationToken cancellationToken)
        {
            return Dns.GetHostAddressesAsync(endPoint.Host, endPoint.AddressFamily, cancellationToken);
        }

        protected virtual bool IsAddressFamilySupported(AddressFamily addressFamily)
        {
            return addressFamily == AddressFamily.InterNetwork
                ? Socket.OSSupportsIPv4
                : addressFamily == AddressFamily.InterNetworkV6 && Socket.OSSupportsIPv6;
        }

        protected virtual Task TaskDelay(CancellationToken cancellationToken)
        {
            return Task.Delay(TimeSpan.FromMilliseconds(ConnectionAttemptDelay), cancellationToken);
        }

        protected virtual async Task<Socket> ConnectSocket(IPAddress address, DnsEndPoint endPoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.NoDelay = true;
                if (address.IsIPv4MappedToIPv6)
                {
                    socket.DualMode = true;
                }

                _logger.Trace("Trying Happy Eyeballs connection to {0} for host {1}", address, endPoint);
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return socket;
            }
            catch (Exception error)
            {
                socket.Dispose();
                _logger.Trace("Failed Happy Eyeballs connection to {0} for host {1} ({2})", address, endPoint, error.Message);
                throw;
            }
        }

        private async ValueTask<Stream> ConnectCore(DnsEndPoint endPoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await ResolveAddresses(endPoint, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (resolved.Length == 0)
            {
                throw new WebException($"The remote name {endPoint.Host} could not be resolved", WebExceptionStatus.NameResolutionFailure);
            }

            var addresses = resolved.Where(address =>
                    (endPoint.AddressFamily == AddressFamily.Unspecified || address.AddressFamily == endPoint.AddressFamily) &&
                    IsAddressFamilySupported(address.AddressFamily))
                .Distinct().ToArray();
            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.AddressFamilyNotSupported);
            }

            var connector = new HappyEyeballs<Socket>(
                (address, token) => ConnectSocket(address, endPoint, token), TaskDelay);
            var socket = await connector.Connect(addresses, cancellationToken).ConfigureAwait(false);
            NetworkStream stream = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Trace("Successfully connected to {0} for host {1}", socket.RemoteEndPoint, endPoint);
                stream = new NetworkStream(socket, ownsSocket: true);
                cancellationToken.ThrowIfCancellationRequested();
                return stream;
            }
            catch
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
                else
                {
                    socket.Dispose();
                }

                throw;
            }
        }
    }
}
