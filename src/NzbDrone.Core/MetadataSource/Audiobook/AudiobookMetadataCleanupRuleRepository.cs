using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public interface IAudiobookMetadataCleanupRuleRepository : IBasicRepository<AudiobookMetadataCleanupRule>
    {
    }

    public class AudiobookMetadataCleanupRuleRepository : BasicRepository<AudiobookMetadataCleanupRule>, IAudiobookMetadataCleanupRuleRepository
    {
        public AudiobookMetadataCleanupRuleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
