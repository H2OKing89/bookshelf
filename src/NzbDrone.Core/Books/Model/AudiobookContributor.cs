using Equ;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class AudiobookContributor : MemberwiseEquatable<AudiobookContributor>, IEmbeddedDocument
    {
        public string Name { get; set; }
        public string Asin { get; set; }
        public string Role { get; set; }
    }
}
