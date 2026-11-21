using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;
using Readarr.Http;

namespace Readarr.Api.V1.Metadata
{
    [V1ApiController("metadataproviderstatus")]
    public class MetadataProviderStatusController : Controller
    {
        private readonly IMetadataProviderStatusService _service;

        public MetadataProviderStatusController(IMetadataProviderStatusService service)
        {
            _service = service;
        }

        [HttpGet]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public List<MetadataProviderStatus> GetStatus()
        {
            return _service.GetStatus();
        }
    }
}
