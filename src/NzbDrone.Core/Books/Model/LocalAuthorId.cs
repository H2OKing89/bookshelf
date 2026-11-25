using System;

namespace NzbDrone.Core.Books
{
    public static class LocalAuthorId
    {
        public static string NewId() => $"local:author:{Guid.NewGuid():N}";
        public static bool IsLocal(string id) => id?.StartsWith("local:author:", StringComparison.Ordinal) == true;
    }
}
