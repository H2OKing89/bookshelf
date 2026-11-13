using System;

namespace NzbDrone.Core.Books
{
    public static class LocalRecordingId
    {
        public static string NewId() => $"local:recording:{Guid.NewGuid():N}";
        public static bool IsLocal(string id) => id?.StartsWith("local:recording:", StringComparison.Ordinal) == true;
    }
}
