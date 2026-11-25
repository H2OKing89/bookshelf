using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    public interface IPlexServerService
    {
        void UpdateLibrary(Author author, PlexServerSettings settings);
        void UpdateLibrary(IEnumerable<Author> authors, PlexServerSettings settings);
        void UpdateLibraryPaths(IEnumerable<string> paths, PlexServerSettings settings);
        ValidationFailure Test(PlexServerSettings settings);
    }

    public class PlexServerService : IPlexServerService
    {
        private readonly ICached<Version> _versionCache;
        private readonly IPlexServerProxy _plexServerProxy;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public PlexServerService(ICacheManager cacheManager, IPlexServerProxy plexServerProxy, IRootFolderService rootFolderService, Logger logger)
        {
            _versionCache = cacheManager.GetCache<Version>(GetType(), "versionCache");
            _plexServerProxy = plexServerProxy;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public void UpdateLibrary(Author author, PlexServerSettings settings)
        {
            UpdateLibrary(new[] { author }, settings);
        }

        public void UpdateLibrary(IEnumerable<Author> authors, PlexServerSettings settings)
        {
            UpdateLibraryPaths(authors.Select(author => author.Path), settings);
        }

        public void UpdateLibraryPaths(IEnumerable<string> paths, PlexServerSettings settings)
        {
            try
            {
                _logger.Debug("Sending Update Request to Plex Server");
                var watch = Stopwatch.StartNew();

                var version = _versionCache.Get(EndpointKey(settings), () => GetVersion(settings), TimeSpan.FromHours(2));
                ValidateVersion(version);

                var sections = GetSections(settings);

                var requested = new HashSet<(int SectionId, string Path)>();
                foreach (var path in paths)
                {
                    UpdateSections(path, sections, settings, requested);
                }

                foreach (var (sectionId, path) in requested)
                {
                    _plexServerProxy.Update(sectionId, path, settings);
                }

                _logger.Debug("Finished sending Update Request to Plex Server (took {0} ms)", watch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to Update Plex host: " + settings.Host);
                throw;
            }
        }

        private static string EndpointKey(PlexServerSettings settings) => $"{settings.Host}:{settings.Port}:{settings.UseSsl}:{settings.UrlBase}";

        private List<PlexSection> GetSections(PlexServerSettings settings)
        {
            _logger.Debug("Getting sections from Plex host: {0}", settings.Host);

            return _plexServerProxy.GetTvSections(settings).ToList();
        }

        private void ValidateVersion(Version version)
        {
            if (version >= new Version(1, 3, 0) && version < new Version(1, 3, 1))
            {
                throw new PlexVersionException("Found version {0}, upgrade to PMS 1.3.1 to fix library updating and then restart Readarr", version);
            }
        }

        private Version GetVersion(PlexServerSettings settings)
        {
            _logger.Debug("Getting version from Plex host: {0}", settings.Host);

            var rawVersion = _plexServerProxy.Version(settings);
            var version = new Version(Regex.Match(rawVersion, @"^(\d+[.-]){4}").Value.Trim('.', '-'));

            return version;
        }

        private void UpdateSections(string path, List<PlexSection> sections, PlexServerSettings settings, HashSet<(int SectionId, string Path)> requested)
        {
            var rootFolderPath = _rootFolderService.GetBestRootFolderPath(path);
            var relativePath = rootFolderPath.PathEquals(path) ? string.Empty : rootFolderPath.GetRelativePath(path);

            // Try to update a matching section location before falling back to updating all section locations.
            foreach (var section in sections)
            {
                foreach (var location in section.Locations)
                {
                    var rootFolder = new OsPath(rootFolderPath);
                    var mappedPath = rootFolder;

                    if (settings.MapTo.IsNotNullOrWhiteSpace())
                    {
                        mappedPath = new OsPath(settings.MapTo) + (rootFolder - new OsPath(settings.MapFrom));

                        _logger.Trace("Mapping Path from {0} to {1} for partial scan", rootFolder, mappedPath);
                    }

                    if (location.Path.PathEquals(mappedPath.FullPath))
                    {
                        _logger.Debug("Updating matching section location, {0}", location.Path);
                        AddSectionPath(relativePath, section, location, requested);

                        return;
                    }
                }
            }

            _logger.Debug("Unable to find matching section location, updating all Music sections");

            foreach (var section in sections)
            {
                foreach (var location in section.Locations)
                {
                    AddSectionPath(relativePath, section, location, requested);
                }
            }
        }

        private void AddSectionPath(string relativePath, PlexSection section, PlexSectionLocation location, HashSet<(int SectionId, string Path)> requested)
        {
            var separator = location.Path.Contains('\\') ? "\\" : "/";
            var locationRelativePath = relativePath.Replace("\\", separator).Replace("/", separator);

            // Plex location paths trim trailing extraneous separator characters, so it doesn't need to be trimmed
            var pathToUpdate = locationRelativePath.Length == 0 ? location.Path : $"{location.Path}{separator}{locationRelativePath}";

            _logger.Debug("Updating section location, {0}", location.Path);
            requested.Add((section.Id, pathToUpdate));
        }

        public ValidationFailure Test(PlexServerSettings settings)
        {
            try
            {
                _versionCache.Remove(EndpointKey(settings));
                var sections = GetSections(settings);

                if (sections.Empty())
                {
                    return new ValidationFailure("Host", "At least one Music library is required");
                }
            }
            catch (PlexAuthenticationException ex)
            {
                _logger.Error(ex, "Unable to connect to Plex Media Server");
                return new ValidationFailure("AuthToken", "Invalid authentication token");
            }
            catch (PlexException ex)
            {
                return new NzbDroneValidationFailure("Host", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to connect to Plex Media Server");

                return new NzbDroneValidationFailure("Host", "Unable to connect to Plex Media Server")
                       {
                           DetailedDescription = ex.Message
                       };
            }

            return null;
        }
    }
}
