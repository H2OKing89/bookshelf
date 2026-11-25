using System.Data;
using FluentMigrator;
using Npgsql;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(165)]
    public class import_list_membership : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportLists").AddColumn("MembershipRevision").AsInt64().NotNullable().WithDefaultValue(0);
            Create.Table("ImportListMembershipSnapshots")
                .WithColumn("ImportListId").AsInt32().PrimaryKey().ForeignKey("ImportLists", "Id").OnDelete(Rule.Cascade)
                .WithColumn("AttemptToken").AsString(32).NotNullable()
                .WithColumn("SuccessfulRevision").AsInt64().Nullable()
                .WithColumn("LastSuccessfulSync").AsDateTime().Nullable();
            Create.Table("ImportListMembershipItems")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("ImportListId").AsInt32().NotNullable().Indexed().ForeignKey("ImportLists", "Id").OnDelete(Rule.Cascade)
                .WithColumn("SourceAuthor").AsString(int.MaxValue).Nullable()
                .WithColumn("SourceTitle").AsString(int.MaxValue).Nullable()
                .WithColumn("SourceAuthorId").AsString(int.MaxValue).Nullable()
                .WithColumn("SourceBookId").AsString(int.MaxValue).Nullable()
                .WithColumn("SourceEditionId").AsString(int.MaxValue).Nullable()
                .WithColumn("AuthorId").AsString(int.MaxValue).Nullable()
                .WithColumn("BookId").AsString(int.MaxValue).Nullable()
                .WithColumn("EditionId").AsString(int.MaxValue).Nullable();

            // Import-list settings may be written by bulk edits or provider
            // actions. The database revision covers every such update.
            IfDatabase("sqlite").Execute.WithConnection((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TRIGGER "AdvanceImportListMembershipRevision"
                    AFTER UPDATE OF "Name", "EnableAutomaticAdd", "Implementation", "ConfigContract", "Settings",
                        "ShouldMonitor", "ShouldMonitorExisting", "ShouldSearch", "MonitorNewItems", "ProfileId", "MetadataProfileId", "RootFolderPath", "Tags"
                    ON "ImportLists"
                    BEGIN
                        UPDATE "ImportLists" SET "MembershipRevision" = OLD."MembershipRevision" + 1 WHERE "Id" = OLD."Id";
                    END;
                    CREATE TRIGGER "RemoveDeletedImportListMembership"
                    AFTER DELETE ON "ImportLists"
                    BEGIN
                        DELETE FROM "ImportListMembershipItems" WHERE "ImportListId" = OLD."Id";
                        DELETE FROM "ImportListMembershipSnapshots" WHERE "ImportListId" = OLD."Id";
                    END;
                    """;
                command.ExecuteNonQuery();
            });
            Execute.WithConnection((connection, transaction) =>
            {
                if (connection is not NpgsqlConnection)
                {
                    return;
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE FUNCTION advance_import_list_membership_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        NEW."MembershipRevision" := OLD."MembershipRevision" + 1;
                        RETURN NEW;
                    END;
                    $$;
                    CREATE TRIGGER "AdvanceImportListMembershipRevision" BEFORE UPDATE ON "ImportLists"
                    FOR EACH ROW EXECUTE FUNCTION advance_import_list_membership_revision();
                    """;
                command.ExecuteNonQuery();
            });
        }
    }
}
