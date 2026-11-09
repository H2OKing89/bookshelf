using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookMetadataMerger
    {
        public static void PreserveMissing(Book incoming, Book stored, List<Edition> storedEditions, bool preferStoredSeries = false)
        {
            if (!AudiobookId.IsNative(incoming.ForeignBookId) || incoming.ForeignBookId != stored.ForeignBookId)
            {
                return;
            }

            // Catalog responses can omit the tags present in a detailed Audnexus lookup.
            incoming.Genres = AudiobookTextNormalizer.Names((incoming.Genres ?? new List<string>()).Concat(stored.Genres ?? new List<string>()));
            incoming.Classifications = MergeReferences(
                incoming.Classifications,
                stored.Classifications,
                item => item.Name,
                item => item.Asin,
                item => item.Type,
                (item, asin) => new AudiobookClassification { Name = item.Name, Asin = asin, Type = item.Type });
            MergeSeries(incoming, stored, preferStoredSeries);
            incoming.ReleaseDate ??= AudiobookReleaseDate.Normalize(stored.ReleaseDate);
            if (incoming.Ratings.Value <= 0)
            {
                incoming.Ratings = stored.Ratings;
            }

            foreach (var edition in incoming.Editions.Value)
            {
                var previous = storedEditions.SingleOrDefault(e => e.ForeignEditionId == edition.ForeignEditionId &&
                    e.Asin == edition.Asin && e.AudibleRegion == edition.AudibleRegion);
                if (previous == null)
                {
                    continue;
                }

                edition.Subtitle = AudiobookTextNormalizer.Text(PreferPresent(edition.Subtitle, previous.Subtitle));
                edition.Overview = PreferPresent(edition.Overview, previous.Overview);
                edition.Publisher = AudiobookTextNormalizer.Publisher(PreferPresent(edition.Publisher, previous.Publisher));
                edition.Language = AudiobookTextNormalizer.Language(PreferPresent(edition.Language, previous.Language));
                edition.Isbn13 = PreferPresent(edition.Isbn13, previous.Isbn13);
                edition.Disambiguation = PreferPresent(edition.Disambiguation, previous.Disambiguation);
                PreserveReleaseDate(edition, previous);
                edition.RuntimeLengthMinutes ??= previous.RuntimeLengthMinutes;
                edition.CopyrightYear ??= previous.CopyrightYear;
                edition.IsAdult ??= previous.IsAdult;
                edition.LiteratureType = PreferPresent(edition.LiteratureType, previous.LiteratureType);
                edition.Contributors = MergeReferences(
                    edition.Contributors,
                    previous.Contributors,
                    item => item.Name,
                    item => item.Asin,
                    item => item.Role,
                    (item, asin) => new AudiobookContributor { Name = item.Name, Asin = asin, Role = item.Role });
                if (edition.ChapterMetadata?.Chapters?.Any() != true && previous.ChapterMetadata != null)
                {
                    edition.ChapterMetadata = previous.ChapterMetadata.JsonClone();
                }

                edition.Narrators = AudiobookTextNormalizer.Names((edition.Narrators ?? new List<string>())
                    .Concat(previous.Narrators ?? new List<string>())
                    .Select(name => AudiobookContributors.Credit(new AudnexusContributorResource { Name = name }, "Narrator"))
                    .Where(credit => credit?.Role == "Narrator").Select(credit => credit.Name)
                    .Concat(edition.Contributors.Where(credit => credit.Role == "Narrator").Select(credit => credit.Name)));

                edition.Images = AudiobookCoverSelector.PreferLarger(edition.Images, previous.Images).JsonClone();

                if (edition.Ratings.Value <= 0)
                {
                    edition.Ratings = previous.Ratings;
                }
            }

            var selected = incoming.Editions.Value.SingleOrDefault(edition => edition.Monitored);
            if (selected?.ReleaseDateIsPlaceholder == true)
            {
                incoming.ReleaseDate = null;
            }
            else if (selected?.ReleaseDate != null)
            {
                incoming.ReleaseDate = selected.ReleaseDate;
            }
        }

        private static void PreserveReleaseDate(Edition incoming, Edition stored)
        {
            if (incoming.ReleaseDateIsPlaceholder)
            {
                var previousDate = AudiobookReleaseDate.Normalize(stored.ReleaseDate);
                if (previousDate.HasValue && !stored.ReleaseDateIsPlaceholder)
                {
                    incoming.ReleaseDate = previousDate;
                    incoming.SourceReleaseDate = stored.SourceReleaseDate ?? previousDate;
                    incoming.ReleaseDateIsPlaceholder = false;
                }
            }
            else if (!incoming.ReleaseDate.HasValue && !incoming.SourceReleaseDate.HasValue)
            {
                incoming.ReleaseDate = AudiobookReleaseDate.Normalize(stored.ReleaseDate);
                incoming.SourceReleaseDate = stored.SourceReleaseDate ?? stored.ReleaseDate;
                incoming.ReleaseDateIsPlaceholder = stored.ReleaseDateIsPlaceholder || AudiobookReleaseDate.IsPlaceholder(stored.ReleaseDate);
            }
        }

        private static List<T> MergeReferences<T>(
            IEnumerable<T> incoming,
            IEnumerable<T> stored,
            Func<T, string> name,
            Func<T, string> asin,
            Func<T, string> kind,
            Func<T, string, T> copy)
            where T : class
        {
            var all = (incoming ?? Enumerable.Empty<T>()).Concat(stored ?? Enumerable.Empty<T>())
                .Where(item => item != null && name(item).IsNotNullOrWhiteSpace()).ToList();
            string NameKey(T item) => AudiobookTextNormalizer.Text(kind(item)) + "\0" + AudiobookTextNormalizer.Text(name(item));
            var known = all.Where(item => asin(item).IsNotNullOrWhiteSpace()).GroupBy(NameKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(asin).Distinct(StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
            string Identity(T item)
            {
                if (asin(item).IsNotNullOrWhiteSpace())
                {
                    return asin(item);
                }

                return known.TryGetValue(NameKey(item), out var ids) && ids.Count == 1 ? ids[0] : null;
            }

            // A name-only credit may inherit a single unambiguous ID for this
            // recording/role. Two explicit, conflicting IDs always stay separate.
            return all.GroupBy(item => kind(item) + "\0" + (Identity(item) is string id ? "id:" + id : "name:" + name(item)), StringComparer.OrdinalIgnoreCase)
                .Select(group => copy(group.First(), Identity(group.First()))).ToList();
        }

        public static List<AudiobookPersonReference> MergePeopleReferences(List<AudiobookPersonReference> incoming, List<AudiobookPersonReference> stored)
        {
            return MergeReferences(
                incoming,
                stored,
                person => person.Name,
                person => person.Asin,
                person => null,
                (person, asin) => new AudiobookPersonReference { Name = person.Name, Asin = asin });
        }

        public static void MergeSeries(Book target, Book supplement, bool preferSupplement = false)
        {
            if (!AudiobookId.IsNative(target.ForeignBookId) || target.ForeignBookId != supplement.ForeignBookId)
            {
                return;
            }

            var current = target.SeriesLinks?.Value ?? new List<SeriesBookLink>();
            var additional = supplement.SeriesLinks?.Value ?? new List<SeriesBookLink>();
            var preferred = preferSupplement ? additional.Concat(current) : current.Concat(additional);
            var primaryId = preferred.FirstOrDefault(link => link.IsPrimary)?.Series?.Value?.ForeignSeriesId;
            var links = new List<SeriesBookLink>();
            foreach (var group in preferred.Where(link => link.Series?.Value?.ForeignSeriesId.IsNotNullOrWhiteSpace() == true)
                .GroupBy(link => link.Series.Value.ForeignSeriesId))
            {
                var first = group.First();
                var source = first.Series.Value;
                var positioned = group.FirstOrDefault(link => link.Position.IsNotNullOrWhiteSpace()) ?? first;
                var series = new Series
                {
                    Id = source.Id,
                    ForeignSeriesId = source.ForeignSeriesId,
                    ForeignAuthorId = target.AuthorMetadata?.Value?.ForeignAuthorId,
                    Title = source.Title,
                    Description = group.Select(link => link.Series.Value.Description).FirstOrDefault(value => value.IsNotNullOrWhiteSpace()),
                    Numbered = positioned.Position.IsNotNullOrWhiteSpace(),
                    WorkCount = source.WorkCount,
                    PrimaryWorkCount = source.PrimaryWorkCount,
                    Books = new List<Book> { target }
                };
                var link = new SeriesBookLink
                {
                    Id = first.Id,
                    BookId = target.Id,
                    SeriesId = source.Id,
                    Book = target,
                    Series = series,
                    Position = positioned.Position,
                    SeriesPosition = positioned.SeriesPosition,
                    IsPrimary = source.ForeignSeriesId == primaryId
                };
                series.LinkItems = new List<SeriesBookLink> { link };
                links.Add(link);
            }

            if (links.Any() && !links.Any(link => link.IsPrimary))
            {
                links[0].IsPrimary = true;
            }

            target.SeriesLinks = links;
        }

        private static string PreferPresent(string incoming, string stored) => incoming.IsNullOrWhiteSpace() ? stored : incoming;
    }
}
