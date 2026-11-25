using System;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    public static class QualityProfileRank
    {
        public const double Unranked = -1;

        // Match the database view: equal group ranks, normalized to the length
        // of the saved profile (not the global format weight or score).
        public static double GetRank(QualityProfile profile, Quality quality)
        {
            var items = profile?.Items;
            if (items == null || quality == null)
            {
                return Unranked;
            }

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item?.Quality == quality || (item?.Quality == null && item?.Items?.Any(member => member?.Quality == quality) == true))
                {
                    return items.Count == 1 ? 1 : (double)index / (items.Count - 1);
                }
            }

            return Unranked;
        }

        public static bool IsQualitySort(string sortKey, string table) =>
            string.Equals(sortKey, "quality", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sortKey, table + ".quality", StringComparison.OrdinalIgnoreCase);

        // Call only with an application-owned table identifier, never a request value.
        public static string Join(SqlBuilder builder, string table)
        {
            var column = $"\"{table}\".\"Quality\"";
            var qualityId = builder.DatabaseType == DatabaseType.PostgreSQL
                ? $"({column}::jsonb ->> 'quality')"
                : $"(CASE WHEN json_valid({column}) THEN json_extract({column}, '$.quality') END)";
            builder.LeftJoin($"\"QualityProfileQualityRanks\" AS \"ProfileRank\" ON \"ProfileRank\".\"ProfileId\" = \"Authors\".\"QualityProfileId\" AND \"ProfileRank\".\"QualityId\" = {qualityId}");
            return "COALESCE(\"ProfileRank\".\"Score\", -1)";
        }
    }
}
