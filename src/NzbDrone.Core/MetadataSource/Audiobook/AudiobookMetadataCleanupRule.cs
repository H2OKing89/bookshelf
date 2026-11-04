using System;
using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public class AudiobookMetadataCleanupRule : ModelBase
    {
        public string Phrase { get; set; }
        public List<string> Labels { get; set; } = new List<string>();
        public bool Enabled { get; set; }
        public string Reason { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public static class AudiobookMetadataCleanupRuleSeed
    {
        // Defaults are inserted only by migration. Previewing does not save or enable them.
        public static List<AudiobookMetadataCleanupRule> Read()
        {
            using var stream = typeof(AudiobookMetadataCleanupRuleSeed).Assembly.GetManifestResourceStream(
                "NzbDrone.Core.MetadataSource.Audiobook.Overrides.cleanup-046.json");
            using var reader = new StreamReader(stream);
            return Json.Deserialize<List<AudiobookMetadataCleanupRule>>(reader.ReadToEnd());
        }
    }
}
