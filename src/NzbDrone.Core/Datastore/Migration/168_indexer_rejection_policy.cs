using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(168)]
    public class indexer_rejection_policy : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Indexers")
                .AddColumn("DangerousFileAction").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("ExecutableFileAction").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("UserRejectedExtensionAction").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
