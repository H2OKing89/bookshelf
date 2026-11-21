using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(161)]
    public class add_audio_format_evidence : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Blocklist").AddColumn("AudioFormatEvidence").AsString(int.MaxValue).Nullable();
        }
    }
}
