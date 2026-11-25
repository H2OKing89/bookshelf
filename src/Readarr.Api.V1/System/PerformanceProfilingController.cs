using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Instrumentation;
using Readarr.Http;

namespace Readarr.Api.V1.System
{
    [Authorize(Policy = PerformanceProfiler.AuthorizationPolicy)]
    [V1ApiController("system/performance")]
    public class PerformanceProfilingController : Controller
    {
        private readonly IPerformanceProfiler _profiler;

        public PerformanceProfilingController(IPerformanceProfiler profiler)
        {
            _profiler = profiler;
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public PerformanceProfileSnapshot Get() => _profiler.Snapshot();
    }
}
