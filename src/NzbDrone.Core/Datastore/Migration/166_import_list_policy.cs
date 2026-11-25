using System.Data;
using FluentMigrator;
using Npgsql;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(166)]
    public class import_list_policy : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportLists").AddColumn("TagExisting").AsInt32().NotNullable().WithDefaultValue(0);

            // Only an insertion can establish list origin. Existing books never
            // obtain cleanup authority merely because they appeared on a list.
            Alter.Table("Books").AddColumn("ImportListOriginId").AsInt32().Nullable();
            Create.Table("ImportListTagOwners")
                .WithColumn("AuthorId").AsInt32().PrimaryKey().ForeignKey("Authors", "Id").OnDelete(Rule.Cascade)
                .WithColumn("TagId").AsInt32().PrimaryKey()
                .WithColumn("ImportListId").AsInt32().PrimaryKey().Indexed();
            Create.Table("ImportListManagedRecordings")
                .WithColumn("BookId").AsInt32().PrimaryKey().ForeignKey("Books", "Id").OnDelete(Rule.Cascade)
                .WithColumn("ForeignBookId").AsString(int.MaxValue).NotNullable()
                .WithColumn("AuthorMetadataId").AsInt32().NotNullable()
                .WithColumn("ImportListId").AsInt32().NotNullable();

            // Do not cascade list deletion into claimed tags or recordings: a
            // deleted definition is not a successful empty provider response.
            Execute.WithConnection((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = connection is NpgsqlConnection ? PostgreSql : Sqlite;
                command.ExecuteNonQuery();
            });
        }

        private const string Sqlite = """
            DROP TRIGGER "AdvanceImportListMembershipRevision";
            CREATE TRIGGER "AdvanceImportListMembershipRevision"
            AFTER UPDATE OF "Name", "EnableAutomaticAdd", "Implementation", "ConfigContract", "Settings",
                "ShouldMonitor", "ShouldMonitorExisting", "ShouldSearch", "MonitorNewItems", "ProfileId", "MetadataProfileId", "RootFolderPath", "Tags", "TagExisting"
            ON "ImportLists"
            BEGIN
                UPDATE "ImportLists" SET "MembershipRevision" = OLD."MembershipRevision" + 1 WHERE "Id" = OLD."Id";
            END;
            CREATE TRIGGER "ClaimImportListRecording"
            AFTER INSERT ON "Books" WHEN NEW."ImportListOriginId" > 0
            BEGIN
                INSERT INTO "ImportListManagedRecordings" ("BookId", "ForeignBookId", "AuthorMetadataId", "ImportListId")
                VALUES (NEW."Id", NEW."ForeignBookId", NEW."AuthorMetadataId", NEW."ImportListOriginId");
            END;
            CREATE TRIGGER "ReleaseChangedImportListRecording"
            AFTER UPDATE OF "Monitored", "ForeignBookId", "AuthorMetadataId" ON "Books"
            WHEN OLD."Monitored" IS NOT NEW."Monitored" OR OLD."ForeignBookId" IS NOT NEW."ForeignBookId" OR OLD."AuthorMetadataId" IS NOT NEW."AuthorMetadataId"
            BEGIN
                DELETE FROM "ImportListManagedRecordings" WHERE "BookId" = NEW."Id";
            END;
            CREATE TRIGGER "RemoveDeletedImportListRecording"
            AFTER DELETE ON "Books"
            BEGIN
                DELETE FROM "ImportListManagedRecordings" WHERE "BookId" = OLD."Id";
            END;
            CREATE TRIGGER "ReleaseChangedImportListTags"
            AFTER UPDATE OF "Tags", "AuthorMetadataId" ON "Authors"
            BEGIN
                DELETE FROM "ImportListTagOwners" WHERE "AuthorId" = NEW."Id" AND
                    (OLD."AuthorMetadataId" IS NOT NEW."AuthorMetadataId" OR
                     "TagId" NOT IN (SELECT value FROM json_each(COALESCE(NEW."Tags", '[]')) WHERE type = 'integer'));
            END;
            CREATE TRIGGER "RemoveDeletedImportListTags"
            AFTER DELETE ON "Authors"
            BEGIN
                DELETE FROM "ImportListTagOwners" WHERE "AuthorId" = OLD."Id";
            END;
            """;

        private const string PostgreSql = """
            CREATE FUNCTION claim_import_list_recording() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."ImportListOriginId" > 0 THEN
                    INSERT INTO "ImportListManagedRecordings" ("BookId", "ForeignBookId", "AuthorMetadataId", "ImportListId")
                    VALUES (NEW."Id", NEW."ForeignBookId", NEW."AuthorMetadataId", NEW."ImportListOriginId");
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER "ClaimImportListRecording" AFTER INSERT ON "Books" FOR EACH ROW EXECUTE FUNCTION claim_import_list_recording();
            CREATE FUNCTION release_changed_import_list_recording() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."Monitored" IS DISTINCT FROM NEW."Monitored" OR OLD."ForeignBookId" IS DISTINCT FROM NEW."ForeignBookId" OR OLD."AuthorMetadataId" IS DISTINCT FROM NEW."AuthorMetadataId" THEN
                    DELETE FROM "ImportListManagedRecordings" WHERE "BookId" = NEW."Id";
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER "ReleaseChangedImportListRecording" AFTER UPDATE OF "Monitored", "ForeignBookId", "AuthorMetadataId" ON "Books" FOR EACH ROW EXECUTE FUNCTION release_changed_import_list_recording();
            CREATE FUNCTION release_changed_import_list_tags() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                DELETE FROM "ImportListTagOwners" WHERE "AuthorId" = NEW."Id" AND
                    (OLD."AuthorMetadataId" IS DISTINCT FROM NEW."AuthorMetadataId" OR
                     NOT (COALESCE(NEW."Tags", '[]')::jsonb @> jsonb_build_array("TagId")));
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER "ReleaseChangedImportListTags" AFTER UPDATE OF "Tags", "AuthorMetadataId" ON "Authors" FOR EACH ROW EXECUTE FUNCTION release_changed_import_list_tags();
            """;
    }
}
