using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IBuildAuthorPaths
    {
        string BuildPath(Author author, bool useExistingRelativeFolder);
    }

    public class AuthorPathBuilder : IBuildAuthorPaths
    {
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly IAuthorRepository _authorRepository;
        private readonly Logger _logger;

        public AuthorPathBuilder(IBuildFileNames fileNameBuilder,
                                 IRootFolderService rootFolderService,
                                 IDiskProvider diskProvider,
                                 IAuthorRepository authorRepository,
                                 Logger logger)
        {
            _fileNameBuilder = fileNameBuilder;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _authorRepository = authorRepository;
            _logger = logger;
        }

        public string BuildPath(Author author, bool useExistingRelativeFolder)
        {
            if (author.RootFolderPath.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("Root folder was not provided", nameof(author));
            }

            if (useExistingRelativeFolder && author.Path.IsNotNullOrWhiteSpace())
            {
                var relativePath = GetExistingRelativePath(author);
                return Path.Combine(author.RootFolderPath, relativePath);
            }

            var generatedPath = Path.Combine(author.RootFolderPath, _fileNameBuilder.GetAuthorFolder(author));
            return FindExistingFolder(author, generatedPath) ?? generatedPath;
        }

        private string FindExistingFolder(Author author, string generatedPath)
        {
            var parentPath = Path.GetDirectoryName(generatedPath);

            try
            {
                if (_diskProvider.FolderExists(generatedPath))
                {
                    return generatedPath;
                }

                if (!_diskProvider.FolderExists(parentPath))
                {
                    return null;
                }

                var folderName = NormalizeFolderName(Path.GetFileName(generatedPath));
                var names = new HashSet<string>(StringComparer.Ordinal) { folderName };
                var authorNames = new[] { author.Name }.Concat(author.Metadata.Value.Aliases ?? new List<string>())
                    .Select(NormalizeFolderName)
                    .Where(x => x.Length > 0)
                    .ToList();

                // Respect naming formats with extra tokens or a different name order.
                // Stored aliases apply when the final folder component is an author name.
                if (authorNames.Contains(folderName))
                {
                    names.UnionWith(authorNames);
                }

                names.Remove(string.Empty);

                var matches = _diskProvider.GetDirectories(parentPath)
                    .Where(x => names.Contains(NormalizeFolderName(Path.GetFileName(x))))
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .ToList();

                if (matches.Count == 1)
                {
                    var claimedPaths = _authorRepository.AllAuthorPaths();
                    if (claimedPaths.Any(x => x.Key != author.Id && x.Value.PathEquals(matches[0])))
                    {
                        _logger.Warn("Existing folder '{0}' is assigned to another author; keeping the generated path '{1}' for {2}.", matches[0], generatedPath, author.Name);
                        return null;
                    }

                    return matches[0];
                }

                if (matches.Count > 1)
                {
                    _logger.Warn("Multiple existing folders match {0}; keeping the generated path '{1}'. Select the intended folder explicitly.", author.Name, generatedPath);
                }
            }
            catch (IOException ex)
            {
                _logger.Warn(ex, "Unable to check existing folders for {0}; keeping the generated path '{1}'.", author.Name, generatedPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warn(ex, "Unable to check existing folders for {0}; keeping the generated path '{1}'.", author.Name, generatedPath);
            }

            return null;
        }

        private static string NormalizeFolderName(string value)
        {
            return new string((value ?? string.Empty).Normalize(NormalizationForm.FormKC)
                .Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private string GetExistingRelativePath(Author author)
        {
            var rootFolderPath = _rootFolderService.GetBestRootFolderPath(author.Path);

            return rootFolderPath.GetRelativePath(author.Path);
        }
    }
}
