using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Audiobook;
using Readarr.Api.V1.Author;
using Readarr.Http.REST;
using Swashbuckle.AspNetCore.Annotations;

namespace Readarr.Api.V1.Books
{
    public class BookResource : RestResource
    {
        public string Title { get; set; }
        public string DisplayTitle { get; set; }
        public string Subtitle { get; set; }
        public string DisplaySubtitle { get; set; }
        public string AuthorTitle { get; set; }
        public string SeriesTitle { get; set; }
        public string DisplaySeriesTitle { get; set; }
        public List<string> MetadataLabels { get; set; }
        public List<string> MetadataWarnings { get; set; }
        public string Disambiguation { get; set; }
        public string Overview { get; set; }
        public string Publisher { get; set; }
        public string Language { get; set; }
        public int AuthorId { get; set; }
        public string ForeignBookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string TitleSlug { get; set; }
        public bool Monitored { get; set; }
        public bool AnyEditionOk { get; set; }
        public Ratings Ratings { get; set; }
        [global::System.Text.Json.Serialization.JsonConverter(typeof(STJCalendarDateConverter))]
        public DateTime? ReleaseDate { get; set; }
        public int PageCount { get; set; }
        public List<string> Narrators { get; set; }
        public List<AudiobookContributor> Contributors { get; set; }
        public int? RuntimeLengthMinutes { get; set; }
        public long? RuntimeLengthMs { get; set; }
        public int ChapterCount { get; set; }
        public int? CopyrightYear { get; set; }
        public bool? IsAdult { get; set; }
        public string LiteratureType { get; set; }
        public DateTime? SourceReleaseDate { get; set; }
        public bool ReleaseDateIsPlaceholder { get; set; }
        public List<string> Genres { get; set; }
        public List<AudiobookClassification> Classifications { get; set; }
        public AuthorResource Author { get; set; }
        public List<MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public BookStatisticsResource Statistics { get; set; }
        public DateTime? Added { get; set; }
        public AddBookOptions AddOptions { get; set; }
        public string RemoteCover { get; set; }
        public DateTime? LastSearchTime { get; set; }
        public List<EditionResource> Editions { get; set; }

        //Hiding this so people don't think its usable (only used to set the initial state)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [SwaggerIgnore]
        public bool Grabbed { get; set; }
    }

    public static class BookResourceMapper
    {
        public static BookResource ToResource(this Book model, IAudiobookMetadataOverrideService overrides = null)
        {
            if (model == null)
            {
                return null;
            }

            var selectedEdition = model.Editions?.Value.Where(x => x.Monitored).SingleOrDefault();

            var title = selectedEdition?.Title ?? model.Title;
            var projection = overrides?.Project(model, selectedEdition) ??
                AudiobookMetadataCleanup.Project(model, selectedEdition, Array.Empty<AudiobookMetadataCleanupRule>());
            var authorTitle = $"{model.AuthorMetadata?.Value?.SortNameLastFirst} {title}";

            var seriesLinks = model.SeriesLinks?.Value?.OrderBy(x => x.SeriesPosition);
            var seriesTitle = seriesLinks?.Select(x => x?.Series?.Value?.Title + (x?.Position.IsNotNullOrWhiteSpace() ?? false ? $" #{x.Position}" : string.Empty)).ConcatToString("; ");

            return new BookResource
            {
                Id = model.Id,
                AuthorId = model.AuthorId,
                ForeignBookId = model.ForeignBookId,
                ForeignEditionId = selectedEdition?.ForeignEditionId,
                TitleSlug = model.TitleSlug,
                Monitored = model.Monitored,
                AnyEditionOk = model.AnyEditionOk,
                ReleaseDate = model.ReleaseDate,
                PageCount = selectedEdition?.PageCount ?? 0,
                Narrators = selectedEdition?.Narrators ?? new List<string>(),
                Contributors = selectedEdition?.Contributors ?? new List<AudiobookContributor>(),
                RuntimeLengthMinutes = selectedEdition?.RuntimeLengthMinutes,
                RuntimeLengthMs = selectedEdition?.ChapterMetadata?.RuntimeLengthMs,
                ChapterCount = selectedEdition?.ChapterMetadata?.Chapters?.Count ?? 0,
                CopyrightYear = selectedEdition?.CopyrightYear,
                IsAdult = selectedEdition?.IsAdult,
                LiteratureType = selectedEdition?.LiteratureType,
                Overview = selectedEdition?.Overview,
                Publisher = selectedEdition?.Publisher,
                Language = selectedEdition?.Language,
                SourceReleaseDate = selectedEdition?.SourceReleaseDate,
                ReleaseDateIsPlaceholder = selectedEdition?.ReleaseDateIsPlaceholder ?? false,
                Genres = model.Genres,
                Classifications = model.Classifications,
                Title = title,
                DisplayTitle = projection.DisplayTitle,
                Subtitle = selectedEdition?.Subtitle,
                DisplaySubtitle = projection.Subtitle,
                MetadataLabels = projection.MetadataLabels,
                MetadataWarnings = projection.Warnings,
                AuthorTitle = authorTitle,
                SeriesTitle = seriesTitle,
                DisplaySeriesTitle = projection.Series + (!string.IsNullOrWhiteSpace(projection.Series) && !string.IsNullOrWhiteSpace(projection.Position) ? " #" + projection.Position : string.Empty),
                Disambiguation = selectedEdition?.Disambiguation,
                Images = selectedEdition?.Images ?? new List<MediaCover>(),
                Links = model.Links.Concat(selectedEdition?.Links ?? new List<Links>()).ToList(),
                Ratings = selectedEdition?.Ratings ?? new Ratings(),
                Added = model.Added,
                LastSearchTime = model.LastSearchTime
            };
        }

