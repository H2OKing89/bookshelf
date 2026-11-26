using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.Search
{
    public class SingleBookSearchMatchSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public SingleBookSearchMatchSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria == null)
            {
                return Decision.Accept();
            }

            var singleBookSpec = searchCriteria as BookSearchCriteria;
            if (singleBookSpec == null)
            {
                return Decision.Accept();
            }

            if (!subject.ParsedBookInfo.BookTitle.Any())
            {
                _logger.Debug("Full discography result during single book search, skipping.");
                return Decision.RejectCode(RejectionCode.FullAuthorPack, "Full author pack");
            }

            return Decision.Accept();
        }
    }
}
