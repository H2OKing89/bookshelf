using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.RecordingReview;
using Readarr.Http;

namespace Readarr.Api.V1.BookFiles
{
    [V1ApiController("bookfile")]
    public class RecordingReviewController : Controller
    {
        private readonly IRecordingReviewService _review;

        public RecordingReviewController(IRecordingReviewService review)
        {
            _review = review;
        }

        [HttpGet("{id:int}/recordingreview")]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public RecordingReviewResult Inspect(int id)
        {
            return _review.Inspect(id);
        }

        [HttpPut("{id:int}/recordingreview")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public RecordingReviewResult Associate(int id, [FromBody] RecordingAssociationResource association)
        {
            if (association == null)
            {
                throw new BadRequestException("An edition and its association preview are required.");
            }

            return _review.Associate(id, association.EditionId, association.Revision);
        }
    }

    public class RecordingAssociationResource
    {
        public int EditionId { get; set; }
        public string Revision { get; set; }
    }
}
