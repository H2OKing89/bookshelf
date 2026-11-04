using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.MetadataOverrides
{
    public class MetadataOverrideResource : RestResource
    {
        public string Kind { get; set; }
        public string Region { get; set; }
        public string Asin { get; set; }
        public string Name { get; set; }
        public List<string> AlternateAsins { get; set; }
        public List<string> Aliases { get; set; }
        public string OriginalTitle { get; set; }
        public string OriginalSubtitle { get; set; }
        public string ReplacementTitle { get; set; }
        public string SourceUrl { get; set; }
        public string Reason { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime UpdatedAt { get; set; }
        public string ObservedTitle { get; set; }
        public string ObservedSubtitle { get; set; }
        public string Status { get; set; }
    }

    [V1ApiController("metadataoverride")]
    public class MetadataOverrideController : Controller
    {
        private readonly IAudiobookMetadataOverrideService _overrides;
        private readonly IBookService _books;

        public MetadataOverrideController(IAudiobookMetadataOverrideService overrides, IBookService books)
        {
            _overrides = overrides;
            _books = books;
        }

        [HttpGet]
        public List<MetadataOverrideResource> All() => _overrides.All().Select(ToResource).ToList();

        [HttpGet("{id:int}")]
        public ActionResult<MetadataOverrideResource> Get(int id)
        {
            var item = _overrides.Find(id);
            return item == null ? NotFound() : ToResource(item);
        }

        [HttpPost]
        public ActionResult<MetadataOverrideResource> Add([FromBody] MetadataOverrideResource resource)
        {
            if (resource == null)
            {
                return BadRequest();
            }

            resource.Id = 0;
            var item = _overrides.Save(ToModel(resource));
            return CreatedAtAction(nameof(Get), new { id = item.Id }, ToResource(item));
        }

        [HttpPut("{id:int}")]
        public ActionResult<MetadataOverrideResource> Update(int id, [FromBody] MetadataOverrideResource resource)
        {
            if (resource == null)
            {
                return BadRequest();
            }

            if (_overrides.Find(id) == null)
            {
                return NotFound();
            }

            resource.Id = id;
            return ToResource(_overrides.Save(ToModel(resource)));
        }

        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id)
        {
            if (_overrides.Find(id) == null)
            {
                return NotFound();
            }

            _overrides.Delete(id);
            return NoContent();
        }

        private MetadataOverrideResource ToResource(AudiobookMetadataOverride item)
        {
            var recordingOverride = item.Kind == "title" || item.Kind == "titleprotection";
            var book = recordingOverride ? _books.FindById(AudiobookId.ForAsin(item.Region, item.Asin)) : null;
            var edition = book?.Editions?.Value?.SingleOrDefault(value => value.Monitored);
            var observed = edition?.Title ?? book?.Title;
            var observedSubtitle = edition?.Subtitle;
            var needsReview = item.Kind == "title" ? observed != item.OriginalTitle :
                (item.OriginalTitle != null && observed != item.OriginalTitle) ||
                (item.OriginalSubtitle != null && observedSubtitle != item.OriginalSubtitle);
            return new MetadataOverrideResource
            {
                Id = item.Id, Kind = item.Kind, Region = item.Region, Asin = item.Asin, Name = item.Name,
                AlternateAsins = item.AlternateAsins, Aliases = item.Aliases,
                OriginalTitle = item.OriginalTitle, OriginalSubtitle = item.OriginalSubtitle, ReplacementTitle = item.ReplacementTitle,
                SourceUrl = item.SourceUrl, Reason = item.Reason, Enabled = item.Enabled, UpdatedAt = item.UpdatedAt,
                ObservedTitle = observed, ObservedSubtitle = observedSubtitle,
                Status = !item.Enabled ? "disabled" : !recordingOverride ? "enabled" : book == null ? "notInLibrary" :
                    needsReview ? "needsReview" : "enabled"
            };
        }

        private static AudiobookMetadataOverride ToModel(MetadataOverrideResource item) => new AudiobookMetadataOverride
        {
            Id = item.Id, Kind = item.Kind, Region = item.Region, Asin = item.Asin, Name = item.Name,
            AlternateAsins = item.AlternateAsins, Aliases = item.Aliases,
            OriginalTitle = item.OriginalTitle, OriginalSubtitle = item.OriginalSubtitle, ReplacementTitle = item.ReplacementTitle,
            SourceUrl = item.SourceUrl, Reason = item.Reason, Enabled = item.Enabled
        };
    }
}
