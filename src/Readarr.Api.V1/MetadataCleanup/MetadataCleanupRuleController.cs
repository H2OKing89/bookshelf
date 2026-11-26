using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource.Audiobook;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.MetadataCleanup
{
    public class MetadataCleanupRuleResource : RestResource
    {
        public string Phrase { get; set; }
        public List<string> Labels { get; set; } = new List<string>();
        public bool Enabled { get; set; } = true;
        public string Reason { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    [V1ApiController("metadatacleanuprule")]
    public class MetadataCleanupRuleController : Controller
    {
        private readonly IAudiobookMetadataCleanupRuleService _rules;

        public MetadataCleanupRuleController(IAudiobookMetadataCleanupRuleService rules)
        {
            _rules = rules;
        }

        [HttpGet]
        public List<MetadataCleanupRuleResource> All() => _rules.All().Select(ToResource).ToList();

        [HttpGet("{id:int}")]
        public ActionResult<MetadataCleanupRuleResource> Get(int id)
        {
            var item = _rules.Find(id);
            return item == null ? NotFound() : ToResource(item);
        }

        [HttpPost]
        public ActionResult<MetadataCleanupRuleResource> Add([FromBody] MetadataCleanupRuleResource resource)
        {
            if (resource == null)
            {
                return BadRequest();
            }

            resource.Id = 0;
            var item = _rules.Save(ToModel(resource));
            return CreatedAtAction(nameof(Get), new { id = item.Id }, ToResource(item));
        }

        [HttpPut("{id:int}")]
        public ActionResult<MetadataCleanupRuleResource> Update(int id, [FromBody] MetadataCleanupRuleResource resource)
        {
            if (resource == null)
            {
                return BadRequest();
            }

            if (_rules.Find(id) == null)
            {
                return NotFound();
            }

            resource.Id = id;
            return ToResource(_rules.Save(ToModel(resource)));
        }

        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id)
        {
            if (_rules.Find(id) == null)
            {
                return NotFound();
            }

            _rules.Delete(id);
            return NoContent();
        }

        public static AudiobookMetadataCleanupRule ToModel(MetadataCleanupRuleResource item) => item == null ? null : new AudiobookMetadataCleanupRule { Id = item.Id, Phrase = item.Phrase, Labels = item.Labels, Enabled = item.Enabled, Reason = item.Reason };

        private static MetadataCleanupRuleResource ToResource(AudiobookMetadataCleanupRule item) => new MetadataCleanupRuleResource
        {
            Id = item.Id, Phrase = item.Phrase, Labels = item.Labels, Enabled = item.Enabled, Reason = item.Reason, UpdatedAt = item.UpdatedAt
        };
    }
}
