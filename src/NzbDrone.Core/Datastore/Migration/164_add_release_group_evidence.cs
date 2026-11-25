using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(164)]
    public class add_release_group_evidence : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Do not reconstruct old acquisition provenance from renamed files.
            Alter.Table("BookFiles").AddColumn("ReleaseGroupEvidence").AsString(int.MaxValue).Nullable();
        }
    }
}
