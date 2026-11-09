using System.Collections.Generic;
using Equ;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class AudiobookChapterMetadata : MemberwiseEquatable<AudiobookChapterMetadata>, IEmbeddedDocument
    {
        public AudiobookChapterMetadata()
        {
            Chapters = new List<AudiobookChapter>();
        }

        public long? RuntimeLengthMs { get; set; }
        public long? BrandIntroDurationMs { get; set; }
        public long? BrandOutroDurationMs { get; set; }
        public bool? IsAccurate { get; set; }
        public List<AudiobookChapter> Chapters { get; set; }
    }
}
