using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource
{
    public enum MetadataProvider
    {
        Audnexus,
        Audible
    }

    public enum MetadataProviderOperation
    {
        AuthorCatalog,
        BookSearch,
        AuthorSearch,
        AuthorProfile,
        RecordingDetails,
        Chapters,
        AuthorRefresh,
        RecordingRefresh
    }

    public enum MetadataProviderState
    {
        Complete,
        Cached,
        Partial,
        RateLimited,
        Unavailable,
        Missing,
        IdentityReview,
        Cancelled
    }

    public enum MetadataProviderReason
    {
        KeywordCoverage,
        SparseMetadata,
        RepeatedPage,
        IncompletePagination,
        CatalogLimit,
        TooManyRequests,
        Timeout,
        ServerError,
        HttpError,
        InvalidResponse,
        Configuration,
        Transport,
        IdentityConflict,
        ConfirmedMissing
    }

    public interface IMetadataProviderStatusService
    {
        List<MetadataProviderStatus> GetStatus();
        void Report(MetadataProviderStatus status);
    }

    public sealed class MetadataProviderStatus
    {
        public MetadataProviderStatus(MetadataProvider provider,
                                      MetadataProviderOperation operation,
                                      MetadataProviderState state,
                                      MetadataProviderReason? reason,
                                      DateTime observedAtUtc,
                                      DateTime? lastCompleteAtUtc,
                                      DateTime? retryAfterUtc)
        {
            Provider = provider;
            Operation = operation;
            State = state;
            Reason = reason;
            ObservedAtUtc = observedAtUtc;
            LastCompleteAtUtc = lastCompleteAtUtc;
            RetryAfterUtc = retryAfterUtc;
        }

        public MetadataProvider Provider { get; }
        public MetadataProviderOperation Operation { get; }
        public MetadataProviderState State { get; }
        public MetadataProviderReason? Reason { get; }
        public DateTime ObservedAtUtc { get; }
        public DateTime? LastCompleteAtUtc { get; }
        public DateTime? RetryAfterUtc { get; }
    }

    public class MetadataProviderStatusService : IMetadataProviderStatusService
    {
        private readonly object _gate = new object();
        private readonly Dictionary<(MetadataProvider, MetadataProviderOperation), MetadataProviderStatus> _statuses = new Dictionary<(MetadataProvider, MetadataProviderOperation), MetadataProviderStatus>();

        public List<MetadataProviderStatus> GetStatus()
        {
            lock (_gate)
            {
                return _statuses.Values.OrderBy(status => status.Provider).ThenBy(status => status.Operation).ToList();
            }
        }

        public void Report(MetadataProviderStatus status)
        {
            lock (_gate)
            {
                var key = (status.Provider, status.Operation);
                var previous = _statuses.GetValueOrDefault(key);
                var latest = previous != null && previous.ObservedAtUtc > status.ObservedAtUtc ? previous : status;
                var lastComplete = new[] { previous?.LastCompleteAtUtc, status.LastCompleteAtUtc }.Max();
                _statuses[key] = new MetadataProviderStatus(latest.Provider, latest.Operation, latest.State, latest.Reason, latest.ObservedAtUtc, lastComplete, latest.RetryAfterUtc);
            }
        }
    }

    internal sealed class MetadataProviderIssue
    {
        public MetadataProviderIssue(MetadataProviderState state, MetadataProviderReason reason, DateTime? retryAfterUtc = null)
        {
            State = state;
            Reason = reason;
            RetryAfterUtc = retryAfterUtc;
        }

        public MetadataProviderState State { get; }
        public MetadataProviderReason Reason { get; }
        public DateTime? RetryAfterUtc { get; }

        public static MetadataProviderIssue FromHttpStatus(int? status, DateTime? retryAfterUtc)
        {
            return status switch
            {
                429 => new MetadataProviderIssue(MetadataProviderState.RateLimited, MetadataProviderReason.TooManyRequests, retryAfterUtc),
                >= 500 => new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.ServerError, retryAfterUtc),
                null or 200 => new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.InvalidResponse, retryAfterUtc),
                _ => new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.HttpError, retryAfterUtc)
            };
        }

        public static MetadataProviderIssue FromTransport(Exception error, DateTime retryAfterUtc)
        {
            var timeout = error is TimeoutException || error is WebException { Status: WebExceptionStatus.Timeout };
            return new MetadataProviderIssue(MetadataProviderState.Unavailable, timeout ? MetadataProviderReason.Timeout : MetadataProviderReason.Transport, retryAfterUtc);
        }

        public static DateTime RetryAfter(HttpHeader headers)
        {
            var now = DateTime.UtcNow;
            var retryAfter = headers?.GetSingleValue("Retry-After");
            if (int.TryParse(retryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                return now.AddSeconds(seconds);
            }

            var defaultRetry = now.AddMinutes(1);
            return DateTime.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var date) && date > defaultRetry
                ? date
                : defaultRetry;
        }
    }

    internal class MetadataProviderObservation
    {
        private MetadataProviderIssue _issue;
        private MetadataProvider? _provider;
        private bool _cached;
        private DateTime? _cacheSourceTime;

        public void UseCached(DateTime? completedAtUtc = null)
        {
            _cached = true;
            _cacheSourceTime = completedAtUtc;
        }

        public void Flag(MetadataProviderState state, MetadataProviderReason reason)
        {
            _issue = new MetadataProviderIssue(state, reason);
        }

        public void Partial(MetadataProviderReason reason)
        {
            _issue ??= new MetadataProviderIssue(MetadataProviderState.Partial, reason);
        }

        public void UseProvider(MetadataProvider provider)
        {
            _provider = provider;
        }

        public static T Observe<T>(IMetadataProviderStatusService status,
                                   MetadataProvider provider,
                                   MetadataProviderOperation operation,
                                   Func<MetadataProviderObservation, T> read)
        {
            var observation = new MetadataProviderObservation();
            try
            {
                var result = read(observation);
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                observation.Report(status, provider, operation);
                return result;
            }
            catch (OperationCanceledException) when (CommandExecutionContext.CancellationToken.IsCancellationRequested)
            {
                status.Report(new MetadataProviderStatus(observation._provider ?? provider, operation, MetadataProviderState.Cancelled, null, DateTime.UtcNow, null, null));
                throw;
            }
            catch (BookNotFoundException)
            {
                observation.Flag(MetadataProviderState.Missing, MetadataProviderReason.ConfirmedMissing);
                observation.Report(status, provider, operation);
                throw;
            }
            catch (BookInfoException error)
            {
                observation._issue = error.ProviderIssue ??
                    new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.Transport);
                observation.Report(status, provider, operation);
                throw;
            }
        }

        private void Report(IMetadataProviderStatusService status, MetadataProvider provider, MetadataProviderOperation operation)
        {
            var now = DateTime.UtcNow;
            var state = _issue?.State ?? (_cached ? MetadataProviderState.Cached : MetadataProviderState.Complete);
            var lastComplete = state == MetadataProviderState.Complete ? now : _cacheSourceTime;
            status.Report(new MetadataProviderStatus(_provider ?? provider, operation, state, _issue?.Reason, now, lastComplete, _issue?.RetryAfterUtc));
        }
    }
}
