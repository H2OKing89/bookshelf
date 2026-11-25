using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using Readarr.Api.V1.Books;
using Readarr.Http;

namespace Readarr.Api.V1.LocalRecordings
{
    [V1ApiController("localrecording")]
    public class LocalRecordingController : Controller
    {
        private readonly ILocalRecordingService _recordings;

        public LocalRecordingController(ILocalRecordingService recordings)
        {
            _recordings = recordings;
        }

        [HttpGet]
        public LocalRecording Preview([FromQuery] List<int> fileIds) => _recordings.Preview(fileIds);

        [HttpPost]
        public BookResource Create([FromBody] LocalRecording recording) => _recordings.Create(recording).ToResource();

        [HttpGet("{bookId:int}")]
        public LocalRecording Get(int bookId) => _recordings.Get(bookId);

        [HttpPut("{bookId:int}")]
        public BookResource Update(int bookId, [FromBody] LocalRecording recording) => _recordings.Update(bookId, recording).ToResource();
    }
}
