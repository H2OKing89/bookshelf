using System.Collections.Generic;
using System.Net.Sockets;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Notifications.Subsonic
{
    public class Subsonic : NotificationBase<SubsonicSettings>, IBatchedMediaServerNotification
    {
        private readonly ISubsonicService _subsonicService;
        private readonly Logger _logger;
        private readonly MediaServerUpdateQueue<Subsonic> _pendingUpdates;

        public Subsonic(ISubsonicService subsonicService, ICacheManager cacheManager, Logger logger)
        {
            _subsonicService = subsonicService;
            _logger = logger;
            _pendingUpdates = new MediaServerUpdateQueue<Subsonic>(cacheManager);
        }

        public override string Link => "http://subsonic.org/";

        public override void OnGrab(GrabMessage grabMessage)
        {
            const string header = "Readarr - Grabbed";

            Notify(Settings, header, grabMessage.Message);
        }

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            QueueImportBatch(new[] { message }, true);
            ProcessPendingUpdates();
        }

        public bool QueueImportBatch(IReadOnlyList<BookDownloadMessage> messages, bool sendNotifications, bool refreshForRemovedFiles = false)
        {
            if (messages.Count == 0 && !refreshForRemovedFiles)
            {
                return false;
            }

            QueueUpdate();

            // Subsonic has a server-wide scan, but its optional chat message
            // is still one notification per imported book for compatibility.
            var notified = false;
            if (sendNotifications)
            {
                foreach (var message in messages)
                {
                    notified |= Notify(Settings, "Readarr - Downloaded", message.Message);
                }
            }

            return notified;
        }

        public bool ProcessPendingUpdates()
        {
            return Settings.UpdateLibrary && _pendingUpdates.Process(Definition, _ => _subsonicService.Update(Settings));
        }

        public override void ProcessQueue()
        {
            ProcessPendingUpdates();
        }

        public override void OnRename(Author author, List<RenamedBookFile> renamedFiles)
        {
            Update();
        }

        public override void OnAuthorAdded(Author author)
        {
            Notify(Settings, AUTHOR_ADDED_TITLE_BRANDED, author.Name);
        }

        public override void OnAuthorDelete(AuthorDeleteMessage deleteMessage)
        {
            const string header = "Readarr - Author Deleted";

            Notify(Settings, header, deleteMessage.Message);

            if (deleteMessage.DeletedFiles)
            {
                Update();
            }
        }

        public override void OnBookDelete(BookDeleteMessage deleteMessage)
        {
            const string header = "Readarr - Book Deleted";

            Notify(Settings, header, deleteMessage.Message);

            if (deleteMessage.DeletedFiles)
            {
                Update();
            }
        }

        public override void OnBookFileDelete(BookFileDeleteMessage deleteMessage)
        {
            const string header = "Readarr - Book File Deleted";

            Notify(Settings, header, deleteMessage.Message);
            if (!deleteMessage.IsPartOfImportBatch)
            {
                Update();
            }
        }

        public override void OnHealthIssue(HealthCheck.HealthCheck healthCheck)
        {
            Notify(Settings, HEALTH_ISSUE_TITLE_BRANDED, healthCheck.Message);
        }

        public override void OnHealthRestored(HealthCheck.HealthCheck previousCheck)
        {
            Notify(Settings, HEALTH_RESTORED_TITLE_BRANDED, previousCheck.Message);
        }

        public override void OnBookRetag(BookRetagMessage message)
        {
            Notify(Settings, BOOK_RETAGGED_TITLE_BRANDED, message.Message);
        }

        public override string Name => "Subsonic";

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_subsonicService.Test(Settings, "Success! Subsonic has been successfully configured!"));

            return new ValidationResult(failures);
        }

        private bool Notify(SubsonicSettings settings, string header, string message)
        {
            try
            {
                if (Settings.Notify)
                {
                    _subsonicService.Notify(Settings, $"{header} - {message}");
                    return true;
                }
            }
            catch (SocketException ex)
            {
                var logMessage = $"Unable to connect to Subsonic Host: {Settings.Host}:{Settings.Port}";
                _logger.Debug(ex, logMessage);
            }

            return false;
        }

        private void QueueUpdate()
        {
            if (Settings.UpdateLibrary)
            {
                _pendingUpdates.Add(Definition, new[] { "library" });
            }
        }

        private void Update()
        {
            QueueUpdate();
            ProcessPendingUpdates();
        }
    }
}
