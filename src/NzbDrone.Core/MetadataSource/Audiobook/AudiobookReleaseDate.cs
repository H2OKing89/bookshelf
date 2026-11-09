using System;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookReleaseDate
    {
        // This specific placeholder was observed on recordings without a confirmed date.
        // Preserve the source value separately; genuine future release dates remain valid.
        public static bool IsPlaceholder(DateTime? value) => value?.Date == new DateTime(2050, 12, 30);

        public static DateTime? Normalize(DateTime? value) => IsPlaceholder(value) ? null : CalendarDate.Normalize(value);
    }
}
