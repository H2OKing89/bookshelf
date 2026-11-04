using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public class AudiobookMetadataProjection
    {
        public string Title { get; set; } = string.Empty;
        public string DisplayTitle { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Series { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public bool TitleProtected { get; set; }
        public bool SubtitleProtected { get; set; }
        public List<string> MetadataLabels { get; set; } = new List<string>();
        public List<AudiobookMetadataCleanupChange> Changes { get; set; } = new List<AudiobookMetadataCleanupChange>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class AudiobookMetadataCleanupChange
    {
        public string Field { get; set; }
        public string OriginalText { get; set; }
        public string RemovedText { get; set; }
        public int? RuleId { get; set; }
        public List<string> Labels { get; set; } = new List<string>();
        public string Reason { get; set; }
    }
}
