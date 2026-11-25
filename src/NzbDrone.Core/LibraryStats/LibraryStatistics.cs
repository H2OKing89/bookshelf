using System.Collections.Generic;

namespace NzbDrone.Core.LibraryStats
{
    public class LibraryStatistics
    {
        public List<LibraryStatisticsAuthor> Authors { get; set; } = new List<LibraryStatisticsAuthor>();
        public int AuthorCount { get; set; }
        public int BookCount { get; set; }
        public int AudioRecordingCount { get; set; }
        public int OwnedAudioRecordingCount { get; set; }
        public int EbookEditionCount { get; set; }
        public int OwnedEbookEditionCount { get; set; }
        public int ConfirmedWorkCount { get; set; }
        public int OwnedConfirmedWorkCount { get; set; }
        public int FileCount { get; set; }
        public int MappedFileCount { get; set; }
        public int UnresolvedFileCount { get; set; }
        public int AudioFileCount { get; set; }
        public int EbookFileCount { get; set; }
        public int OtherFileCount { get; set; }
        public long SizeOnDisk { get; set; }
        public long MappedSizeOnDisk { get; set; }
        public long UnresolvedSizeOnDisk { get; set; }
        public double? KnownAudioDurationSeconds { get; set; }
        public int AudioFilesWithKnownDuration { get; set; }
        public List<LibraryStatisticsGroup> Codecs { get; set; }
        public List<LibraryStatisticsGroup> Bitrates { get; set; }
        public List<LibraryStatisticsGroup> Languages { get; set; }
        public List<LibraryStatisticsGroup> Narrators { get; set; }
        public List<LibraryStatisticsGroup> Sources { get; set; }
        public List<LibraryStatisticsGroup> MediaInfoStatus { get; set; }
    }

    public class LibraryStatisticsAuthor
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class LibraryStatisticsGroup
    {
        public string Label { get; set; }
        public int Count { get; set; }
    }
}
