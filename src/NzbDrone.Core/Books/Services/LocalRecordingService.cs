using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    public interface ILocalRecordingService
    {
        LocalRecording Preview(List<int> fileIds);
        Book Create(LocalRecording recording);
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

        public LocalRecordingService(IMediaFileService files,
            IAudioTagService tags,
            IDiskProvider disk,
            IAuthorService authors,
            IBookService books,
            ILocalRecordingRepository repository,
            IEventAggregator events)
        {
            _files = files;
            _tags = tags;
            _disk = disk;
            _authors = authors;
            _books = books;
            _repository = repository;
            _events = events;
        }

        public LocalRecording Preview(List<int> fileIds)
        {
            return Describe(ReadFiles(fileIds));
        }

        public Book Create(LocalRecording recording)
        {
            if (recording == null || string.IsNullOrWhiteSpace(recording.Title) || recording.Title.Length > 1000 ||
                recording.AuthorId <= 0 || recording.Year < 1 || recording.Year > 9999 ||
                recording.Subtitle?.Length > 1000 || recording.Publisher?.Length > 1000 || recording.Language?.Length > 100 ||
                recording.Narrators?.Count > 100 || recording.Narrators?.Any(name => name?.Length > 500) == true)
            {
                throw Invalid("Choose an author and enter a title. Check that the metadata fields and year are valid.");
            }

            var selected = ReadFiles(recording.FileIds);
            var tags = Describe(selected);
            var author = _authors.GetAuthor(recording.AuthorId);
            var id = LocalRecordingId.NewId();
            var title = recording.Title.Trim();
            var slug = id.Replace(':', '-');
            var edition = new Edition
            {
                ForeignEditionId = id,
                TitleSlug = slug,
                Title = title,
                Subtitle = recording.Subtitle?.Trim(),
                Narrators = (recording.Narrators ?? new List<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).Distinct().ToList(),
                Language = recording.Language?.Trim(),
                Publisher = recording.Publisher?.Trim(),
                ReleaseDate = recording.Year.HasValue ? new DateTime(recording.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : (DateTime?)null,
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

            _repository.Create(book, edition, ordered.Select(item => item.File).ToList());
            var saved = _books.GetBook(book.Id);
            _events.PublishEvent(new BookAddedEvent(saved, false));
            _events.PublishEvent(new BookUpdatedEvent(saved));
            return saved;
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
            result.Warnings.Add("This recording uses local file tags. Catalog refresh will preserve it. Automatic searching starts disabled.");
            if (author == null)
            {
                result.Warnings.Add("Select the author from your library.");
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
