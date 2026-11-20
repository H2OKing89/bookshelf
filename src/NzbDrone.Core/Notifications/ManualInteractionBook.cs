using System.Collections.Generic;

namespace NzbDrone.Core.Notifications
{
    // Explicit recording identities; do not infer one edition from a work title
    // or collapse different Audible marketplaces into a single ASIN.
    public class ManualInteractionBook
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ForeignBookId { get; set; }
        public List<ManualInteractionEdition> Editions { get; set; } = new List<ManualInteractionEdition>();
    }

    public class ManualInteractionEdition
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ForeignEditionId { get; set; }
        public string Asin { get; set; }
        public string AudibleRegion { get; set; }
    }
}