        public static Book ToModel(this BookResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var author = resource.Author?.ToModel() ?? new NzbDrone.Core.Books.Author();

            return new Book
            {
                Id = resource.Id,
                ForeignBookId = resource.ForeignBookId,
                ForeignEditionId = resource.ForeignEditionId,
                TitleSlug = resource.TitleSlug,
                Title = resource.Title,
                Classifications = resource.Classifications ?? new List<AudiobookClassification>(),
                Monitored = resource.Monitored,
                AnyEditionOk = resource.AnyEditionOk,
                Editions = resource.Editions?.ToModel() ?? new List<Edition>(),
                AddOptions = resource.AddOptions,
                Author = author,
                AuthorMetadata = author.Metadata.Value
            };
        }

        public static Book ToModel(this BookResource resource, Book book)
        {
            var editions = book.Editions?.Value ?? new List<Edition>();
            var updates = new Dictionary<int, EditionResource>();
            if (resource.ForeignBookId.IsNotNullOrWhiteSpace() && resource.ForeignBookId != book.ForeignBookId)
            {
                throw new BadRequestException("The book's metadata identity cannot be changed.");
            }

            foreach (var editionResource in resource.Editions ?? new List<EditionResource>())
            {
                if (editionResource == null)
                {
                    throw new BadRequestException("An edition cannot be null.");
                }

                var edition = editions.SingleOrDefault(item => item.Id == editionResource.Id);
                if (edition == null || updates.ContainsKey(editionResource.Id) ||
                    (editionResource.BookId != 0 && editionResource.BookId != book.Id) ||
                    (editionResource.ForeignEditionId.IsNotNullOrWhiteSpace() && editionResource.ForeignEditionId != edition.ForeignEditionId) ||
                    (editionResource.Asin.IsNotNullOrWhiteSpace() && !string.Equals(editionResource.Asin, edition.Asin, StringComparison.OrdinalIgnoreCase)) ||
                    (editionResource.AudibleRegion.IsNotNullOrWhiteSpace() && !string.Equals(editionResource.AudibleRegion, edition.AudibleRegion, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BadRequestException("The edition must retain its identity and belong to this book.");
                }

                updates.Add(editionResource.Id, editionResource);
            }

            if (editions.Count(edition => updates.TryGetValue(edition.Id, out var update) ? update.Monitored : edition.Monitored) > 1)
            {
                throw new BadRequestException("Only one edition can be monitored for a book.");
            }

            var updatedBook = resource.ToModel();
            updatedBook.ForeignBookId = book.ForeignBookId;

            book.ApplyChanges(updatedBook);
            foreach (var edition in editions)
            {
                if (updates.TryGetValue(edition.Id, out var update))
                {
                    edition.ApplyChanges(new Edition { ForeignEditionId = edition.ForeignEditionId, Monitored = update.Monitored });
                }
            }

            book.Editions = editions;

            return book;
        }

        public static List<BookResource> ToResource(this IEnumerable<Book> models, IAudiobookMetadataOverrideService overrides = null)
        {
            return models?.Select(model => model.ToResource(overrides)).ToList();
        }

        public static List<Book> ToModel(this IEnumerable<BookResource> resources)
        {
            return resources.Select(ToModel).ToList();
        }
    }
}
