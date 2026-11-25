using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.Qualities
{
    public class QualityRankingChangedEvent : IEvent
    {
        public QualityRankingChangedEvent(bool authorAssignmentsChanged = false)
        {
            AuthorAssignmentsChanged = authorAssignmentsChanged;
        }

        public bool AuthorAssignmentsChanged { get; }
    }
}
