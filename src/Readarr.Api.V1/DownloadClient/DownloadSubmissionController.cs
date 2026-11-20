using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using Readarr.Http;

namespace Readarr.Api.V1.DownloadClient
{
    [V1ApiController("downloadsubmission")]
    public class DownloadSubmissionController : Controller
    {
        private readonly IDownloadSubmissionGuard _guard;

        public DownloadSubmissionController(IDownloadSubmissionGuard guard)
        {
            _guard = guard;
        }

        [HttpGet]
        public List<DownloadSubmissionHold> GetPending()
        {
            return _guard.GetPending();
        }

        [HttpPost("acknowledge")]
        public IActionResult Acknowledge([FromBody] DownloadSubmissionAcknowledgement resource)
        {
            if (resource == null || !resource.ClientReviewed || resource.DownloadClientId <= 0 ||
                !Guid.TryParseExact(resource.AttemptId, "N", out _))
            {
                return BadRequest(new { Message = "Review the specified download client before clearing its submission hold." });
            }

            if (!_guard.Acknowledge(resource.AttemptId, resource.DownloadClientId))
            {
                return Conflict(new { Message = "This submission is still in progress, has already been reviewed, or belongs to another client. Refresh the holds." });
            }

            return NoContent();
        }
    }

    public class DownloadSubmissionAcknowledgement
    {
        public string AttemptId { get; set; }
        public int DownloadClientId { get; set; }
        public bool ClientReviewed { get; set; }
    }
}
