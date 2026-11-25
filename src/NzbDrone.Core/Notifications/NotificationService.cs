using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Update.History.Events;

namespace NzbDrone.Core.Notifications
{
    public class NotificationService
        : IHandle<BookGrabbedEvent>,
          IHandle<BookImportedEvent>,
          IHandle<AuthorRenamedEvent>,
          IHandle<AuthorAddedEvent>,
          IHandle<AuthorDeletedEvent>,
          IHandle<BookDeletedEvent>,
          IHandle<BookFileDeletedEvent>,
          IHandle<HealthCheckFailedEvent>,
          IHandle<HealthCheckRestoredEvent>,
          IHandle<DownloadFailedEvent>,
          IHandle<ManualInteractionRequiredEvent>,
          IHandle<BookFileRetaggedEvent>,
          IHandleAsync<DeleteCompletedEvent>,
          IHandleAsync<BookImportBatchCompletedEvent>,
          IHandleAsync<CommandExecutedEvent>,
          IHandle<UpdateInstalledEvent>
    {
        private readonly INotificationFactory _notificationFactory;
        private readonly INotificationStatusService _notificationStatusService;
        private readonly Logger _logger;

        public NotificationService(INotificationFactory notificationFactory, INotificationStatusService notificationStatusService, Logger logger)
        {
            _notificationFactory = notificationFactory;
            _notificationStatusService = notificationStatusService;
            _logger = logger;
        }

        private string GetMessage(Author author, List<Book> books, QualityModel quality)
        {
            var qualityString = quality.Quality.ToString();

            if (quality.Revision.Version > 1)
            {
                qualityString += " Proper";
            }

            var bookTitles = string.Join(" + ", books.Select(e => e.Title));

            return string.Format("{0} - {1} - [{2}]",
                                    author.Name,
                                    bookTitles,
                                    qualityString);
        }

        private string GetBookDownloadMessage(Author author, Book book, List<BookFile> tracks)
        {
            return string.Format("{0} - {1} ({2} Files Imported)",
                author.Name,
                book.Title,
                tracks.Count);
        }

        private string FormatMissing(object value)
        {
            var text = value?.ToString();
            return text.IsNullOrWhiteSpace() ? "<missing>" : text;
        }

        private string GetTrackRetagMessage(Author author, BookFile bookFile, Dictionary<string, Tuple<string, string>> diff)
        {
            return string.Format("{0}:\n{1}",
                                 bookFile.Path,
                                 string.Join("\n", diff.Select(x => $"{x.Key}: {FormatMissing(x.Value.Item1)} → {FormatMissing(x.Value.Item2)}")));
        }

        private bool ShouldHandleAuthor(ProviderDefinition definition, Author author)
        {
            if (definition.Tags.Empty())
            {
                _logger.Debug("No tags set for this notification.");
                return true;
            }

            if (author == null)
            {
                _logger.Debug("Skipping tagged notification because the download has no matched author.");
                return false;
            }

            if (definition.Tags.Intersect(author.Tags).Any())
            {
                _logger.Debug("Notification and author have one or more intersecting tags.");
                return true;
            }

            //TODO: this message could be more clear
            _logger.Debug("{0} does not have any intersecting tags with {1}. Notification will not be sent.", definition.Name, author.Name);
            return false;
        }

        private bool ShouldHandleHealthFailure(HealthCheck.HealthCheck healthCheck, bool includeWarnings)
        {
            if (healthCheck.Type == HealthCheckResult.Error)
            {
                return true;
            }

            if (healthCheck.Type == HealthCheckResult.Warning && includeWarnings)
            {
                return true;
            }

            return false;
        }

        public void Handle(BookGrabbedEvent message)
        {
            var grabMessage = new GrabMessage
            {
                Message = GetMessage(message.Book.Author, message.Book.Books, message.Book.ParsedBookInfo.Quality),
                Author = message.Book.Author,
                Quality = message.Book.ParsedBookInfo.Quality,
                RemoteBook = message.Book,
                DownloadClientName = message.DownloadClientName,
                DownloadClientType = message.DownloadClient,
                DownloadId = message.DownloadId
            };

            foreach (var notification in _notificationFactory.OnGrabEnabled())
            {
                try
                {
                    if (!ShouldHandleAuthor(notification.Definition, message.Book.Author))
                    {
                        continue;
                    }

                    notification.OnGrab(grabMessage);
                    _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Error(ex, "Unable to send OnGrab notification to {0}", notification.Definition.Name);
                }
            }
        }

        public void Handle(BookImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var downloadMessage = ToDownloadMessage(message);

            foreach (var notification in _notificationFactory.OnReleaseImportEnabled())
            {
                try
                {
                    // Media-server providers receive the entire committed import batch below.
                    if (message.IsPartOfBatch && notification is IBatchedMediaServerNotification)
                    {
                        continue;
                    }

                    if (ShouldHandleAuthor(notification.Definition, message.Author))
                    {
                        if (downloadMessage.OldFiles.Empty() || ((NotificationDefinition)notification.Definition).OnUpgrade)
                        {
                            notification.OnReleaseImport(downloadMessage);
                            _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnReleaseImport notification to: " + notification.Definition.Name);
                }
            }
        }

        private BookDownloadMessage ToDownloadMessage(BookImportedEvent message)
        {
            return new BookDownloadMessage
            {
                Message = GetBookDownloadMessage(message.Author, message.Book, message.ImportedBooks),
                Author = message.Author,
                Book = message.Book,
                DownloadClientInfo = message.DownloadClientInfo,
                DownloadId = message.DownloadId,
                BookFiles = message.ImportedBooks,
                OldFiles = message.OldFiles,
            };
        }

        public void HandleAsync(BookImportBatchCompletedEvent message)
        {
            var downloads = message.Imports.Where(import => import.NewDownload && import.ImportedBooks.Any()).Select(ToDownloadMessage).ToList();
            if (downloads.Count == 0 && message.ReplacedFiles.Count == 0)
            {
                return;
            }

            var blocked = _notificationStatusService.GetBlockedProviders().Select(status => status.ProviderId).ToHashSet();

            // Do not drop committed refresh work when a connection is temporarily blocked.
            // Capture its complete batch and let ProcessQueue honor provider backoff.
            foreach (var notification in _notificationFactory.GetAvailableProviders())
            {
                if (notification is not IBatchedMediaServerNotification mediaServer)
                {
                    continue;
                }

                try
                {
                    var definition = (NotificationDefinition)notification.Definition;
                    var eligible = downloads.Where(download => definition.OnReleaseImport && ShouldHandleAuthor(definition, download.Author) &&
                        (download.OldFiles.Empty() || definition.OnUpgrade)).ToList();

                    // Honor the separately configured old-file deletion trigger
                    // without scanning for each replaced part during an import.
                    var refreshForRemovedFiles = notification.SupportsOnBookFileDeleteForUpgrade && definition.OnBookFileDelete &&
                        definition.OnBookFileDeleteForUpgrade && message.ReplacedFiles.Any(file => file.EditionId > 0 && ShouldHandleAuthor(definition, file.Author));
                    if (eligible.Count == 0 && !refreshForRemovedFiles)
                    {
                        continue;
                    }

                    if (mediaServer.QueueImportBatch(eligible, !blocked.Contains(notification.Definition.Id), refreshForRemovedFiles))
                    {
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    RecordIsolatedFailure(notification, ex, "queue media-server import notification");
                }
            }

            ProcessQueue();
        }

        public void Handle(AuthorRenamedEvent message)
        {
            foreach (var notification in _notificationFactory.OnRenameEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, message.Author))
                    {
                        notification.OnRename(message.Author, message.RenamedFiles);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnRename notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(AuthorAddedEvent message)
        {
            foreach (var notification in _notificationFactory.OnAuthorAddedEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, message.Author))
                    {
                        notification.OnAuthorAdded(message.Author);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnAuthorAdded notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(AuthorDeletedEvent message)
        {
            var deleteMessage = new AuthorDeleteMessage(message.Author, message.DeleteFiles);

            foreach (var notification in _notificationFactory.OnAuthorDeleteEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, deleteMessage.Author))
                    {
                        notification.OnAuthorDelete(deleteMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnAuthorDelete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(BookDeletedEvent message)
        {
            var deleteMessage = new BookDeleteMessage(message.Book, message.DeleteFiles);

            foreach (var notification in _notificationFactory.OnBookDeleteEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, deleteMessage.Book.Author))
                    {
                        notification.OnBookDelete(deleteMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnBookDelete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(BookFileDeletedEvent message)
        {
            var deleteMessage = new BookFileDeleteMessage();

            var book = new List<Book> { message.BookFile.Edition.Value.Book };

            deleteMessage.Message = GetMessage(message.BookFile.Author, book, message.BookFile.Quality);
            deleteMessage.BookFile = message.BookFile;
            deleteMessage.Book = message.BookFile.Edition.Value.Book;
            deleteMessage.Reason = message.Reason;
            deleteMessage.IsPartOfImportBatch = message.IsPartOfImportBatch;

            foreach (var notification in _notificationFactory.OnBookFileDeleteEnabled())
            {
                try
                {
                    if (message.Reason != MediaFiles.DeleteMediaFileReason.Upgrade || ((NotificationDefinition)notification.Definition).OnBookFileDeleteForUpgrade)
                    {
                        if (ShouldHandleAuthor(notification.Definition, message.BookFile.Author))
                        {
                            notification.OnBookFileDelete(deleteMessage);
                            _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnBookFileDelete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(HealthCheckFailedEvent message)
        {
            // Don't send health check notifications during the start up grace period,
            // once that duration expires they they'll be retested and fired off if necessary.
            if (message.IsInStartupGracePeriod)
            {
                return;
            }

            foreach (var notification in _notificationFactory.OnHealthIssueEnabled())
            {
                try
                {
                    if (ShouldHandleHealthFailure(message.HealthCheck, ((NotificationDefinition)notification.Definition).IncludeHealthWarnings))
                    {
                        notification.OnHealthIssue(message.HealthCheck);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnHealthIssue notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(HealthCheckRestoredEvent message)
        {
            if (message.IsInStartupGracePeriod)
            {
                return;
            }

            // Health checks are system-wide, not attributable to an author tag.
            // Use the prior severity so a warning recovery respects IncludeHealthWarnings.
            foreach (var notification in _notificationFactory.OnHealthRestoredEnabled())
            {
                try
                {
                    if (ShouldHandleHealthFailure(message.PreviousCheck, ((NotificationDefinition)notification.Definition).IncludeHealthWarnings))
                    {
                        notification.OnHealthRestored(message.PreviousCheck);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    RecordIsolatedFailure(notification, ex, "send OnHealthRestored notification");
                }
            }
        }

        public void Handle(DownloadFailedEvent message)
        {
            var downloadFailedMessage = new DownloadFailedMessage
            {
                DownloadId = message.DownloadId,
                DownloadClient = message.DownloadClient,
                Quality = message.Quality,
                SourceTitle = message.SourceTitle,
                Message = message.Message
            };

            foreach (var notification in _notificationFactory.OnDownloadFailureEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, message.TrackedDownload.RemoteBook.Author))
                    {
                        notification.OnDownloadFailure(downloadFailedMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnDownloadFailure notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(ManualInteractionRequiredEvent message)
        {
            var tracked = message.TrackedDownload;
            var books = tracked.RemoteBook?.Books ?? new List<Book>();
            var statuses = tracked.StatusMessages.Where(status => status != null).Select(status => new TrackedDownloadStatusMessage(status.Title,
                status.Messages?.ToList() ?? new List<string>(),
                status.Rejections.Where(reason => reason != null).Select(reason => new Rejection(reason.Reason, reason.Type, reason.Code)))).ToList();
            var client = tracked.DownloadItem.DownloadClientInfo;
            var source = tracked.DownloadItem.Title.IsNullOrWhiteSpace() ? tracked.DownloadItem.DownloadId : tracked.DownloadItem.Title;
            var reasons = statuses.SelectMany(status => status.Rejections.Select(reason => $"[{reason.Code}] {status.Title}: {reason.Reason}"))
                .Distinct().ToList();
            var text = $"Bookshelf needs manual intervention to import {source}.";
            if (client != null)
            {
                text += $" Download client: {client.Name} ({client.Type}).";
            }

            var sourcePath = tracked.ImportItem?.OutputPath.FullPath ?? tracked.DownloadItem.OutputPath.FullPath;
            if (!sourcePath.IsNullOrWhiteSpace())
            {
                text += $" Source: {sourcePath}.";
            }

            if (reasons.Any())
            {
                text += " " + string.Join("; ", reasons.Take(10));
                if (reasons.Count > 10)
                {
                    text += $"; {reasons.Count - 10} additional issues are available in Activity.";
                }
            }

            var downloadMessage = new BookDownloadMessage
            {
                Message = text,
                Author = tracked.RemoteBook?.Author,
                Book = books.Count == 1 ? books[0] : null,
                Books = books,
                BookIdentities = books.Select(book => ManualInteractionIdentity(book, tracked)).ToList(),
                BookFiles = new List<BookFile>(),
                OldFiles = new List<BookFile>(),
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = tracked.DownloadClient, Name = client?.Name, Type = client?.Type, Protocol = tracked.Protocol
                },
                DownloadId = tracked.DownloadItem.DownloadId,
                SourceTitle = source,
                SourcePath = tracked.DownloadItem.OutputPath.FullPath,
                OutputPath = tracked.ImportItem?.OutputPath.FullPath,
                DownloadStatus = tracked.DownloadItem.Status.ToString(),
                ImportStatus = tracked.State.ToString(),
                StatusMessages = statuses
            };

            foreach (var notification in _notificationFactory.OnImportFailureEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, downloadMessage.Author))
                    {
                        notification.OnImportFailure(downloadMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        _notificationStatusService.RecordFailure(notification.Definition.Id);
                    }
                    catch (Exception statusError)
                    {
                        _logger.Debug("Unable to record notification failure ({0})", statusError.GetType().Name);
                    }

                    // Provider errors may contain endpoint credentials. Preserve
                    // isolation without copying those details into import logs.
                    _logger.Warn("Unable to send OnImportFailure notification to {0} ({1})", notification.Definition.Name, ex.GetType().Name);
                }
            }
        }

        private static ManualInteractionBook ManualInteractionIdentity(Book book, TrackedDownload tracked)
        {
            var committedEditions = tracked.ImportReceipt?.Files?.Where(file => file.BookId == book.Id)
                .Select(file => file.EditionId).ToHashSet() ?? new HashSet<int>();
            var editions = (book.Editions?.Value ?? new List<Edition>())
                .Where(edition => edition.BookId == book.Id &&
                    (committedEditions.Any() ? committedEditions.Contains(edition.Id) : edition.Monitored));
            return new ManualInteractionBook
            {
                Id = book.Id,
                Title = book.Title,
                ForeignBookId = book.ForeignBookId,
                Editions = editions.Select(edition => new ManualInteractionEdition
                {
                    Id = edition.Id,
                    Title = edition.Title,
                    ForeignEditionId = edition.ForeignEditionId,
                    Asin = edition.Asin,
                    AudibleRegion = edition.AudibleRegion
                }).ToList()
            };
        }

        public void Handle(BookFileRetaggedEvent message)
        {
            var retagMessage = new BookRetagMessage
            {
                Message = GetTrackRetagMessage(message.Author, message.BookFile, message.Diff),
                Author = message.Author,
                Book = message.BookFile.Edition.Value.Book.Value,
                BookFile = message.BookFile,
                Diff = message.Diff,
                Scrubbed = message.Scrubbed
            };

            foreach (var notification in _notificationFactory.OnBookRetagEnabled())
            {
                try
                {
                    if (ShouldHandleAuthor(notification.Definition, message.Author))
                    {
                        notification.OnBookRetag(retagMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnBookRetag notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(UpdateInstalledEvent message)
        {
            var updateMessage = new ApplicationUpdateMessage();
            updateMessage.Message = $"Readarr updated from {message.PreviousVerison.ToString()} to {message.NewVersion.ToString()}";
            updateMessage.PreviousVersion = message.PreviousVerison;
            updateMessage.NewVersion = message.NewVersion;

            foreach (var notification in _notificationFactory.OnApplicationUpdateEnabled())
            {
                try
                {
                    notification.OnApplicationUpdate(updateMessage);
                    _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnApplicationUpdate notification to: " + notification.Definition.Name);
                }
            }
        }

        private void RecordIsolatedFailure(INotification notification, Exception error, string action)
        {
            try
            {
                _notificationStatusService.RecordFailure(notification.Definition.Id);
            }
            catch (Exception statusError)
            {
                _logger.Debug("Unable to record notification failure ({0})", statusError.GetType().Name);
            }

            // Avoid logging provider exception details that may contain endpoint credentials.
            _logger.Warn("Unable to {0} for {1} ({2})", action, notification.Definition.Name, error.GetType().Name);
        }

        public void HandleAsync(DeleteCompletedEvent message)
        {
            ProcessQueue();
        }

        public void HandleAsync(CommandExecutedEvent message)
        {
            // The regular download-monitor command runs every minute, even if
            // there are no further downloads. Failed refreshes can retry there.
            if (message.Command?.Body is RefreshMonitoredDownloadsCommand)
            {
                ProcessQueue();
            }
        }

        private void ProcessQueue()
        {
            var blocked = _notificationStatusService.GetBlockedProviders().Select(status => status.ProviderId).ToHashSet();
            foreach (var notification in _notificationFactory.GetAvailableProviders())
            {
                try
                {
                    if (notification is IBatchedMediaServerNotification mediaServer)
                    {
                        if (!blocked.Contains(notification.Definition.Id) && mediaServer.ProcessPendingUpdates())
                        {
                            _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                    else
                    {
                        notification.ProcessQueue();
                    }
                }
                catch (Exception ex)
                {
                    if (notification is IBatchedMediaServerNotification)
                    {
                        RecordIsolatedFailure(notification, ex, "process media-server notification queue");
                    }
                    else
                    {
                        _logger.Warn(ex, "Unable to process notification queue for " + notification.Definition.Name);
                    }
                }
            }
        }
    }
}
