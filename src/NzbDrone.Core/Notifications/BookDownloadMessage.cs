using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Notifications
{
    public class BookDownloadMessage
    {
        public string Message { get; set; }
        public Author Author { get; set; }
        public Book Book { get; set; }
        public List<Book> Books { get; set; } = new List<Book>();
        public List<ManualInteractionBook> BookIdentities { get; set; } = new List<ManualInteractionBook>();
        public List<BookFile> BookFiles { get; set; }
        public List<BookFile> OldFiles { get; set; }
        public DownloadClientItemClientInfo DownloadClientInfo { get; set; }
        public string DownloadId { get; set; }
        public string SourceTitle { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string DownloadStatus { get; set; }
        public string ImportStatus { get; set; }
        public List<TrackedDownloadStatusMessage> StatusMessages { get; set; } = new List<TrackedDownloadStatusMessage>();

        public override string ToString()
        {
            return Message;
        }
    }
}
