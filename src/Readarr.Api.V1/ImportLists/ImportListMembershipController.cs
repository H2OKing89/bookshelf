using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ImportLists;
using Readarr.Http;

namespace Readarr.Api.V1.ImportLists
{
    [V1ApiController("importlistmembership")]
    public class ImportListMembershipController : Controller
    {
        private readonly IImportListMembershipRepository _repository;

        public ImportListMembershipController(IImportListMembershipRepository repository)
        {
            _repository = repository;
        }

        [HttpGet("{listId:int}")]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public ImportListMembershipSnapshot Get(int listId) => _repository.Get(listId);
    }
}
