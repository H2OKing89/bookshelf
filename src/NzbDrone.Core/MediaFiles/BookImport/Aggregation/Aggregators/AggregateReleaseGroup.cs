using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators
{
    public class AggregateReleaseGroup : IAggregate<LocalBook>
    {
        public LocalBook Aggregate(LocalBook item, bool otherFiles)
        {
            ReleaseGroupResolution.Apply(item);
            return item;
        }
    }
}
