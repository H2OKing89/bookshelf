using System.Data;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(163)]
    public class recording_work_membership : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.Table("RecordingWorks")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Title").AsString(512).NotNullable()
                .WithColumn("Author").AsString(512).NotNullable()
                .WithColumn("Series").AsString(512).NotNullable()
                .WithColumn("Position").AsString(128).NotNullable();

            Create.Table("RecordingWorkMemberships")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("EditionId").AsInt32().NotNullable().ForeignKey("Editions", "Id").OnDelete(Rule.Cascade)
                .WithColumn("WorkId").AsInt32().NotNullable().Indexed().ForeignKey("RecordingWorks", "Id")
                .WithColumn("RecordingIdentity").AsString(64).NotNullable()
                .WithColumn("Confirmed").AsBoolean().NotNullable();

            Create.Index("IX_RecordingWorkMemberships_Edition_Work")
                .OnTable("RecordingWorkMemberships")
                .OnColumn("EditionId").Ascending()
                .OnColumn("WorkId").Ascending()
                .WithOptions().Unique();

            // Bookshelf retains legacy SQLite connections with foreign_keys
            // disabled. Enforce cleanup of this new relation without changing
            // the behavior of historical foreign keys elsewhere in the app.
            IfDatabase("sqlite").Execute.WithConnection((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TRIGGER "RemoveDeletedRecordingWorkMemberships"
                    AFTER DELETE ON "Editions"
                    BEGIN
                        DELETE FROM "RecordingWorkMemberships" WHERE "EditionId" = OLD."Id";
                    END;
                    """;
                command.ExecuteNonQuery();
            });
        }
    }
}
