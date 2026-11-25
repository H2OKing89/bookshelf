using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface ILocalRecordingService
    {
        LocalRecording Preview(List<int> fileIds);
        Book Create(LocalRecording recording);
        LocalRecording Get(int bookId);
        Book Update(int bookId, LocalRecording recording);
    }

    public class LocalRecordingService : ILocalRecordingService
    {
        private readonly IMediaFileService _files;
        private readonly IAudioTagService _tags;
        private readonly IDiskProvider _disk;
        private readonly IAuthorService _authors;
        private readonly IBookService _books;
        private readonly ILocalRecordingRepository _repository;
        private readonly IEventAggregator _events;
        private readonly IRootFolderService _rootFolders;

        public LocalRecordingService(IMediaFileService files,
            IAudioTagService tags,
            IDiskProvider disk,
            IAuthorService authors,
            IBookService books,
            ILocalRecordingRepository repository,
            IEventAggregator events,
            IRootFolderService rootFolders)
        {
            _files = files;
            _tags = tags;
            _disk = disk;
            _authors = authors;
            _books = books;
            _repository = repository;
            _events = events;
            _rootFolders = rootFolders;
        }

        public LocalRecording Preview(List<int> fileIds)
        {
            return Describe(ReadFiles(fileIds));
        }

        public Book Create(LocalRecording recording)
        {
            LocalRecordingMetadata.Normalize(recording);
            if (recording.AuthorId < 0 || (recording.AuthorId == 0) != !string.IsNullOrWhiteSpace(recording.NewAuthorName))
            {
                throw Invalid("Choose an existing author or explicitly create a local author.");
            }

            var selected = ReadFiles(recording.FileIds);
            var tags = Describe(selected);
            RootFolder newAuthorRoot = null;
            var author = recording.AuthorId > 0 ? _authors.GetAuthor(recording.AuthorId) : BuildLocalAuthor(recording, selected, out newAuthorRoot);
            var id = LocalRecordingId.NewId();
            var title = recording.Title;
            var slug = id.Replace(':', '-');
            var edition = new Edition
            {
                ForeignEditionId = id,
                TitleSlug = slug,
                Title = title,
                Subtitle = recording.Subtitle,
                Narrators = recording.Narrators,
                Language = recording.Language,
                Publisher = recording.Publisher,
                ReleaseDate = LocalRecordingMetadata.ReleaseDate(recording.Year),
                ChapterMetadata = recording.ChapterMetadata,
                RuntimeLengthMinutes = tags.RuntimeLengthMinutes,
                ReleaseDateIsPlaceholder = recording.Year.HasValue,
                Format = "Audiobook",
                IsEbook = false,
                Monitored = true,
                ManualAdd = true
            };
            var book = new Book
            {
                ForeignBookId = id,
                ForeignEditionId = id,
                AuthorMetadataId = author.AuthorMetadataId,
                AuthorMetadata = author.Metadata,
                Author = author,
                Title = title,
                CleanTitle = title.CleanBookTitle(),
                TitleSlug = slug,
                ReleaseDate = edition.ReleaseDate,
                Added = DateTime.UtcNow,
                Monitored = false,
                AnyEditionOk = false,
                AddOptions = new AddBookOptions { AddType = BookAddType.Manual },
                Editions = new List<Edition> { edition }
            };

            var ordered = selected.OrderBy(item => item.Tag.Disc).ThenBy(item => item.Tag.Track)
                .ThenBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                ordered[index].File.Part = index + 1;
            }

            _repository.Create(book, edition, ordered.Select(item => item.File).ToList(), LocalRecordingMetadata.EditSources(tags, recording), newAuthorRoot);
            if (newAuthorRoot != null)
            {
                _authors.NotifyAuthorAdded(author.Id, false);
            }

            var saved = _books.GetBook(book.Id);
            _events.PublishEvent(new BookAddedEvent(saved, false));
            _events.PublishEvent(new BookUpdatedEvent(saved));
            return saved;
        }

        public LocalRecording Get(int bookId) => _repository.Get(bookId);

        public Book Update(int bookId, LocalRecording recording)
        {
            LocalRecordingMetadata.Normalize(recording);
            if (string.IsNullOrWhiteSpace(recording.Revision) || (recording.BookId != 0 && recording.BookId != bookId))
            {
                throw Invalid("Refresh the local recording before editing it.");
            }

            _repository.Update(bookId, recording);
            var saved = _books.GetBook(bookId);
            _events.PublishEvent(new BookUpdatedEvent(saved));
            return saved;
        }

        private Author BuildLocalAuthor(LocalRecording recording, List<TaggedFile> selected, out RootFolder root)
        {
            var name = recording.NewAuthorName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 250 || string.IsNullOrWhiteSpace(recording.NewAuthorPath) ||
                !Path.IsPathFullyQualified(recording.NewAuthorPath))
            {
                throw Invalid("Enter a local author name and absolute folder.");
            }

            string path;
            try
            {
                path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(recording.NewAuthorPath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw Invalid("Enter a valid local author folder.");
            }

            root = _rootFolders.GetBestRootFolder(path);
            if (root == null || root.IsCalibreLibrary || !root.Path.IsParentPath(path) || root.Path.PathEquals(path) ||
                root.DefaultQualityProfileId <= 0 || root.DefaultMetadataProfileId <= 0 ||
                selected.Any(item => !path.IsParentPath(item.File.Path)))
            {
                throw Invalid("The local author folder must be inside a configured audio root and contain every selected file. The root must have valid default profiles.");
            }

            var id = LocalAuthorId.NewId();
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = id,
                TitleSlug = id.Replace(':', '-'),
                Name = name,
                SortName = name,
                NameLastFirst = name,
                SortNameLastFirst = name
            };

            return new Author
            {
                Metadata = metadata,
                CleanName = name.CleanAuthorName(),
                Path = path,
                RootFolderPath = root.Path,
                QualityProfileId = root.DefaultQualityProfileId,
                MetadataProfileId = root.DefaultMetadataProfileId,
                Tags = root.DefaultTags ?? new HashSet<int>(),
                Added = DateTime.UtcNow,
                Monitored = false,
                MonitorNewItems = NewItemMonitorTypes.None
            };
        }

        private List<TaggedFile> ReadFiles(List<int> fileIds)
        {
            if (fileIds == null || fileIds.Count == 0 || fileIds.Count > 1000 || fileIds.Any(id => id <= 0) || fileIds.Distinct().Count() != fileIds.Count)
            {
                throw Invalid("Select between 1 and 1000 distinct unmapped audio files from one recording.");
            }

            var wanted = fileIds.ToHashSet();
            var files = _files.GetUnmappedFiles().Where(file => wanted.Contains(file.Id)).ToList();
            if (files.Count != fileIds.Count || files.Any(file => file.EditionId != 0))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "One or more files are already mapped or no longer exist. Refresh Unmapped.");
            }

            var selected = new List<TaggedFile>();
            foreach (var file in files)
            {
                if (!MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(file.Path)) || !_disk.FileExists(file.Path))
                {
                    throw Invalid("Every selected file must be an existing audio file.");
                }

                var tag = _tags.ReadAudioTag(file.Path);
                if (tag == null || !tag.IsValid)
                {
                    throw Invalid("A selected file's tags could not be read. Check the file before creating a local recording.");
                }

                var parsed = tag.ToParsedTrackInfo(false);
                AudioRecordingIdentifier.Apply(parsed, file.Path, null);
                if (!string.IsNullOrEmpty(parsed.Asin) || parsed.AsinConflict)
                {
                    throw Invalid("A selected file has an Audible ASIN. Resolve its catalog match before creating a local recording.");
                }

                selected.Add(new TaggedFile { File = file, Tag = tag });
            }

            if (selected.Select(item => RecordingFolder(item.File.Path)).Distinct(PathEqualityComparer.Instance).Count() > 1 ||
                selected.Select(item => item.Tag.Book?.Trim()).Where(title => !string.IsNullOrEmpty(title)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                throw Invalid("Select files from one recording with consistent album tags. Create separate recordings for different editions.");
            }

            if (ConflictingCredits(selected.Select(item => item.Tag.BookAuthors)) ||
                ConflictingCredits(selected.Select(item => item.Tag.Narrators)))
            {
                throw Invalid("Selected files have conflicting author or narrator credits. Create separate recordings for different performances.");
            }

            return selected;
        }

        private LocalRecording Describe(List<TaggedFile> selected)
        {
            // The same ordering is used for the preview and file association.
            selected = selected.OrderBy(item => item.Tag.Disc).ThenBy(item => item.Tag.Track)
                .ThenBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var first = selected[0].Tag;
            var title = selected.Count == 1 ? first.Title ?? first.Book : first.Book;
            var authorNames = selected.SelectMany(item => item.Tag.BookAuthors ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
            var author = authorNames.Select(name => _authors.FindByName(name)).FirstOrDefault(value => value != null);
            var years = selected.Select(item => item.Tag.Year).Where(year => year > 0 && year <= 9999).Distinct().ToList();
            var result = new LocalRecording
            {
                FileIds = selected.Select(item => item.File.Id).ToList(),
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(selected[0].File.Path) : title.Trim(),
                AuthorId = author?.Id ?? 0,
                AuthorName = author?.Name ?? authorNames.FirstOrDefault(),
                Narrators = selected.SelectMany(item => item.Tag.Narrators ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList(),
                Language = first.Language,
                Publisher = first.Publisher,
                Year = years.Count == 1 ? (int?)years[0] : null,
                RuntimeLengthMinutes = selected.Any(item => item.Tag.Duration <= TimeSpan.Zero) ? (int?)null :
                    (int)Math.Round(selected.Sum(item => item.Tag.Duration.TotalMinutes))
            };
            result.Provenance = LocalRecordingMetadata.Sources(result, "fileTags");
            if (string.IsNullOrWhiteSpace(title))
            {
                result.Provenance["title"] = "fileName";
            }

            result.Warnings.Add("This recording uses local file tags. Catalog refresh will preserve it. Automatic searching starts disabled.");
            if (author == null)
            {
                var root = _rootFolders.GetBestRootFolder(selected[0].File.Path);
                if (root != null && root.Path.IsParentPath(selected[0].File.Path))
                {
                    var relative = Path.GetRelativePath(root.Path, selected[0].File.Path);
                    var separator = relative.IndexOf(Path.DirectorySeparatorChar);
                    if (separator > 0)
                    {
                        result.NewAuthorPath = Path.Combine(root.Path, relative[..separator]);
                    }
                }

                result.Warnings.Add("Select the author from your library or create a new local author for this folder.");
            }

            if (selected.Count > 1 && (selected.Any(item => item.Tag.Track == 0) || selected.GroupBy(item => (item.Tag.Disc, item.Tag.Track)).Any(group => group.Count() > 1)))
            {
                result.Warnings.Add("Some disc/track numbers are missing or repeated. Files will be ordered by disc, track, then filename.");
            }

            return result;
        }

        private static bool ConflictingCredits(IEnumerable<string[]> credits)
        {
            var populated = credits.Select(names => (names ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)).Where(names => names.Count > 0).ToList();
            return populated.Count > 1 && populated.Skip(1).Any(names => !names.SetEquals(populated[0]));
        }

        private static string RecordingFolder(string path)
        {
            var folder = Path.GetDirectoryName(path);
            return Regex.IsMatch(Path.GetFileName(folder), @"^(?:cd|disc|disk)[ _.-]*\d+$", RegexOptions.IgnoreCase)
                ? Path.GetDirectoryName(folder) : folder;
        }

        private static NzbDroneClientException Invalid(string message) => new NzbDroneClientException(HttpStatusCode.BadRequest, message);

        private class TaggedFile
        {
            public BookFile File { get; set; }
            public AudioTag Tag { get; set; }
        }
    }
}
