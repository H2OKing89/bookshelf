using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Common.Http.HappyEyeballs
{
    // Adapted from Sonarr v5.0.0.2941 (c018578e4b79ee048575c02d7a8aa2b929e4b837),
    // whose implementation derives from https://slugcat.systems/post/24-06-16-ipv6-is-hard-happy-eyeballs-dotnet-httpclient/.
    // Keep connection ownership here so synchronous failures and cancellation
    // cannot abandon an attempt or return more than one successful connection.
    public class HappyEyeballs<TSocket>
        where TSocket : IDisposable
    {
        private readonly Func<IPAddress, CancellationToken, Task<TSocket>> _connectSocket;
        private readonly Func<CancellationToken, Task> _taskDelay;

        public HappyEyeballs(Func<IPAddress, CancellationToken, Task<TSocket>> connectSocket,
                            Func<CancellationToken, Task> taskDelay)
        {
            ArgumentNullException.ThrowIfNull(connectSocket);
            ArgumentNullException.ThrowIfNull(taskDelay);
            _connectSocket = connectSocket;
            _taskDelay = taskDelay;
        }

        public async ValueTask<TSocket> Connect(IPAddress[] addresses, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(addresses);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(addresses.Length);
            cancellationToken.ThrowIfCancellationRequested();

            var ordered = SortInterleaved(addresses);
            var attempts = new List<Task<TSocket>>();
            var pending = new List<Task<TSocket>>();
            var failures = new List<Exception>();
            Task<TSocket> winner = null;
            TSocket connection = default;
            var transferred = false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                try
                {
                    var next = 0;
                    while (next < ordered.Length || pending.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var completed = pending.FirstOrDefault(task => task.IsCompleted);
                        if (completed == null && next < ordered.Length)
                        {
                            var attempt = ConnectSocket(ordered[next++], cancellation.Token);
                            attempts.Add(attempt);
                            pending.Add(attempt);
                            completed = pending.FirstOrDefault(task => task.IsCompleted);
                        }

                        if (completed == null)
                        {
                            if (next < ordered.Length)
                            {
                                using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                                var delay = Delay(delayCancellation.Token);
                                try
                                {
                                    var completedAttempt = Task.WhenAny(pending);
                                    var first = await Task.WhenAny(completedAttempt, delay).WaitAsync(cancellationToken).ConfigureAwait(false);
                                    if (first == delay)
                                    {
                                        // A failed delay is not a successful stagger interval.
                                        await delay.ConfigureAwait(false);
                                        continue;
                                    }

                                    completed = await completedAttempt.ConfigureAwait(false);
                                }
                                finally
                                {
                                    await delayCancellation.CancelAsync().ConfigureAwait(false);
                                    try
                                    {
                                        await delay.ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
                                    {
                                    }
                                }
                            }
                            else
                            {
                                completed = await Task.WhenAny(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
                            }
                        }

                        pending.Remove(completed);
                        try
                        {
                            connection = await completed.ConfigureAwait(false);
                            winner = completed;
                            break;
                        }
                        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                        {
                            failures.Add(error);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await DisposeLosers(attempts, winner).ConfigureAwait(false);
                    }
                }

                // Cancellation can arrive while a losing attempt is finishing.
                // The winner is still owned here until this final handoff.
                cancellationToken.ThrowIfCancellationRequested();
                if (winner == null)
                {
                    throw new AggregateException("All connection attempts failed.", failures);
                }

                transferred = true;
                return connection;
            }
            catch (Exception error) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Connection attempt cancelled.", error, cancellationToken);
            }
            finally
            {
                if (!transferred && winner != null)
                {
                    connection.Dispose();
                }
            }
        }

        private async Task<TSocket> ConnectSocket(IPAddress address, CancellationToken cancellationToken)
        {
            // An async wrapper turns a synchronously thrown connector exception
            // into a tracked failed attempt, allowing the other family to proceed.
            return await _connectSocket(address, cancellationToken).ConfigureAwait(false);
        }

        private async Task Delay(CancellationToken cancellationToken)
        {
            await _taskDelay(cancellationToken).ConfigureAwait(false);
        }

        private static IPAddress[] SortInterleaved(IPAddress[] addresses)
        {
            var ipv6 = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
            var ipv4 = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).ToArray();
            var ordered = new List<IPAddress>(addresses.Length);
            for (var index = 0; index < Math.Max(ipv6.Length, ipv4.Length); index++)
            {
                if (index < ipv6.Length)
                {
                    ordered.Add(ipv6[index]);
                }

                if (index < ipv4.Length)
                {
                    ordered.Add(ipv4[index]);
                }
            }

            return ordered.ToArray();
        }

        private static async Task DisposeLosers(IEnumerable<Task<TSocket>> attempts, Task<TSocket> winner)
        {
            List<Exception> failures = null;
            foreach (var attempt in attempts.Where(attempt => attempt != winner))
            {
                TSocket connection;
                try
                {
                    connection = await attempt.ConfigureAwait(false);
                }
                catch
                {
                    // Failed and cancelled attempts own no returned connection.
                    continue;
                }

                try
                {
                    connection.Dispose();
                }
                catch (Exception error)
                {
                    (failures ??= new List<Exception>()).Add(error);
                }
            }

            if (failures != null)
            {
                throw new AggregateException("Failed to dispose losing connections.", failures);
            }
        }
    }
}
