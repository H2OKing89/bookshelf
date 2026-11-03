using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public interface IAudiobookMetadataOverrideRepository : IBasicRepository<AudiobookMetadataOverride>
    {
    }

    public class AudiobookMetadataOverrideRepository : BasicRepository<AudiobookMetadataOverride>, IAudiobookMetadataOverrideRepository
    {
        public AudiobookMetadataOverrideRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
