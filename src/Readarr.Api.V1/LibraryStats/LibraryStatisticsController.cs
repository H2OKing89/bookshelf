using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.LibraryStats;
using Readarr.Http;

namespace Readarr.Api.V1.LibraryStats
{
    [V1ApiController("librarystatistics")]
    public class LibraryStatisticsController : Controller
    {
        private readonly ILibraryStatisticsService _statistics;

        public LibraryStatisticsController(ILibraryStatisticsService statistics)
        {
            _statistics = statistics;
        }

        [HttpGet]
        [Produces("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public LibraryStatistics Get(int? authorId = null, string media = "all", string source = "all")
        {
            return _statistics.GetStatistics(authorId, media, source);
        }
    }
}
