using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryService
    {
        bool DownloadAlreadyImported(string downloadId);
        DownloadHistory GetLatestDownloadHistoryItem(string downloadId);
        DownloadHistory GetLatestDownloadHistoryItem(string downloadId, int downloadClientId);
        DownloadHistory GetLatestGrab(string downloadId);
        DownloadHistory GetLatestGrab(string downloadId, int downloadClientId);
        DownloadHistory GetLatestImportCompletion(string downloadId, int downloadClientId);
        DownloadHistory RecordImportCompletion(TrackedDownload trackedDownload, DownloadImportReceipt receipt);
        DownloadHistory RecordSourceRetention(TrackedDownload trackedDownload);
        bool KeepSources(TrackedDownload trackedDownload);
    }

    public class DownloadHistoryService : IDownloadHistoryService,
                                          IHandle<BookGrabbedEvent>,
                                          IHandle<TrackImportedEvent>,
                                          IHandle<BookImportIncompleteEvent>,
                                          IHandle<DownloadCompletedEvent>,
                                          IHandle<DownloadFailedEvent>,
                                          IHandle<DownloadIgnoredEvent>,
                                          IHandle<AuthorDeletedEvent>
    {
        public const string ImportReceiptKey = "importReceipt";
        public const int MaximumReceiptLength = 4 * 1024 * 1024;

        private readonly IDownloadHistoryRepository _repository;
        private readonly IHistoryService _historyService;
        private readonly object _completionGate = new object();

        public DownloadHistoryService(IDownloadHistoryRepository repository, IHistoryService historyService)
        {
            _repository = repository;
            _historyService = historyService;
        }

        public bool DownloadAlreadyImported(string downloadId)
        {
            var events = _repository.FindByDownloadId(downloadId);

            // Events are ordered by date descending, if a grabbed event comes before an imported event then it was never imported
            // or grabbed again after importing and should be reprocessed.
            foreach (var e in events)
            {
                if (e.EventType == DownloadHistoryEventType.DownloadGrabbed)
                {
                    return false;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadImported)
                {
                    return true;
                }
            }

            return false;
        }

        public DownloadHistory GetLatestDownloadHistoryItem(string downloadId)
        {
            var events = _repository.FindByDownloadId(downloadId);

            // Events are ordered by date descending. We'll return the most recent expected event.
            foreach (var e in events)
            {
                if (e.EventType == DownloadHistoryEventType.DownloadIgnored)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadGrabbed)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadImported)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadFailed)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadImportIncomplete)
                {
                    return e;
                }
            }

            return null;
        }

        public DownloadHistory GetLatestDownloadHistoryItem(string downloadId, int downloadClientId)
        {
            return _repository.FindByDownloadId(downloadId)
                .Where(history => history.DownloadClientId == downloadClientId ||
                    (history.DownloadClientId == 0 && history.EventType == DownloadHistoryEventType.DownloadIgnored))
                .Where(history => history.EventType == DownloadHistoryEventType.DownloadIgnored ||
                    history.EventType == DownloadHistoryEventType.DownloadGrabbed || history.EventType == DownloadHistoryEventType.DownloadImported ||
                    history.EventType == DownloadHistoryEventType.DownloadFailed || history.EventType == DownloadHistoryEventType.DownloadImportIncomplete)
                .OrderByDescending(history => history.Date).ThenByDescending(history => history.Id).FirstOrDefault();
        }

        public DownloadHistory GetLatestGrab(string downloadId, int downloadClientId)
        {
            return _repository.FindByDownloadId(downloadId)
                .Where(history => history.DownloadClientId == downloadClientId && history.EventType == DownloadHistoryEventType.DownloadGrabbed)
                .OrderByDescending(history => history.Date).ThenByDescending(history => history.Id).FirstOrDefault();
        }

        public DownloadHistory GetLatestGrab(string downloadId)
        {
            return _repository.FindByDownloadId(downloadId)
                .FirstOrDefault(d => d.EventType == DownloadHistoryEventType.DownloadGrabbed);
        }

        public DownloadHistory GetLatestImportCompletion(string downloadId, int downloadClientId)
        {
            return _repository.FindByDownloadId(downloadId)
                .Where(history => history.EventType == DownloadHistoryEventType.DownloadImported && history.DownloadClientId == downloadClientId)
                .OrderByDescending(history => history.Date).ThenByDescending(history => history.Id).FirstOrDefault();
        }

        public DownloadHistory RecordImportCompletion(TrackedDownload trackedDownload, DownloadImportReceipt receipt)
        {
            lock (_completionGate)
            {
                return RecordImportCompletionCore(trackedDownload, receipt);
            }
        }

        public DownloadHistory RecordSourceRetention(TrackedDownload trackedDownload)
        {
            if (trackedDownload?.DownloadItem?.DownloadId.IsNullOrWhiteSpace() != false || trackedDownload.DownloadClient <= 0 ||
                (trackedDownload.DownloadItem.DownloadClientInfo?.Id is > 0 && trackedDownload.DownloadItem.DownloadClientInfo.Id != trackedDownload.DownloadClient))
            {
                throw new ArgumentException("A bound download is required to retain its sources");
            }

            var output = SourceOutputPath(trackedDownload);
            lock (_completionGate)
            {
                var existing = _repository.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                    .FirstOrDefault(history => history.EventType == DownloadHistoryEventType.DownloadSourceRetained &&
                        history.DownloadId == trackedDownload.DownloadItem.DownloadId && history.DownloadClientId == trackedDownload.DownloadClient &&
                        output.PathEquals(DownloadImportCompletionService.DataValue(history.Data, "outputPath")));
                if (existing != null)
                {
                    return existing;
                }

                var history = new DownloadHistory
                {
                    EventType = DownloadHistoryEventType.DownloadSourceRetained,
                    AuthorId = trackedDownload.RemoteBook?.Author?.Id ?? 0,
                    DownloadId = trackedDownload.DownloadItem.DownloadId,
                    SourceTitle = trackedDownload.DownloadItem.Title,
                    Date = DateTime.UtcNow,
                    Protocol = trackedDownload.Protocol,
                    DownloadClientId = trackedDownload.DownloadClient
                };
                history.Data.Add("OutputPath", output);

                // A Copy choice must survive a crash or incomplete import, so
                // persist it before importing any selected file.
                return _repository.Insert(history);
            }
        }

        public bool KeepSources(TrackedDownload trackedDownload)
        {
            if (trackedDownload?.DownloadItem?.DownloadId.IsNullOrWhiteSpace() != false || trackedDownload.DownloadClient <= 0)
            {
                return false;
            }

            var retained = _repository.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                .Where(history => history.EventType == DownloadHistoryEventType.DownloadSourceRetained &&
                    history.DownloadId == trackedDownload.DownloadItem.DownloadId && history.DownloadClientId == trackedDownload.DownloadClient);
            foreach (var history in retained)
            {
                try
                {
                    var output = DownloadImportCompletionService.DataValue(history.Data, "outputPath");
                    if (output.IsNullOrWhiteSpace() || !Path.IsPathFullyQualified(output) || SourceOutputPath(trackedDownload).PathEquals(output))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // An own malformed retention record cannot grant deletion.
                    return true;
                }
            }

            return false;
        }

        private static string SourceOutputPath(TrackedDownload trackedDownload)
        {
            var output = trackedDownload.ImportItem?.OutputPath.FullPath;
            if (output.IsNullOrWhiteSpace())
            {
                output = trackedDownload.DownloadItem.OutputPath.FullPath;
            }

            if (output.IsNullOrWhiteSpace() || !Path.IsPathFullyQualified(output))
            {
                throw new ArgumentException("An absolute local output path is required to retain download sources");
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        }

        private DownloadHistory RecordImportCompletionCore(TrackedDownload trackedDownload, DownloadImportReceipt receipt)
        {
            if (receipt == null || receipt.Version != 1 || receipt.AuthorId <= 0 || receipt.Files?.Count is not > 0 || receipt.Files.Count > 10000 ||
                receipt.BookIds?.Count is not > 0 || receipt.BookIds.Count > 4096 || receipt.BookIds.Any(id => id <= 0) ||
                trackedDownload?.DownloadItem?.DownloadId.IsNullOrWhiteSpace() != false || trackedDownload.DownloadClient <= 0 ||
                receipt.DownloadId != trackedDownload.DownloadItem.DownloadId || receipt.DownloadClientId != trackedDownload.DownloadClient)
            {
                throw new ArgumentException("A verified download completion receipt within supported limits is required");
            }

            var json = STJson.ToJson(receipt);
            if (json.Length > MaximumReceiptLength)
            {
                throw new ArgumentException("Download completion receipt exceeds the supported size");
            }

            var previous = GetLatestDownloadHistoryItem(receipt.DownloadId, receipt.DownloadClientId);
            if (previous?.EventType == DownloadHistoryEventType.DownloadImported && previous.DownloadClientId == receipt.DownloadClientId &&
                DownloadImportCompletionService.DataValue(previous.Data, ImportReceiptKey) == json)
            {
                return previous;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadImported,
                AuthorId = receipt.AuthorId,
                DownloadId = receipt.DownloadId,
                SourceTitle = trackedDownload.DownloadItem.Title,
                Date = DateTime.UtcNow,
                Protocol = trackedDownload.Protocol,
                DownloadClientId = receipt.DownloadClientId
            };
            history.Data.Add(ImportReceiptKey, json);
            history.Data.Add("DownloadClient", trackedDownload.DownloadItem.DownloadClientInfo?.Type);
            history.Data.Add("DownloadClientName", trackedDownload.DownloadItem.DownloadClientInfo?.Name);

            // This call is intentionally synchronous and is not routed through
            // the event aggregator, which suppresses subscriber exceptions.
            return _repository.Insert(history);
        }

        public void Handle(BookGrabbedEvent message)
        {
            // Don't store grabbed events for clients that don't download IDs
            if (message.DownloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadGrabbed,
                AuthorId = message.Book.Author.Id,
                DownloadId = message.DownloadId,
                SourceTitle = message.Book.Release.Title,
                Date = DateTime.UtcNow,
                Protocol = message.Book.Release.DownloadProtocol,
                IndexerId = message.Book.Release.IndexerId,
                DownloadClientId = message.DownloadClientId,
                Release =  message.Book.Release
            };

            history.Data.Add("Indexer", message.Book.Release.Indexer);
            history.Data.Add("DownloadClient", message.DownloadClient);
            history.Data.Add("DownloadClientName", message.DownloadClientName);
            history.Data.Add("CustomFormatScore", message.Book.CustomFormatScore.ToString());
            history.Data.Add("BookIds", STJson.ToJson(message.Book.Books.Select(book => book.Id).Where(id => id > 0).Distinct().OrderBy(id => id).ToList()));

            _repository.Insert(history);
        }

        public void Handle(TrackImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var downloadId = message.DownloadId;

            // Try to find the downloadId if the user used manual import (from wanted: missing) or the
            // API to import and downloadId wasn't provided.
            if (downloadId.IsNullOrWhiteSpace())
            {
                downloadId = _historyService.FindDownloadId(message);
            }

            if (downloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.FileImported,

                AuthorId = message.ImportedBook.Author.Value.Id,
                DownloadId = downloadId,
                SourceTitle = message.BookInfo.Path,
                Date = DateTime.UtcNow,
                Protocol = message.DownloadClientInfo.Protocol,
                DownloadClientId = message.DownloadClientInfo.Id
            };

            history.Data.Add("DownloadClient", message.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo.Name);
            history.Data.Add("SourcePath", message.BookInfo.Path);
            history.Data.Add("DestinationPath", message.ImportedBook.Path);

            _repository.Insert(history);
        }

        public void Handle(BookImportIncompleteEvent message)
        {
            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadImportIncomplete,
                AuthorId = message.TrackedDownload.RemoteBook?.Author?.Id ?? 0,
                DownloadId = message.TrackedDownload.DownloadItem.DownloadId,
                SourceTitle = message.TrackedDownload.DownloadItem.OutputPath.ToString(),
                Date = DateTime.UtcNow,
                Protocol = message.TrackedDownload.Protocol,
                DownloadClientId = message.TrackedDownload.DownloadClient
            };

            history.Data.Add("DownloadClient", message.TrackedDownload.DownloadItem.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.TrackedDownload.DownloadItem.DownloadClientInfo.Name);
            history.Data.Add("StatusMessages", message.TrackedDownload.StatusMessages.ToJson());

            _repository.Insert(history);
        }

        public void Handle(DownloadCompletedEvent message)
        {
            // Verified completion is already persisted by RecordImportCompletion
            // before state/event publication. Legacy events cannot create an
            // unverified marker, and replaying a notification adds no duplicate.
        }

        public void Handle(DownloadFailedEvent message)
        {
            // Don't track failed download for an unknown download
            if (message.TrackedDownload == null)
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadFailed,
                AuthorId = message.AuthorId,
                DownloadId = message.DownloadId,
                SourceTitle = message.SourceTitle,
                Date = DateTime.UtcNow,
                Protocol = message.TrackedDownload.Protocol,
                DownloadClientId = message.TrackedDownload.DownloadClient
            };

            history.Data.Add("DownloadClient", message.TrackedDownload.DownloadItem.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.TrackedDownload.DownloadItem.DownloadClientInfo.Name);

            _repository.Insert(history);
        }

        public void Handle(DownloadIgnoredEvent message)
        {
            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadIgnored,
                AuthorId = message.AuthorId,
                DownloadId = message.DownloadId,
                SourceTitle = message.SourceTitle,
                Date = DateTime.UtcNow,
                Protocol = message.DownloadClientInfo.Protocol,
                DownloadClientId = message.DownloadClientInfo.Id
            };

            history.Data.Add("DownloadClient", message.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo.Name);

            _repository.Insert(history);
        }

        public void Handle(AuthorDeletedEvent message)
        {
            _repository.DeleteByAuthorId(message.Author.Id);
        }
    }
}
