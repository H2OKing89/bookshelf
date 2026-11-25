using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(162)]
    public class add_local_recording_provenance : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Older local recordings have unknown provenance until explicitly edited.
            // The edit token belongs to the local metadata workflow, not general
            // edition monitoring updates or provider refreshes.
            Alter.Table("Editions").AddColumn("LocalMetadataRevision").AsString(32).Nullable();
            Alter.Table("Editions").AddColumn("LocalMetadataProvenance").AsString(int.MaxValue).Nullable();
        }
    }
}
