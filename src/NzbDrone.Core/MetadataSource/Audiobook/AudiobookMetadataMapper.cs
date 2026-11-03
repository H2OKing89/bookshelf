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
        public static AuthorMetadata Author(AudnexusContributorResource contributor, string region)
        {
            var id = AudiobookId.IsAsin(contributor.Asin)
                ? AudiobookId.ForAsin(region, contributor.Asin)
                : AudiobookId.ForName(region, contributor.Name);
            var profile = contributor as AudnexusAuthorResource;
            var result = new AuthorMetadata
            {
                ForeignAuthorId = id,
                TitleSlug = "author-" + region + "-" + (AudiobookId.IsAsin(contributor.Asin) ? contributor.Asin :
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contributor.Name))).Substring(0, 16).ToLowerInvariant()),
                Name = contributor.Name,
                SortName = contributor.Name,
                NameLastFirst = contributor.Name,
                SortNameLastFirst = contributor.Name,
                Overview = profile?.Description,
                Status = AuthorStatusType.Continuing
            };
            AddImage(result.Images, profile?.Image, MediaCoverTypes.Poster);
            if (AudiobookId.IsAsin(contributor.Asin))
            {
                result.Links.Add(new Links { Name = "Audible", Url = $"{StoreUrl(region)}/author/{contributor.Asin}" });
            }

            return result;
        }

        public static Book Book(AudnexusBookResource resource, string region)
        {
            if (!AudiobookId.IsAsin(resource?.Asin) || resource.Region != region || resource.Title.IsNullOrWhiteSpace() ||
                resource.Authors?.Any(a => a?.Name.IsNotNullOrWhiteSpace() == true) != true)
            {
                throw new BookInfoException("Audiobook metadata is missing its ASIN, marketplace, title, or author.");
            }

            var metadata = Author(resource.Authors.First(a => a?.Name.IsNotNullOrWhiteSpace() == true), region);
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
                Subtitle = resource.Subtitle,
                Overview = resource.Description,
                Language = resource.Language,
                Format = "Audiobook",
                Disambiguation = resource.FormatType,
                Publisher = resource.PublisherName,
                Isbn13 = resource.Isbn,
                ReleaseDate = releaseDate,
                RuntimeLengthMinutes = resource.RuntimeLengthMin > 0 ? resource.RuntimeLengthMin : null,
                Narrators = resource.Narrators?.Where(n => n?.Name.IsNotNullOrWhiteSpace() == true).Select(n => n.Name.Trim()).Distinct().ToList() ?? new List<string>(),
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
                Genres = resource.Genres?.Where(g => g?.Name.IsNotNullOrWhiteSpace() == true).Select(g => g.Name).Distinct().ToList() ?? new List<string>(),
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
                Title = source.Name,
                Numbered = source.Position.IsNotNullOrWhiteSpace(),
                Books = new List<Book> { book }
            };
            decimal.TryParse(source.Position, NumberStyles.Number, CultureInfo.InvariantCulture, out var position);
            var link = new SeriesBookLink
            {
                Book = book,
                Series = series,
                Position = source.Position,
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
