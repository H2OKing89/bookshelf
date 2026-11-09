using Equ;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class AudiobookChapter : MemberwiseEquatable<AudiobookChapter>, IEmbeddedDocument
    {
        public string Title { get; set; }
        public int Ordinal { get; set; }
        public long StartOffsetMs { get; set; }
        public long LengthMs { get; set; }
    }
}
