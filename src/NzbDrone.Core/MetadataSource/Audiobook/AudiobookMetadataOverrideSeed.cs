using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookMetadataOverrideSeed
    {
        // Used only by the database migration. Deleting or disabling a correction
        // must not cause it to reappear at startup or on the next catalog request.
        public static List<AudiobookMetadataOverride> Read()
        {
            using var stream = typeof(AudiobookMetadataOverrideSeed).Assembly.GetManifestResourceStream(
                "NzbDrone.Core.MetadataSource.Audiobook.Overrides.defaults.json");
            using var reader = new StreamReader(stream);
            return Json.Deserialize<List<AudiobookMetadataOverride>>(reader.ReadToEnd());
        }
    }
}
