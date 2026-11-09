using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(047)]
    public class audiobook_metadata_completeness : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Books")
                .AddColumn("Classifications").AsString().NotNullable().WithDefaultValue("[]");

            Alter.Table("Editions")
                .AddColumn("Contributors").AsString().NotNullable().WithDefaultValue("[]")
                .AddColumn("ChapterMetadata").AsString().Nullable()
                .AddColumn("CopyrightYear").AsInt32().Nullable()
                .AddColumn("IsAdult").AsBoolean().Nullable()
                .AddColumn("LiteratureType").AsString().Nullable()
                .AddColumn("SourceReleaseDate").AsDateTimeOffset().Nullable()
                .AddColumn("ReleaseDateIsPlaceholder").AsBoolean().NotNullable().WithDefaultValue(false);

            Alter.Table("AuthorMetadata")
                .AddColumn("SimilarAuthors").AsString().NotNullable().WithDefaultValue("[]");

            Execute.Sql(@"UPDATE ""Editions""
                          SET ""Language"" = 'eng'
                          WHERE ""ForeignEditionId"" LIKE 'audible:%'
                            AND LOWER(TRIM(""Language"")) IN ('eng', 'english')");

            // Preserve the original provider value before excluding its known placeholder from calendar dates.
            Execute.Sql(@"UPDATE ""Editions""
                          SET ""SourceReleaseDate"" = ""ReleaseDate"", ""ReleaseDateIsPlaceholder"" = TRUE,
                              ""ReleaseDate"" = NULL
                          WHERE ""ForeignEditionId"" LIKE 'audible:%'
                            AND CAST(""ReleaseDate"" AS TEXT) LIKE '2050-12-30%'");

            Execute.Sql(@"UPDATE ""Books""
                          SET ""ReleaseDate"" = NULL
                          WHERE ""ForeignBookId"" LIKE 'audible:%'
                            AND CAST(""ReleaseDate"" AS TEXT) LIKE '2050-12-30%'
                            AND EXISTS (SELECT 1 FROM ""Editions""
                                        WHERE ""Editions"".""BookId"" = ""Books"".""Id""
                                          AND ""Editions"".""ReleaseDateIsPlaceholder"" = TRUE
                                          AND ""Editions"".""SourceReleaseDate"" = ""Books"".""ReleaseDate"")");
        }
    }
}
