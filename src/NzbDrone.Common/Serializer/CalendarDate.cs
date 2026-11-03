using System;
using System.Globalization;

namespace NzbDrone.Common.Serializer
{
    public static class CalendarDate
    {
        // UTC is a storage convention here; a release date is not an instant.
        public static DateTime Normalize(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        public static DateTime? Normalize(DateTime? value) => value.HasValue ? Normalize(value.Value) : null;

        public static DateTime Parse(string value)
        {
            if (value == null || value.Length < 10 ||
                !DateTime.TryParseExact(value.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                (value.Length > 10 && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            {
                throw new FormatException("Expected an ISO calendar date or timestamp.");
            }

            return Normalize(date);
        }
    }
}
