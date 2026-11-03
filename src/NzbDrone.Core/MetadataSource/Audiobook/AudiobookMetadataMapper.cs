using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookMetadataMapper
    {
        public static AuthorMetadata Author(AudnexusContributorResource contributor, string region, AudiobookAuthorIdentity identity = null)
        {
            var asin = identity?.Asin ?? contributor.Asin;
            var name = identity?.Name ?? AudiobookTextNormalizer.Text(contributor.Name);
            var id = AudiobookId.IsAsin(asin)
                ? AudiobookId.ForAsin(region, asin)
                : AudiobookId.ForName(region, contributor.Name);
            var profile = contributor as AudnexusAuthorResource;
            var result = new AuthorMetadata
            {
                ForeignAuthorId = id,
                TitleSlug = "author-" + region + "-" + (AudiobookId.IsAsin(asin) ? asin :
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contributor.Name))).Substring(0, 16).ToLowerInvariant()),
                Name = name,
                SortName = name,
                NameLastFirst = name,
                SortNameLastFirst = name,
                Aliases = AudiobookTextNormalizer.Names((identity?.Aliases ?? Array.Empty<string>()).Append(contributor.Name))
                    .Where(alias => !string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)).ToList(),
                Overview = profile?.Description,
                Status = AuthorStatusType.Continuing
            };
            AddImage(result.Images, profile?.Image, MediaCoverTypes.Poster);
            if (AudiobookId.IsAsin(asin))
            {
                result.Links.Add(new Links { Name = "Audible", Url = $"{StoreUrl(region)}/author/{asin}" });
            }

            return result;
        }

        public static Book Book(AudnexusBookResource resource, string region, AuthorMetadata expectedAuthor = null, AudiobookAuthorIdentity identity = null)
        {
            if (!AudiobookId.IsAsin(resource?.Asin) || resource.Region != region || resource.Title.IsNullOrWhiteSpace() ||
                resource.Authors?.Any(a => a?.Name.IsNotNullOrWhiteSpace() == true) != true)
            {
                throw new BookInfoException("Audiobook metadata is missing its ASIN, marketplace, title, or author.");
            }

            var contributor = resource.Authors.First(a => a?.Name.IsNotNullOrWhiteSpace() == true);
            var metadata = Author(contributor, region, identity);

            // A missing contributor ASIN can use the author already established for
            // this bibliography/recording. A conflicting ASIN must remain separate.
            if (!AudiobookId.IsAsin(contributor.Asin) && expectedAuthor != null &&
                AudiobookId.Parse(expectedAuthor.ForeignAuthorId).Region == region &&
                new[] { expectedAuthor.Name }.Concat(expectedAuthor.Aliases).Any(name =>
                    string.Equals(AudiobookTextNormalizer.Text(name), AudiobookTextNormalizer.Text(contributor.Name), StringComparison.OrdinalIgnoreCase)))
            {
                metadata = expectedAuthor;
            }

            var author = new Author { Metadata = metadata, CleanName = Parser.Parser.CleanAuthorName(metadata.Name), Series = new List<Series>() };
            var id = AudiobookId.ForAsin(region, resource.Asin);
            decimal.TryParse(resource.Rating, NumberStyles.Number, CultureInfo.InvariantCulture, out var rating);
            var releaseDate = CalendarDate.Normalize(resource.ReleaseDate);
            var edition = new Edition
            {
                ForeignEditionId = id,
                TitleSlug = "recording-" + region + "-" + resource.Asin,
                Asin = resource.Asin,
                AudibleRegion = region,
                Title = resource.Title,
                Subtitle = AudiobookTextNormalizer.Text(resource.Subtitle),
                Overview = resource.Description,
                Language = AudiobookTextNormalizer.Language(resource.Language),
                Format = "Audiobook",
                Disambiguation = AudiobookTextNormalizer.Text(resource.FormatType)?.ToLowerInvariant(),
                Publisher = AudiobookTextNormalizer.Publisher(resource.PublisherName),
                Isbn13 = resource.Isbn,
                ReleaseDate = releaseDate,
                RuntimeLengthMinutes = resource.RuntimeLengthMin > 0 ? resource.RuntimeLengthMin : null,
                Narrators = AudiobookTextNormalizer.Names(resource.Narrators?.Select(n => n?.Name)),
                Ratings = new Ratings { Value = rating },
                Monitored = true
            };
            AddImage(edition.Images, resource.Image, MediaCoverTypes.Cover);
            edition.Links.Add(new Links { Name = "Audible", Url = $"{StoreUrl(region)}/pd/{resource.Asin}" });
            var book = new Book
            {
                ForeignBookId = id,
                ForeignEditionId = id,
                TitleSlug = edition.TitleSlug,
                Title = resource.Title,
                CleanTitle = Parser.Parser.CleanBookTitle(resource.Title),
                ReleaseDate = releaseDate,
                Genres = AudiobookTextNormalizer.Names(resource.Genres?.Select(g => g?.Name)),
                Ratings = edition.Ratings,
                Author = author,
                AuthorMetadata = metadata,
                Editions = new List<Edition> { edition },
                SeriesLinks = new List<SeriesBookLink>()
            };
            edition.Book = book;
            AddSeries(book, resource.SeriesPrimary, region, true);
            AddSeries(book, resource.SeriesSecondary, region, false);
            author.Books = new List<Book> { book };
            author.Series = book.SeriesLinks.Value.Select(x => x.Series.Value).ToList();
            return book;
        }

        private static void AddSeries(Book book, AudnexusSeriesResource source, string region, bool primary)
        {
            if (source?.Name.IsNullOrWhiteSpace() != false)
            {
                return;
            }

            var series = new Series
            {
                ForeignSeriesId = AudiobookId.IsAsin(source.Asin) ? AudiobookId.ForAsin(region, source.Asin) : AudiobookId.ForName(region, source.Name),
                ForeignAuthorId = book.AuthorMetadata.Value.ForeignAuthorId,
                Title = AudiobookTextNormalizer.Text(source.Name),
                Numbered = source.Position.IsNotNullOrWhiteSpace(),
                Books = new List<Book> { book }
            };
            decimal.TryParse(source.Position, NumberStyles.Number, CultureInfo.InvariantCulture, out var position);
            var link = new SeriesBookLink
            {
                Book = book,
                Series = series,
                Position = AudiobookTextNormalizer.Position(source.Position),
                SeriesPosition = (int)Math.Clamp(position * 1000, 0, int.MaxValue),
                IsPrimary = primary
            };
            series.LinkItems = new List<SeriesBookLink> { link };
            book.SeriesLinks.Value.Add(link);
        }

        private static void AddImage(List<MediaCover.MediaCover> images, string value, MediaCoverTypes type)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            {
                images.Add(new MediaCover.MediaCover(type, value));
            }
        }

        private static string StoreUrl(string region)
        {
            var suffix = region switch { "us" => "com", "uk" => "co.uk", "au" => "com.au", "jp" => "co.jp", "in" => "in", _ => region };
            return "https://www.audible." + suffix;
        }
    }
}
