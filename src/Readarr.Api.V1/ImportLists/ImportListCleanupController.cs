using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ImportLists;
using Readarr.Http;

namespace Readarr.Api.V1.ImportLists
{
    [V1ApiController("importlistcleanup")]
    public class ImportListCleanupController : Controller
    {
        private readonly IImportListPolicyRepository _repository;

        public ImportListCleanupController(IImportListPolicyRepository repository)
        {
            _repository = repository;
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public ImportListCleanupPreview Get([FromQuery] ImportListCleanupMode mode = ImportListCleanupMode.Preview, [FromQuery] int? tagId = null) =>
            _repository.Preview(mode, tagId);

        [HttpPost]
        public ImportListCleanupPreview Apply([FromBody] ImportListCleanupRequest request) => _repository.Apply(request);
    }
}
