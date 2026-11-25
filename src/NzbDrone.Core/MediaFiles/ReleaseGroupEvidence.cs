using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles
{
    // The resolved group is what naming and custom formats consume. Retain
    // the original labels separately so a pack uploader is not mistaken for
    // an individual recording's releaser, even after a subsequent manual edit.
    public class ReleaseGroupEvidence : IEmbeddedDocument
    {
        public string DownloadGroup { get; set; }
        public string FolderGroup { get; set; }
        public string FileGroup { get; set; }
        public string PackUploaderGroup { get; set; }
        public string RecordingFolderGroup { get; set; }
        public string RecordingId { get; set; }
        public string[] PackRecordingIds { get; set; }
        public string ResolvedGroup { get; set; }
        public string Source { get; set; }
        public string Reason { get; set; }

        public static ReleaseGroupEvidence ManualChoice(ReleaseGroupEvidence detected, string group)
        {
            var result = detected == null ? new ReleaseGroupEvidence() : (ReleaseGroupEvidence)detected.MemberwiseClone();
            result.ResolvedGroup = group.Trim();
            result.Source = "manual";
            result.Reason = "explicitChoice";
            return result;
        }
    }
}
