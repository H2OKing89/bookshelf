using Equ;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class AudiobookClassification : MemberwiseEquatable<AudiobookClassification>, IEmbeddedDocument
    {
        public string Name { get; set; }
        public string Asin { get; set; }
        public string Type { get; set; }
    }
}
