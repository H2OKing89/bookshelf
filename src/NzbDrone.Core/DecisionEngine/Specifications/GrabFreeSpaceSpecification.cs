using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class GrabFreeSpaceSpecification : IDecisionEngineSpecification
    {
        private readonly IDownloadGrabSpaceService _grabSpaceService;

        public GrabFreeSpaceSpecification(IDownloadGrabSpaceService grabSpaceService)
        {
            _grabSpaceService = grabSpaceService;
        }

        public SpecificationPriority Priority => SpecificationPriority.Disk;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            return _grabSpaceService.Preview(subject);
        }
    }
}
