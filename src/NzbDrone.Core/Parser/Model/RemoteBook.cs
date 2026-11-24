using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.Parser.Model
{
    public class RemoteBook
    {
        public ReleaseInfo Release { get; set; }
        public ParsedBookInfo ParsedBookInfo { get; set; }
        public Author Author { get; set; }
        public List<Book> Books { get; set; }
        public bool DownloadAllowed { get; set; }
        public TorrentSeedConfiguration SeedConfiguration { get; set; }
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public List<Language> RecordedLanguages { get; set; }
        public AudioFormatEvidence RecordedAudioFormatEvidence { get; set; }
        public int? SelectedEditionLanguageId { get; set; }
        public ReleaseSourceType ReleaseSource { get; set; }

        public RemoteBook()
        {
            Books = new List<Book>();
            CustomFormats = new List<CustomFormat>();
        }

        public RemoteBook CopyForInteractiveGrab()
        {
            // A forced association and the download service change the mapping,
            // parser details and seed configuration on this request's copy.
            var copy = (RemoteBook)MemberwiseClone();
            copy.Release = Release?.CopyForInteractiveGrab();
            copy.ParsedBookInfo = ParsedBookInfo?.CopyForInteractiveGrab();
            if (copy.ParsedBookInfo?.StructuredRelease != null)
            {
                copy.ParsedBookInfo.StructuredRelease = ReferenceEquals(ParsedBookInfo.StructuredRelease, Release)
                    ? copy.Release
                    : ParsedBookInfo.StructuredRelease.CopyForInteractiveGrab();
            }

            copy.Books = Books?.ToList() ?? new List<Book>();
            copy.CustomFormats = CustomFormats?.ToList() ?? new List<CustomFormat>();
            copy.RecordedLanguages = RecordedLanguages?.ToList();
            return copy;
        }

        public bool IsRecentBook()
        {
            return Books.Any(e => e.ReleaseDate >= DateTime.UtcNow.Date.AddDays(-14));
        }

        public override string ToString()
        {
            return Release.Title;
        }
    }

    public enum ReleaseSourceType
    {
        Unknown = 0,
        Rss = 1,
        Search = 2,
        UserInvokedSearch = 3,
        InteractiveSearch = 4,
        ReleasePush = 5
    }
}
