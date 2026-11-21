using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(159)]
    public class AddMinimumUpgradeFormatScore : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles")
                .AddColumn("MinUpgradeFormatScore").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
