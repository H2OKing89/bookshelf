using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.RecordingReview
{
    public class RecordingReviewResult
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public bool PreferPathAsin { get; set; }
        public List<string> FilenameAsins { get; set; } = new List<string>();
        public List<string> FolderAsins { get; set; } = new List<string>();
        public List<string> EmbeddedAsins { get; set; } = new List<string>();
        public bool MalformedEmbeddedAsin { get; set; }
        public string EffectiveAsin { get; set; }
        public string TagTitle { get; set; }
        public List<string> TagAuthors { get; set; } = new List<string>();
        public List<string> TagNarrators { get; set; } = new List<string>();
        public string Reason { get; set; }
        public RecordingReviewTarget CurrentRecording { get; set; }
        public List<RecordingReviewTarget> Candidates { get; set; } = new List<RecordingReviewTarget>();
    }

    public class RecordingReviewTarget
    {
        public int EditionId { get; set; }
        public int BookId { get; set; }
        public int AuthorId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string RecordingId { get; set; }
        public string Asin { get; set; }
        public string Region { get; set; }
        public List<string> Narrators { get; set; } = new List<string>();
        public string Revision { get; set; }
    }
}
