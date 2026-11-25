using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    // User-reviewed works deliberately have an independent identity, even
    // when they share a title, series position or author with another work.
    public class RecordingWork : ModelBase
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Series { get; set; }
        public string Position { get; set; }
    }

    public class RecordingWorkMembership : ModelBase
    {
        public int EditionId { get; set; }
        public int WorkId { get; set; }
        public string RecordingIdentity { get; set; }
        public bool Confirmed { get; set; }
    }

    public class RecordingWorkRecording
    {
        public int EditionId { get; set; }
        public int BookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string Asin { get; set; }
        public string AudibleRegion { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string BookSlug { get; set; }
        public bool Monitored { get; set; }
        public int FileCount { get; set; }
        public long SizeOnDisk { get; set; }
    }

    public class RecordingWorkReference
    {
        public RecordingWorkRecording Recording { get; set; }
        public bool Confirmed { get; set; }
    }

    public class RecordingWorkCoverage
    {
        public RecordingWork Work { get; set; }
        public bool Confirmed { get; set; }
        public bool HasConfirmedFiles { get; set; }
        public List<RecordingWorkReference> Recordings { get; set; }
    }

    public class RecordingWorkSnapshot
    {
        public RecordingWorkRecording Recording { get; set; }
        public string Revision { get; set; }
        public int UnmatchedMembershipCount { get; set; }
        public List<RecordingWorkCoverage> Memberships { get; set; }
        public int CoveredWorkCount { get; set; }
        public int RelatedRecordingCount { get; set; }
        public int RelatedFileCount { get; set; }
        public long RelatedSizeOnDisk { get; set; }
    }

    public class RecordingWorkEdit
    {
        public string Revision { get; set; }
        public List<RecordingWorkMemberEdit> Memberships { get; set; }
    }

    public class RecordingWorkMemberEdit
    {
        // Exactly one of an existing work ID or a new manually described work.
        public int? WorkId { get; set; }
        public RecordingWork NewWork { get; set; }
        public bool? Confirmed { get; set; }
    }
}
