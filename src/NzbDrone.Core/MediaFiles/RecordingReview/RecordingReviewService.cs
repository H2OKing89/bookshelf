using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.MediaFiles.RecordingReview
{
    public interface IRecordingReviewService
    {
        RecordingReviewResult Inspect(int fileId);
        RecordingReviewResult Associate(int fileId, int editionId, string revision);
    }

    public class RecordingReviewService : IRecordingReviewService
    {
        private const int CandidateLimit = 16;
        private const int EvidenceLimit = 16;

        private readonly IMediaFileService _files;
        private readonly IRecordingReviewRepository _review;
        private readonly IBookRepository _books;
        private readonly IAuthorRepository _authors;
        private readonly IAudioTagService _tags;
        private readonly IDiskProvider _disk;
        private readonly IConfigService _config;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public RecordingReviewService(IMediaFileService files,
                                      IRecordingReviewRepository review,
                                      IBookRepository books,
                                      IAuthorRepository authors,
                                      IAudioTagService tags,
                                      IDiskProvider disk,
                                      IConfigService config,
                                      IEventAggregator events,
                                      Logger logger)
        {
            _files = files;
            _review = review;
            _books = books;
            _authors = authors;
            _tags = tags;
            _disk = disk;
            _config = config;
            _events = events;
            _logger = logger;
        }

        public RecordingReviewResult Inspect(int fileId)
        {
            return Assess(fileId).Result;
        }

        public RecordingReviewResult Associate(int fileId, int editionId, string revision)
        {
            if (editionId <= 0 || revision?.Length != 64)
            {
                throw new BadRequestException("An edition and its association preview are required.");
            }

            var context = Assess(fileId);
            var target = context.Result.Candidates.SingleOrDefault(candidate => candidate.EditionId == editionId);
            if (context.Edition == null || target?.Revision == null || !string.Equals(revision, target.Revision, StringComparison.Ordinal) ||
                !MatchesLiveFile(context.File, context.FileSize, context.FileModified) ||
                !_review.Associate(context.File, context.Edition, context.Book, context.Author))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "The file or recording changed. Review the current evidence and preview again.");
            }

            context.File.EditionId = target.EditionId;
            context.File.Part = 1;
            context.File.Edition = context.Edition;
            context.Edition.Book = context.Book;
            context.File.Author = context.Author;
            context.Book.Author = context.Author;
            target.Revision = null;
            context.Result.CurrentRecording = target;
            context.Result.Reason = "alreadyAssociated";

            try
            {
                _events.PublishEvent(new BookUpdatedEvent(context.Book));
                _events.PublishEvent(new BookFileAddedEvent(context.File));
            }
            catch (Exception error)
            {
                _logger.Warn("Recording association saved, but notification failed ({0})", error.GetType().Name);
            }

            return context.Result;
        }

        private ReviewContext Assess(int fileId)
        {
            BookFile file;
            try
            {
                file = _files.Get(fileId);
            }
            catch (ModelNotFoundException)
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "The book file was not found. Refresh your library.");
            }

            if (file == null)
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "The book file was not found. Refresh your library.");
            }

            var saved = file.Edition?.Value;
            var result = new RecordingReviewResult
            {
                FileId = file.Id,
                FileName = Path.GetFileName(file.Path),
                PreferPathAsin = _config.PreferPathAsin,
                CurrentRecording = saved == null ? null : Describe(saved, file.Author?.Value)
            };
            var context = new ReviewContext { File = file, Result = result };

            if (!MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(file.Path)))
            {
                result.Reason = "unsupportedFile";
                return context;
            }

            if (!TryReadState(file.Path, out var size, out var modified))
            {
                result.Reason = "fileUnavailable";
                return context;
            }

            var tag = ReadTag(file);
            var evidence = tag?.GetRecordingEvidence(file.Path, result.PreferPathAsin) ??
                AudioRecordingIdentifier.Inspect(file.Path, null, result.PreferPathAsin);
            result.FilenameAsins = evidence.FilenameAsins.Take(EvidenceLimit).ToList();
            result.FolderAsins = evidence.FolderAsins.Take(EvidenceLimit).ToList();
            result.EmbeddedAsins = evidence.EmbeddedAsins.Take(EvidenceLimit).ToList();
            result.MalformedEmbeddedAsin = evidence.MalformedEmbeddedAsin;
            result.EffectiveAsin = evidence.EffectiveAsin;
            result.TagTitle = LimitText(tag?.Book ?? tag?.Title);
            result.TagAuthors = LimitNames(tag?.BookAuthors);
            result.TagNarrators = LimitNames(tag?.Narrators);

            if (!MatchesLiveFile(file, size, modified))
            {
                result.Reason = "fileChanged";
                return context;
            }

            var evidenceAsins = evidence.FilenameAsins.Concat(evidence.FolderAsins).Concat(evidence.EmbeddedAsins)
                .Distinct(StringComparer.Ordinal).Take(EvidenceLimit).ToArray();
            var catalog = FindCatalog(evidenceAsins);
            var authors = FindAuthors(catalog);
            result.Candidates = catalog.Take(CandidateLimit).Select(candidate =>
                Describe(candidate.Edition, GetAuthor(authors, candidate.Book))).ToList();

            if (evidence.IdentifierConflict)
            {
                result.Reason = "identifierConflict";
            }
            else if (file.EditionId > 0)
            {
                result.Reason = saved == null ? "catalogMissing" : MappedReason(evidence.EffectiveAsin, result.TagNarrators, saved);
            }
            else if (evidence.EffectiveAsin == null)
            {
                result.Reason = result.TagAuthors.Count == 0 ? "authorMissing" : "insufficientEvidence";
            }
            else
            {
                EvaluateTarget(context, catalog, authors, tag, size, modified);
            }

            return context;
        }

        private void EvaluateTarget(ReviewContext context,
                                    List<CatalogCandidate> catalog,
                                    List<Author> authors,
                                    AudioTag tag,
                                    long size,
                                    DateTime modified)
        {
            var result = context.Result;
            var exact = catalog.Where(candidate => AudioRecordingIdentifier.MatchesAsin(result.EffectiveAsin, candidate.Edition)).ToList();
            if (exact.Count == 0)
            {
                result.Reason = "catalogMissing";
                return;
            }

            if (exact.Count != 1 || catalog.Count > CandidateLimit)
            {
                result.Reason = "ambiguousCatalogMatch";
                return;
            }

            var candidate = exact[0];
            var edition = candidate.Edition;
            var book = candidate.Book;
            var author = GetAuthor(authors, book);
            var file = context.File;
            if (author == null)
            {
                result.Reason = "authorMissing";
            }
            else if (HasDifferentNarration(result.TagNarrators, edition))
            {
                result.Reason = "differentPerformance";
            }
            else if (!IsCanonicalIdentity(edition, result.EffectiveAsin) || tag?.IsValid != true)
            {
                result.Reason = "insufficientEvidence";
            }
            else if (file.Part > 1 || tag.Track > 1 || tag.TrackCount > 1 || tag.Disc > 1 || tag.DiscCount > 1 ||
                     _files.GetFilesByBook(book.Id).Count > 0)
            {
                result.Reason = "existingOrMultipartRecording";
            }
            else if (file.CalibreId != 0 || string.IsNullOrWhiteSpace(author.Path) || !Path.IsPathFullyQualified(author.Path) ||
                     !author.Path.IsParentPath(file.Path))
            {
                result.Reason = "outsideAuthorFolder";
            }
            else
            {
                context.Edition = edition;
                context.Book = book;
                context.Author = author;
                context.FileSize = size;
                context.FileModified = modified;
                result.Reason = "exactCatalogMatch";
                var target = result.Candidates.Single(item => item.EditionId == edition.Id);
                target.Revision = Revision(context, target);
            }
        }

        private List<CatalogCandidate> FindCatalog(string[] asins)
        {
            if (asins.Length == 0)
            {
                return new List<CatalogCandidate>();
            }

            var recordingIds = asins.SelectMany(asin => AudnexusProxy.Regions.Select(region => AudiobookId.ForAsin(region, asin))).ToArray();
            var editions = _review.FindCatalogEditions(asins, recordingIds, CandidateLimit + 1);
            var books = editions.Select(edition => edition.BookId).Distinct().ToDictionary(id => id, _books.Find);
            return editions.Select(edition => new CatalogCandidate
            {
                Edition = edition,
                Book = books.GetValueOrDefault(edition.BookId)
            }).ToList();
        }

        private List<Author> FindAuthors(List<CatalogCandidate> catalog)
        {
            var metadataIds = catalog.Where(candidate => candidate.Book != null)
                .Select(candidate => candidate.Book.AuthorMetadataId).Distinct().ToArray();
            return metadataIds.Length == 0 ? new List<Author>() : _authors.GetAuthorsByMetadataId(metadataIds);
        }

        private static Author GetAuthor(List<Author> authors, Book book)
        {
            var matches = authors.Where(author => author.AuthorMetadataId == book?.AuthorMetadataId).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static RecordingReviewTarget Describe(Edition edition, Author author)
        {
            return new RecordingReviewTarget
            {
                EditionId = edition.Id,
                BookId = edition.BookId,
                AuthorId = author?.Id ?? 0,
                Title = LimitText(edition.Title),
                Author = LimitText(author?.Name),
                RecordingId = LimitText(edition.ForeignEditionId),
                Asin = AudioRecordingIdentifier.Normalize(edition.Asin),
                Region = LimitText(edition.AudibleRegion),
                Narrators = LimitNames(edition.Narrators)
            };
        }

        private bool MatchesLiveFile(BookFile file, long size, DateTime modified)
        {
            var savedTime = DateTime.SpecifyKind(file.Modified, DateTimeKind.Utc);
            return file.Size == size &&
                Math.Abs((savedTime - modified).TotalMilliseconds) <= MediaInfoAnalysis.StoredTimestampToleranceMilliseconds &&
                TryReadState(file.Path, out var currentSize, out var currentModified) && size == currentSize && modified == currentModified;
        }

        private bool TryReadState(string path, out long size, out DateTime modified)
        {
            size = 0;
            modified = default;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            try
            {
                var file = _disk.GetFileInfo(path);
                if (file?.Exists != true || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                size = file.Length;
                modified = file.LastWriteTimeUtc;
                return size > 0 && modified != default;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }

        private AudioTag ReadTag(BookFile file)
        {
            try
            {
                return _tags.ReadAudioTag(file.Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _logger.Debug("Recording review could not read audio tags ({0})", error.GetType().Name);
                return null;
            }
        }

        private static string MappedReason(string asin, List<string> narrators, Edition edition)
        {
            return (asin != null && !AudioRecordingIdentifier.MatchesAsin(asin, edition)) || HasDifferentNarration(narrators, edition)
                ? "differentPerformance"
                : "alreadyAssociated";
        }

        private static bool HasDifferentNarration(List<string> narrators, Edition edition)
        {
            var known = LimitNames(edition.Narrators);
            return narrators.Count > 0 && known.Count > 0 &&
                !narrators.Select(AudiobookTextNormalizer.Text).Intersect(known.Select(AudiobookTextNormalizer.Text), StringComparer.OrdinalIgnoreCase).Any();
        }

        private static bool IsCanonicalIdentity(Edition edition, string asin)
        {
            return edition.AudibleRegion != null && AudnexusProxy.Regions.Contains(edition.AudibleRegion) &&
                string.Equals(edition.ForeignEditionId, AudiobookId.ForAsin(edition.AudibleRegion, asin), StringComparison.Ordinal);
        }

        private static string Revision(ReviewContext context, RecordingReviewTarget target)
        {
            var snapshot = new
            {
                context.File.Id,
                context.File.Path,
                context.FileModified,
                context.FileSize,
                context.File.Part,
                context.Result.PreferPathAsin,
                context.Result.EffectiveAsin,
                context.Result.FilenameAsins,
                context.Result.FolderAsins,
                context.Result.EmbeddedAsins,
                context.Result.TagTitle,
                context.Result.TagAuthors,
                context.Result.TagNarrators,
                AuthorPath = context.Author.Path,
                context.Book.ForeignBookId,
                context.Book.AuthorMetadataId,
                Target = target
            };
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(STJson.ToJson(snapshot))));
        }

        private static string LimitText(string value)
        {
            return value?.Length > 500 ? value.Substring(0, 500) : value;
        }

        private static List<string> LimitNames(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(EvidenceLimit).Select(LimitText).ToList();
        }

        private sealed class ReviewContext
        {
            public BookFile File { get; set; }
            public RecordingReviewResult Result { get; set; }
            public Edition Edition { get; set; }
            public Book Book { get; set; }
            public Author Author { get; set; }
            public long FileSize { get; set; }
            public DateTime FileModified { get; set; }
        }

        private sealed class CatalogCandidate
        {
            public Edition Edition { get; set; }
            public Book Book { get; set; }
        }
    }
}
