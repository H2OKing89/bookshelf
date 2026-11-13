using System.Collections.Generic;

namespace NzbDrone.Core.Books
{
    public class LocalRecording
    {
        public List<int> FileIds { get; set; } = new List<int>();
        public int AuthorId { get; set; }
        public string AuthorName { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public List<string> Narrators { get; set; } = new List<string>();
        public string Language { get; set; }
        public string Publisher { get; set; }
        public int? Year { get; set; }
        public int? RuntimeLengthMinutes { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
