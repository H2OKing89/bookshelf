using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using Readarr.Http;

namespace Readarr.Api.V1.MetadataCleanup
{
    public class MetadataCleanupPreviewRequest
    {
        public List<MetadataCleanupRuleResource> Rules { get; set; }
        public List<int> BookIds { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class MetadataCleanupPreviewItem
    {
        public int BookId { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string SeriesTitle { get; set; }
        public string DisplayTitle { get; set; }
        public string DisplaySubtitle { get; set; }
        public string DisplaySeriesTitle { get; set; }
        public List<string> MetadataLabels { get; set; }
        public List<AudiobookMetadataCleanupChange> Changes { get; set; }
        public List<string> Warnings { get; set; }
    }

    public class MetadataCleanupPreviewResource
    {
        public int Total { get; set; }
        public int Changed { get; set; }
        public int Affected { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<MetadataCleanupPreviewItem> Items { get; set; }
    }

    [V1ApiController("metadatacleanup")]
    public class MetadataCleanupPreviewController : Controller
    {
        private readonly IBookService _books;
        private readonly IAudiobookMetadataOverrideService _overrides;

        public MetadataCleanupPreviewController(IBookService books, IAudiobookMetadataOverrideService overrides)
        {
            _books = books;
            _overrides = overrides;
        }

        [HttpPost("preview")]
        public ActionResult<MetadataCleanupPreviewResource> Preview([FromBody] MetadataCleanupPreviewRequest request)
        {
            if (request == null)
            {
                return BadRequest();
            }

            var errors = new List<ValidationFailure>();
            if (request.Page < 1)
            {
                errors.Add(new ValidationFailure("page", "Page must be at least 1."));
            }

            if (request.PageSize < 1 || request.PageSize > 200)
            {
                errors.Add(new ValidationFailure("pageSize", "Page size must be between 1 and 200."));
            }

            if (request.BookIds?.Any(id => id < 1) == true)
            {
                errors.Add(new ValidationFailure("bookIds", "Book IDs must be positive."));
            }

            if (errors.Any())
            {
                throw new ValidationException(errors);
            }

            var rules = request.Rules == null ? null : AudiobookMetadataCleanupRuleValidation.NormalizeAndValidate(request.Rules.Select(MetadataCleanupRuleController.ToModel));
            var books = (request.BookIds == null ? _books.GetAllBooks() : _books.GetBooks(request.BookIds.Distinct())).OrderBy(book => book.Id).ToList();
            var affected = new List<MetadataCleanupPreviewItem>();
            var changed = 0;
            foreach (var book in books)
            {
                var edition = book.Editions?.Value?.SingleOrDefault(value => value.Monitored);
                var projection = _overrides.Project(book, edition, rules);
                if (projection.Changes.Any())
                {
                    changed++;
                }

                if (!projection.Changes.Any() && !projection.Warnings.Any())
                {
                    continue;
                }

                var series = book.SeriesLinks?.Value?.OrderByDescending(link => link.IsPrimary).ThenBy(link => link.SeriesPosition).FirstOrDefault();
                affected.Add(new MetadataCleanupPreviewItem
                {
                    BookId = book.Id, Title = edition?.Title ?? book.Title ?? string.Empty, Subtitle = edition?.Subtitle ?? string.Empty,
                    SeriesTitle = series?.Series?.Value?.Title ?? string.Empty,
                    DisplayTitle = projection.DisplayTitle, DisplaySubtitle = projection.Subtitle, DisplaySeriesTitle = projection.Series,
                    MetadataLabels = projection.MetadataLabels, Changes = projection.Changes, Warnings = projection.Warnings
                });
            }

            return new MetadataCleanupPreviewResource
            {
                Total = books.Count, Changed = changed, Affected = affected.Count, Page = request.Page, PageSize = request.PageSize,
                Items = affected.Skip((int)global::System.Math.Min((long)(request.Page - 1) * request.PageSize, int.MaxValue)).Take(request.PageSize).ToList()
            };
        }
    }
}
