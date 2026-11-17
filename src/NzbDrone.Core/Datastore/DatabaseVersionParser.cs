using System;

namespace NzbDrone.Core.Datastore
{
    public static class DatabaseVersionParser
    {
        public static Version ParseServerVersion(string serverVersion)
        {
            var text = serverVersion?.TrimStart();
            var length = 0;
            while (text != null && length < text.Length &&
                ((text[length] >= '0' && text[length] <= '9') || text[length] == '.'))
            {
                length++;
            }

            if (length == 0 || !Version.TryParse(text.Substring(0, length), out var version))
            {
                throw new FormatException("The database provider returned an invalid server version.");
            }

            return version;
        }
    }
}
