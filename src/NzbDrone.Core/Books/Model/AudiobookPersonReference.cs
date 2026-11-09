using Equ;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class AudiobookPersonReference : MemberwiseEquatable<AudiobookPersonReference>, IEmbeddedDocument
    {
        public string Name { get; set; }
        public string Asin { get; set; }
    }
}
