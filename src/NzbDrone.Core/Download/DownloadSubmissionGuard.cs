using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IDownloadSubmissionGuard
    {
        List<DownloadSubmissionHold> GetPending();
        bool Acknowledge(string attemptId, int downloadClientId);
        void EnsureCanSubmit(RemoteBook remoteBook);
        string Begin(RemoteBook remoteBook, IDownloadClient client);
        void MarkUncertain(string attemptId, string expectedDownloadId);
        void Complete(string attemptId);
    }

    public class DownloadSubmissionHold
    {
        public string AttemptId { get; set; }
        public int DownloadClientId { get; set; }
        public string DownloadClientName { get; set; }
        public int AuthorId { get; set; }
        public int[] BookIds { get; set; }
        public string Title { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string ExpectedDownloadId { get; set; }
        public string Reason { get; set; }
        public bool CanAcknowledge { get; set; }
    }

    public class DownloadSubmissionGuard : IDownloadSubmissionGuard
    {
        private readonly IDownloadHistoryRepository _repository;
        private readonly object _gate = new object();
        private readonly HashSet<string> _activeAttempts = new HashSet<string>();

        public DownloadSubmissionGuard(IDownloadHistoryRepository repository)
        {
            _repository = repository;
        }

        public List<DownloadSubmissionHold> GetPending()
        {
            lock (_gate)
            {
                return Pending().Select(history =>
                {
                    var hold = ToHold(history);
                    hold.CanAcknowledge = !_activeAttempts.Contains(hold.AttemptId);
                    return hold;
                }).ToList();
            }
        }

        public void EnsureCanSubmit(RemoteBook remoteBook)
        {
            lock (_gate)
            {
                EnsureCanSubmitCore(remoteBook);
            }
        }

        private void EnsureCanSubmitCore(RemoteBook remoteBook)
        {
            var bookIds = remoteBook.Books.Select(book => book.Id).Where(id => id > 0).ToHashSet();
            if (bookIds.Count == 0)
            {
                throw new ArgumentException("At least one persisted book identity is required before submitting a download.");
            }

            foreach (var pending in Pending())
            {
                var heldIds = BookIds(pending);

                // A damaged marker cannot authorize another submission. Scope
                // unknown book identities to the author, or globally if lost.
                if ((heldIds.Length == 0 && (pending.AuthorId <= 0 || pending.AuthorId == remoteBook.Author.Id)) ||
                    heldIds.Any(bookIds.Contains))
                {
                    throw new DownloadClientSubmissionUncertainException(
                        $"A previous submission to {Data(pending, "DownloadClientName")} has an unknown outcome. " +
                        $"Review submission attempt {AttemptId(pending)} before grabbing these books again.");
                }
            }
        }

        public string Begin(RemoteBook remoteBook, IDownloadClient client)
        {
            lock (_gate)
            {
                EnsureCanSubmitCore(remoteBook);
                var attemptId = Guid.NewGuid().ToString("N");
                var marker = new DownloadHistory
                {
                    EventType = DownloadHistoryEventType.DownloadSubmissionPending,
                    DownloadId = "submission:" + attemptId,
                    DownloadClientId = client.Definition.Id,
                    AuthorId = remoteBook.Author.Id,
                    SourceTitle = remoteBook.Release.Title,
                    Protocol = remoteBook.Release.DownloadProtocol,
                    IndexerId = remoteBook.Release.IndexerId,
                    Date = DateTime.UtcNow
                };
                marker.Data["AttemptId"] = attemptId;
                marker.Data["DownloadClientName"] = client.Definition.Name;
                marker.Data["BookIds"] = STJson.ToJson(remoteBook.Books.Select(book => book.Id).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray());

                // Persist before invoking any client add. A crash between the
                // request and its response must fail closed after restart.
                var persisted = _repository.Insert(marker);
                if (persisted?.Id is not > 0)
                {
                    throw new InvalidOperationException("The download submission hold could not be confirmed in storage; no add was authorized.");
                }

                _activeAttempts.Add(attemptId);
                return attemptId;
            }
        }

        public void MarkUncertain(string attemptId, string expectedDownloadId)
        {
            lock (_gate)
            {
                _activeAttempts.Remove(attemptId);
                var marker = Find(attemptId);
                if (marker == null)
                {
                    return;
                }

                SetData(marker, "ExpectedDownloadId", expectedDownloadId);
                _repository.Update(marker);
            }
        }

        public void Complete(string attemptId)
        {
            lock (_gate)
            {
                _activeAttempts.Remove(attemptId);
                var marker = Find(attemptId);
                if (marker != null)
                {
                    _repository.Delete(marker);
                }
            }
        }

        public bool Acknowledge(string attemptId, int downloadClientId)
        {
            lock (_gate)
            {
                var marker = Find(attemptId);
                if (marker == null || marker.DownloadClientId != downloadClientId || _activeAttempts.Contains(attemptId))
                {
                    return false;
                }

                // Preserve the operator's acknowledgement as a neutral audit
                // record. It is not a grab/import receipt or cleanup authority.
                SetData(marker, "AcknowledgedUtc", DateTime.UtcNow.ToString("O"));
                _repository.Update(marker);
                return true;
            }
        }

        private DownloadHistory Find(string attemptId)
        {
            return Pending().SingleOrDefault(history => AttemptId(history) == attemptId);
        }

        private IEnumerable<DownloadHistory> Pending()
        {
            return _repository.GetSubmissionAttempts().Where(history =>
                !DateTime.TryParseExact(Data(history, "AcknowledgedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var acknowledged) ||
                acknowledged.Kind != DateTimeKind.Utc || acknowledged < history.Date);
        }

        private static string AttemptId(DownloadHistory history)
        {
            var stored = Data(history, "AttemptId");
            return !string.IsNullOrEmpty(stored) ? stored : history.DownloadId?.Replace("submission:", string.Empty, StringComparison.Ordinal);
        }

        private static string Data(DownloadHistory history, string key)
        {
            var values = history.Data?.Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value).Distinct(StringComparer.Ordinal).ToList();
            return values?.Count == 1 ? values[0] : null;
        }

        private static void SetData(DownloadHistory history, string key, string value)
        {
            history.Data ??= new Dictionary<string, string>();
            foreach (var existingKey in history.Data.Keys.Where(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                history.Data.Remove(existingKey);
            }

            history.Data[key] = value;
        }

        private static int[] BookIds(DownloadHistory history)
        {
            try
            {
                return STJson.Deserialize<List<int>>(Data(history, "BookIds"))?.Where(id => id > 0).ToArray() ?? Array.Empty<int>();
            }
            catch (Exception)
            {
                return Array.Empty<int>();
            }
        }

        private static DownloadSubmissionHold ToHold(DownloadHistory history)
        {
            return new DownloadSubmissionHold
            {
                AttemptId = AttemptId(history),
                DownloadClientId = history.DownloadClientId,
                DownloadClientName = Data(history, "DownloadClientName"),
                AuthorId = history.AuthorId,
                BookIds = BookIds(history),
                Title = history.SourceTitle,
                CreatedUtc = history.Date,
                ExpectedDownloadId = Data(history, "ExpectedDownloadId"),
                Reason = "The client may have accepted this submission. Check that client before acknowledging this hold and retrying."
            };
        }
    }
}
