using System;
using FluentMigrator;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(046)]
    public class audiobook_metadata_cleanup : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AudiobookMetadataOverrides").AddColumn("OriginalSubtitle").AsString().Nullable();
            Create.Table("AudiobookMetadataCleanupRules")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Phrase").AsString().NotNullable()
                .WithColumn("Labels").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("Enabled").AsBoolean().NotNullable()
                .WithColumn("Reason").AsString().NotNullable()
                .WithColumn("UpdatedAt").AsDateTime().NotNullable();

            foreach (var item in AudiobookMetadataCleanupRuleSeed.Read())
            {
                Insert.IntoTable("AudiobookMetadataCleanupRules").Row(new
                {
                    item.Phrase, Labels = item.Labels.ToJson(), Enabled = false, item.Reason, UpdatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
