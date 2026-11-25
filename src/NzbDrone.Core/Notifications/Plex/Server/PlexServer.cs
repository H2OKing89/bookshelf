using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications.Plex.PlexTv;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    public class PlexServer : NotificationBase<PlexServerSettings>, IBatchedMediaServerNotification
    {
        private readonly IPlexServerService _plexServerService;
        private readonly IPlexTvService _plexTvService;
        private readonly Logger _logger;

        private readonly MediaServerUpdateQueue<PlexServer> _pendingUpdates;

        public PlexServer(IPlexServerService plexServerService, IPlexTvService plexTvService, ICacheManager cacheManager, Logger logger)
        {
            _plexServerService = plexServerService;
            _plexTvService = plexTvService;
            _logger = logger;

            _pendingUpdates = new MediaServerUpdateQueue<PlexServer>(cacheManager, PathEqualityComparer.Instance);
        }

        public override string Link => "https://www.plex.tv/";
        public override string Name => "Plex Media Server";

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            QueueImportBatch(new[] { message }, true);
            ProcessPendingUpdates();
        }

        public bool QueueImportBatch(IReadOnlyList<BookDownloadMessage> messages, bool sendNotifications, bool refreshForRemovedFiles = false)
        {
            if (Settings.UpdateLibrary)
            {
                // Use the paths of files actually committed. Collapse all paths
                // below the configured author directory to that single targeted scan;
                // files stored elsewhere retain their own directory as a target.
                var targets = messages.SelectMany(message => message.BookFiles)
                    .Where(file => file?.Path.IsNotNullOrWhiteSpace() == true)
                    .Select(file => Path.GetDirectoryName(file.Path))
                    .Where(path => path.IsNotNullOrWhiteSpace()).ToList();
                var authors = messages.Select(message => message.Author?.Path)
                    .Where(path => path.IsNotNullOrWhiteSpace()).Distinct(PathEqualityComparer.Instance).ToList();
                var refreshPaths = targets.Select(path => authors.FirstOrDefault(author => author.PathEquals(path) || author.IsParentPath(path)) ?? path);
                _pendingUpdates.Add(Definition, refreshPaths);
            }

            // Plex does not have a separate per-book notification operation.
            return false;
        }

        public override void OnRename(Author author, List<RenamedBookFile> renamedFiles)
        {
            UpdateIfEnabled(author);
            ProcessPendingUpdates();
        }

        public override void OnBookRetag(BookRetagMessage message)
        {
            UpdateIfEnabled(message.Author);
            ProcessPendingUpdates();
        }

        public override void OnBookDelete(BookDeleteMessage deleteMessage)
        {
            if (deleteMessage.DeletedFiles)
            {
                UpdateIfEnabled(deleteMessage.Book.Author);
            }
        }

        public override void OnAuthorDelete(AuthorDeleteMessage deleteMessage)
        {
            if (deleteMessage.DeletedFiles)
            {
                UpdateIfEnabled(deleteMessage.Author);
            }
        }

        private void UpdateIfEnabled(Author author)
        {
            if (Settings.UpdateLibrary && author?.Path.IsNotNullOrWhiteSpace() == true)
            {
                _logger.Debug("Scheduling library update for author {0} {1}", author.Id, author.Name);
                _pendingUpdates.Add(Definition, new[] { author.Path });
            }
        }

        public bool ProcessPendingUpdates()
        {
            return Settings.UpdateLibrary && _pendingUpdates.Process(Definition, paths =>
            {
                _plexTvService.Ping(Settings.AuthToken);
                _plexServerService.UpdateLibraryPaths(paths, Settings);
            });
        }

        public override void ProcessQueue()
        {
            ProcessPendingUpdates();
        }

        public override ValidationResult Test()
        {
            _plexTvService.Ping(Settings.AuthToken);

            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_plexServerService.Test(Settings));

            return new ValidationResult(failures);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                return _plexTvService.GetPinUrl();
            }
            else if (action == "continueOAuth")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                if (query["callbackUrl"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam callbackUrl invalid.");
                }

                if (query["id"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam id invalid.");
                }

                if (query["code"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam code invalid.");
                }

                return _plexTvService.GetSignInUrl(query["callbackUrl"], Convert.ToInt32(query["id"]), query["code"]);
            }
            else if (action == "getOAuthToken")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                if (query["pinId"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam pinId invalid.");
                }

                var authToken = _plexTvService.GetAuthToken(Convert.ToInt32(query["pinId"]));

                return new
                       {
                           authToken
                       };
            }

            return new { };
        }
    }
}
