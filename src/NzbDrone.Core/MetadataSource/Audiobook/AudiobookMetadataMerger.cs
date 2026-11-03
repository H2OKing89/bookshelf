using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookMetadataMerger
    {
        public static void PreserveMissing(Book incoming, Book stored, List<Edition> storedEditions)
        {
            if (!AudiobookId.IsNative(incoming.ForeignBookId) || incoming.ForeignBookId != stored.ForeignBookId)
            {
                return;
            }

            // Catalog responses can omit the tags present in a detailed Audnexus lookup.
            incoming.Genres = (incoming.Genres ?? new List<string>()).Concat(stored.Genres ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            incoming.ReleaseDate ??= stored.ReleaseDate;
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

                edition.Subtitle = PreferPresent(edition.Subtitle, previous.Subtitle);
                edition.Overview = PreferPresent(edition.Overview, previous.Overview);
                edition.Publisher = PreferPresent(edition.Publisher, previous.Publisher);
                edition.Language = PreferPresent(edition.Language, previous.Language);
                edition.Isbn13 = PreferPresent(edition.Isbn13, previous.Isbn13);
                edition.Disambiguation = PreferPresent(edition.Disambiguation, previous.Disambiguation);
                edition.ReleaseDate ??= previous.ReleaseDate;
                edition.RuntimeLengthMinutes ??= previous.RuntimeLengthMinutes;
                if (edition.Narrators?.Any() != true)
                {
                    edition.Narrators = previous.Narrators;
                }

                if (edition.Images?.Any() != true)
                {
                    edition.Images = previous.Images;
                }

                if (edition.Ratings.Value <= 0)
                {
                    edition.Ratings = previous.Ratings;
                }
            }
        }

        private static string PreferPresent(string incoming, string stored) => incoming.IsNullOrWhiteSpace() ? stored : incoming;
    }
}
