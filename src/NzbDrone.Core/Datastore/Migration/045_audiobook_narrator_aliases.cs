using System;
using FluentMigrator;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(045)]
    public class audiobook_narrator_aliases : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Narrators have verified names rather than recording/author ASINs.
            Alter.Table("AudiobookMetadataOverrides").AlterColumn("Asin").AsString().Nullable();
            foreach (var item in AudiobookMetadataOverrideSeed.Read("narrators-045.json"))
            {
                Insert.IntoTable("AudiobookMetadataOverrides").Row(new
                {
                    item.Kind, item.Region, item.Asin, item.Name,
                    AlternateAsins = item.AlternateAsins.ToJson(), Aliases = item.Aliases.ToJson(),
                    item.OriginalTitle, item.ReplacementTitle, item.SourceUrl, item.Reason, item.Enabled,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
