using System;
using FluentMigrator;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.MetadataSource.Audiobook;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(044)]
    public class audiobook_metadata_overrides : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.Table("AudiobookMetadataOverrides")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Kind").AsString().NotNullable()
                .WithColumn("Region").AsString().NotNullable()
                .WithColumn("Asin").AsString().NotNullable()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("AlternateAsins").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("Aliases").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("OriginalTitle").AsString().Nullable()
                .WithColumn("ReplacementTitle").AsString().Nullable()
                .WithColumn("SourceUrl").AsString().NotNullable()
                .WithColumn("Reason").AsString().NotNullable()
                .WithColumn("Enabled").AsBoolean().NotNullable()
                .WithColumn("UpdatedAt").AsDateTime().NotNullable();

            Create.Index("IX_AudiobookMetadataOverrides_Identity").OnTable("AudiobookMetadataOverrides")
                .OnColumn("Kind").Ascending().OnColumn("Region").Ascending().OnColumn("Asin").Ascending().WithOptions().Unique();

            foreach (var item in AudiobookMetadataOverrideSeed.Read())
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
