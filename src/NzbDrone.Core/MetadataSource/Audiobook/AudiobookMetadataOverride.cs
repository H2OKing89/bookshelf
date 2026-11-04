using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public class AudiobookMetadataOverride : ModelBase
    {
        public string Kind { get; set; }
        public string Region { get; set; }
        public string Asin { get; set; }
        public string Name { get; set; }
        public List<string> AlternateAsins { get; set; } = new List<string>();
        public List<string> Aliases { get; set; } = new List<string>();
        public string OriginalTitle { get; set; }
        public string OriginalSubtitle { get; set; }
        public string ReplacementTitle { get; set; }
        public string SourceUrl { get; set; }
        public string Reason { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime UpdatedAt { get; set; }
    }
}
