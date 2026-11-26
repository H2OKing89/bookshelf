using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public interface IAudiobookMetadataOverrideService
    {
        List<AudiobookMetadataOverride> All();
        AudiobookMetadataOverride Find(int id);
        AudiobookMetadataOverride Save(AudiobookMetadataOverride item);
        void Delete(int id);
        AudiobookAuthorIdentity FindAuthor(AudnexusContributorResource contributor, string region);
        string CanonicalAuthorId(string id);
        IEnumerable<string> AuthorSearchNames(AuthorMetadata metadata, string region);
        AudiobookMetadataProjection Project(Book book, Edition edition = null, IEnumerable<AudiobookMetadataCleanupRule> rules = null);
        string DisplayTitle(Book book, string publishedTitle = null, Edition edition = null);
        string NarratorName(string name, string region);
    }

    public class AudiobookMetadataOverrideService : IAudiobookMetadataOverrideService, IHandle<AuthorReconciledEvent>
    {
        private readonly IAudiobookMetadataOverrideRepository _repository;
        private readonly IAudiobookMetadataCleanupRuleService _cleanupRules;
        private readonly ICached<List<AudiobookMetadataOverride>> _cache;

        public AudiobookMetadataOverrideService(IAudiobookMetadataOverrideRepository repository, ICacheManager cacheManager, IAudiobookMetadataCleanupRuleService cleanupRules)
        {
            _repository = repository;
            _cleanupRules = cleanupRules;
            _cache = cacheManager.GetCache<List<AudiobookMetadataOverride>>(GetType());
        }

        private List<AudiobookMetadataOverride> Snapshot() => _cache.Get("all", () => _repository.All().ToList());

        public List<AudiobookMetadataOverride> All() => Snapshot().Select(item => new AudiobookMetadataOverride
        {
            Id = item.Id, Kind = item.Kind, Region = item.Region, Asin = item.Asin, Name = item.Name,
            AlternateAsins = item.AlternateAsins.ToList(), Aliases = item.Aliases.ToList(),
            OriginalTitle = item.OriginalTitle, OriginalSubtitle = item.OriginalSubtitle, ReplacementTitle = item.ReplacementTitle,
            SourceUrl = item.SourceUrl, Reason = item.Reason, Enabled = item.Enabled, UpdatedAt = item.UpdatedAt
        }).ToList();
        public AudiobookMetadataOverride Find(int id) => All().SingleOrDefault(item => item.Id == id);

        public AudiobookMetadataOverride Save(AudiobookMetadataOverride item)
        {
            item.Kind = item.Kind?.Trim().ToLowerInvariant();
            item.Region = item.Region?.Trim().ToLowerInvariant();
            item.Asin = item.Asin?.Trim().ToUpperInvariant();
            if (item.Kind == "narrator")
            {
                item.Asin = null;
            }

            item.Name = AudiobookTextNormalizer.Text(item.Name);
            item.AlternateAsins = (item.AlternateAsins ?? new List<string>()).Select(asin => asin?.Trim().ToUpperInvariant()).Distinct().ToList();
            item.Aliases = AudiobookTextNormalizer.Names(item.Aliases)
                .Where(name => !string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            item.SourceUrl = item.SourceUrl?.Trim();
            item.Reason = item.Reason?.Trim();
            if (item.Kind == "titleprotection")
            {
                item.OriginalTitle = string.IsNullOrWhiteSpace(item.OriginalTitle) ? null : item.OriginalTitle;
                item.OriginalSubtitle = string.IsNullOrWhiteSpace(item.OriginalSubtitle) ? null : item.OriginalSubtitle;
            }

            Validate(item);
            item.UpdatedAt = DateTime.UtcNow;
            var saved = item.Id == 0 ? _repository.Insert(item) : _repository.Update(item);
            _cache.Clear();
            return saved;
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
            _cache.Clear();
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AuthorReconciledEvent message)
        {
            _cache.Clear();
        }

        public AudiobookAuthorIdentity FindAuthor(AudnexusContributorResource contributor, string region)
        {
            var matches = Snapshot().Where(item => item.Enabled && item.Kind == "author" && item.Region == region)
                .Where(item => AudiobookId.IsAsin(contributor.Asin)
                    ? item.Asin == contributor.Asin || item.AlternateAsins.Contains(contributor.Asin)
                    : item.Aliases.Append(item.Name).Contains(AudiobookTextNormalizer.Text(contributor.Name), StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
            {
                return null;
            }

            var match = matches[0];
            return new AudiobookAuthorIdentity(match.Region, match.Asin, match.Name, match.AlternateAsins.ToArray(), match.Aliases.ToArray());
        }

        public string CanonicalAuthorId(string id)
        {
            var parsed = AudiobookId.Parse(id);
            var identity = FindAuthor(
                new AudnexusContributorResource
                {
                    Asin = parsed.IsName ? null : parsed.Value,
                    Name = parsed.IsName ? parsed.Value : null
                },
                parsed.Region);
            return identity == null ? id : AudiobookId.ForAsin(parsed.Region, identity.Asin);
        }

        public IEnumerable<string> AuthorSearchNames(AuthorMetadata metadata, string region)
        {
            var parsed = AudiobookId.Parse(metadata.ForeignAuthorId);
            var identity = FindAuthor(new AudnexusContributorResource { Asin = parsed.IsName ? null : parsed.Value, Name = metadata.Name }, region);
            return AudiobookTextNormalizer.Names((identity?.Aliases ?? Array.Empty<string>())
                .Concat(new[] { identity?.Name, metadata.Name }).Concat(metadata.Aliases));
        }

        public AudiobookMetadataProjection Project(Book book, Edition edition = null, IEnumerable<AudiobookMetadataCleanupRule> rules = null)
        {
            edition ??= book.Editions?.Value?.SingleOrDefault(item => item.Monitored);
            var title = edition?.Title ?? book.Title;
            var recordingIds = new[]
            {
                NativeRecordingId(book.ForeignBookId), NativeRecordingId(edition?.ForeignEditionId),
                AudiobookId.IsAsin(edition?.Asin) ? NativeRecordingId(AudiobookId.ForAsin(edition.AudibleRegion, edition.Asin)) : null
            }.Where(id => id != null).Distinct(StringComparer.Ordinal).ToList();
            var recordingId = recordingIds.Count == 1 ? recordingIds[0] : null;
            var correction = Snapshot().SingleOrDefault(item => item.Enabled && item.Kind == "title" &&
                AudiobookId.ForAsin(item.Region, item.Asin) == recordingId && item.OriginalTitle == title);
            var protection = Snapshot().SingleOrDefault(item => item.Enabled && item.Kind == "titleprotection" &&
                AudiobookId.ForAsin(item.Region, item.Asin) == recordingId);
            var projection = AudiobookMetadataCleanup.Project(book,
                edition,
                rules ?? _cleanupRules.All() ?? new List<AudiobookMetadataCleanupRule>(),
                correction?.ReplacementTitle,
                correction == null && protection?.OriginalTitle != null && protection.OriginalTitle == title,
                protection?.OriginalSubtitle != null && protection.OriginalSubtitle == edition?.Subtitle);
            if (recordingIds.Count > 1)
            {
                projection.Warnings.Add("The available recording identities disagree; recording-specific corrections and protections were not applied.");
            }

            if (protection?.OriginalTitle != null && protection.OriginalTitle != title)
            {
                projection.Warnings.Add("The provider title changed; its protection no longer applies.");
            }

            if (protection?.OriginalSubtitle != null && protection.OriginalSubtitle != edition?.Subtitle)
            {
                projection.Warnings.Add("The provider subtitle changed; its protection no longer applies.");
            }

            return projection;
        }

        private static string NativeRecordingId(string id)
        {
            var parts = id?.Split(':');
            return parts?.Length == 3 && parts[0] == "audible" && AudnexusProxy.Regions.Contains(parts[1]) && AudiobookId.IsAsin(parts[2]) ? id : null;
        }

        public string DisplayTitle(Book book, string publishedTitle = null, Edition edition = null)
        {
            edition ??= book.Editions?.Value?.SingleOrDefault(item => item.Monitored);
            if (publishedTitle != null && publishedTitle != (edition?.Title ?? book.Title))
            {
                edition = new Edition
                {
                    Id = edition?.Id ?? 0, BookId = edition?.BookId ?? 0, ForeignEditionId = edition?.ForeignEditionId,
                    Book = book, Title = publishedTitle, Subtitle = edition?.Subtitle,
                    Asin = edition?.Asin, AudibleRegion = edition?.AudibleRegion, Language = edition?.Language,
                    Narrators = edition?.Narrators, Publisher = edition?.Publisher
                };
            }

            return Project(book, edition).DisplayTitle;
        }

        public string NarratorName(string name, string region)
        {
            var matches = Snapshot().Where(item => item.Enabled && item.Kind == "narrator" && item.Region == region &&
                item.Aliases.Append(item.Name).Contains(AudiobookTextNormalizer.Text(name), StringComparer.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0].Name : name;
        }

        private void Validate(AudiobookMetadataOverride item)
        {
            var errors = new List<ValidationFailure>();
            void Require(bool valid, string field, string message)
            {
                if (!valid)
                {
                    errors.Add(new ValidationFailure(field, message));
                }
            }

            Require(item.Kind == "author" || item.Kind == "title" || item.Kind == "narrator" || item.Kind == "titleprotection", "kind", "Choose author, title, narrator, or title protection.");
            Require(item.Region != null && AudnexusProxy.Regions.Contains(item.Region), "region", "Choose a supported marketplace.");
            Require(item.Kind == "narrator" || AudiobookId.IsAsin(item.Asin), "asin", "Enter a valid Audible ASIN.");
            Require(Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var source) && (source.Scheme == "https" || source.Scheme == "http"), "sourceUrl", "Provide a source URL for this correction.");
            Require(!string.IsNullOrWhiteSpace(item.Reason), "reason", "Explain the evidence for this correction.");
            var others = All().Where(other => other.Id != item.Id).ToList();
            Require(
                !others.Any(other => other.Kind == item.Kind && other.Region == item.Region &&
                (item.Kind == "narrator" ? string.Equals(other.Name, item.Name, StringComparison.OrdinalIgnoreCase) : other.Asin == item.Asin)),
                "asin",
                "A correction already exists for this identity.");
            if (item.Kind == "author")
            {
                Require(!string.IsNullOrWhiteSpace(item.Name), "name", "Provide the canonical author name.");
                Require(item.AlternateAsins.All(AudiobookId.IsAsin) && !item.AlternateAsins.Contains(item.Asin), "alternateAsins", "Alternate ASINs must be valid and different from the canonical ASIN.");
                if (item.Enabled)
                {
                    var ids = item.AlternateAsins.Append(item.Asin).ToHashSet();
                    var names = item.Aliases.Append(item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Require(
                        !others.Where(other => other.Enabled && other.Kind == "author" && other.Region == item.Region)
                        .Any(other => other.AlternateAsins.Append(other.Asin).Any(ids.Contains) || other.Aliases.Append(other.Name).Any(names.Contains)),
                        "alternateAsins",
                        "An enabled mapping already claims one of these author IDs or names.");
                }
            }
            else if (item.Kind == "title")
            {
                Require(!string.IsNullOrWhiteSpace(item.OriginalTitle), "originalTitle", "Provide the exact provider title this correction applies to.");
                Require(!string.IsNullOrWhiteSpace(item.ReplacementTitle) && item.ReplacementTitle != item.OriginalTitle, "replacementTitle", "Provide a different corrected title.");
            }
            else if (item.Kind == "titleprotection")
            {
                Require(item.OriginalTitle != null || item.OriginalSubtitle != null, "originalTitle", "Provide the exact provider title or subtitle to protect.");
            }
            else if (item.Kind == "narrator")
            {
                Require(!string.IsNullOrWhiteSpace(item.Name), "name", "Provide the canonical narrator name.");
                var names = item.Aliases.Append(item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Require(
                    !item.Enabled || !others.Where(other => other.Enabled && other.Kind == "narrator" && other.Region == item.Region)
                    .Any(other => other.Aliases.Append(other.Name).Any(names.Contains)),
                    "aliases",
                    "An enabled narrator mapping already claims one of these names.");
            }

            if (errors.Any())
            {
                throw new ValidationException(errors);
            }
        }
    }
}
