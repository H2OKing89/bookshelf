using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class audiobook_edition_metadata : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Editions")
                .AddColumn("AudibleRegion").AsString().Nullable()
                .AddColumn("Narrators").AsString().NotNullable().WithDefaultValue("[]")
                .AddColumn("RuntimeLengthMinutes").AsInt32().Nullable();
        }
    }
}
