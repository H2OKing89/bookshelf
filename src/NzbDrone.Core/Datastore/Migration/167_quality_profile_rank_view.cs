using FluentMigrator;
using Npgsql;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(167)]
    public class quality_profile_rank_view : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // A read-only derived view avoids maintaining a second copy of each
            // saved profile or needing a separate cache/rebuild on edits.
            Execute.WithConnection((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = connection is NpgsqlConnection ? PostgreSqlView : SqliteView;
                command.ExecuteNonQuery();
            });
        }

        private const string SqliteView = """
            CREATE VIEW "QualityProfileQualityRanks" AS
            WITH profiles AS (
                SELECT "Id" AS "ProfileId",
                    CASE WHEN json_valid("Items") THEN
                        CASE WHEN json_type("Items") = 'array' THEN "Items" ELSE '[]' END
                    ELSE '[]' END AS items
                FROM "QualityProfiles"
            ), positions AS (
                SELECT "ProfileId", CASE WHEN item.type = 'object' THEN item.value ELSE '{}' END AS item,
                    CASE WHEN json_array_length(profiles.items) = 1 THEN 1.0
                        ELSE CAST(item.key AS REAL) / (json_array_length(profiles.items) - 1) END AS score
                FROM profiles, json_each(profiles.items) AS item
            ), ranks AS (
                SELECT "ProfileId", json_extract(item, '$.quality') AS "QualityId", score
                FROM positions WHERE json_type(item, '$.quality') = 'integer'
                UNION ALL
                SELECT "ProfileId", json_extract(CASE WHEN member.type = 'object' THEN member.value ELSE '{}' END, '$.quality'), score
                FROM positions, json_each(CASE WHEN json_type(item, '$.items') = 'array' THEN json_extract(item, '$.items') ELSE '[]' END) AS member
                WHERE COALESCE(json_type(item, '$.quality'), 'null') = 'null'
                    AND json_type(CASE WHEN member.type = 'object' THEN member.value ELSE '{}' END, '$.quality') = 'integer'
            )
            SELECT "ProfileId", "QualityId", MIN(score) AS "Score" FROM ranks GROUP BY "ProfileId", "QualityId";
            """;

        private const string PostgreSqlView = """
            CREATE VIEW "QualityProfileQualityRanks" AS
            WITH profiles AS (
                SELECT "Id" AS "ProfileId",
                    CASE WHEN jsonb_typeof("Items"::jsonb) = 'array' THEN "Items"::jsonb ELSE '[]'::jsonb END AS items
                FROM "QualityProfiles"
            ), positions AS (
                SELECT "ProfileId", item.value AS item,
                    CASE WHEN jsonb_array_length(profiles.items) = 1 THEN 1.0::double precision
                        ELSE (item.ordinality - 1)::double precision / (jsonb_array_length(profiles.items) - 1) END AS score
                FROM profiles CROSS JOIN LATERAL jsonb_array_elements(profiles.items) WITH ORDINALITY AS item
            ), ranks AS (
                SELECT "ProfileId", item ->> 'quality' AS "QualityId", score
                FROM positions WHERE jsonb_typeof(item -> 'quality') = 'number'
                UNION ALL
                SELECT "ProfileId", member.value ->> 'quality', score
                FROM positions CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(item -> 'items') = 'array' THEN item -> 'items' ELSE '[]'::jsonb END) AS member
                WHERE COALESCE(jsonb_typeof(item -> 'quality'), 'null') = 'null'
                    AND jsonb_typeof(member.value -> 'quality') = 'number'
            )
            SELECT "ProfileId", "QualityId", MIN(score) AS "Score" FROM ranks GROUP BY "ProfileId", "QualityId";
            """;
    }
}
