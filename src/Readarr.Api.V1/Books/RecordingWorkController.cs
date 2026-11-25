using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using Readarr.Http;

namespace Readarr.Api.V1.Books
{
    [V1ApiController("recordingwork")]
    public class RecordingWorkController : Controller
    {
        private readonly IRecordingWorkRepository _repository;

        public RecordingWorkController(IRecordingWorkRepository repository)
        {
            _repository = repository;
        }

        [HttpGet("book/{bookId:int}")]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public List<RecordingWorkRecording> Recordings(int bookId)
        {
            return _repository.ForBook(bookId);
        }

        [HttpGet("works")]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public List<RecordingWork> Works(string term = null)
        {
            return _repository.FindWorks(term);
        }

        [HttpGet("{editionId:int}")]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public RecordingWorkSnapshot Inspect(int editionId)
        {
            return _repository.Inspect(editionId);
        }

        [HttpPut("{editionId:int}")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public RecordingWorkSnapshot Save(int editionId, [FromBody] RecordingWorkEdit edit)
        {
            return _repository.Save(editionId, edit);
        }
    }
}
