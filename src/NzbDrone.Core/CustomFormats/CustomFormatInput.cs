using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.CustomFormats
{
    public class CustomFormatInput
    {
        public ParsedBookInfo BookInfo { get; set; }
        public Author Author { get; set; }
        public long Size { get; set; }
        public IndexerFlags IndexerFlags { get; set; }
        public string Filename { get; set; }
        public AudioFormatEvidence Audio { get; set; }

        public IReadOnlyCollection<Language> Languages { get; set; } = Array.Empty<Language>();
        public int SelectedEditionLanguageId { get; set; }
    }
}
