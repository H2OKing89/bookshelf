using System;
using System.Collections.Generic;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport;

namespace NzbDrone.Core.Download
{
    public class DownloadImportReceipt
    {
        public int Version { get; set; } = 1;
        public string DownloadId { get; set; }
        public int DownloadClientId { get; set; }
        public int AuthorId { get; set; }

        // Reconciliation updates live ownership while retaining the identity
        // used when the original completion notification was claimed.
        public int? OriginalAuthorId { get; set; }
        public string OutputPath { get; set; }
        public bool KeepSourceFiles { get; set; }
        public List<int> BookIds { get; set; } = new List<int>();
        public List<DownloadImportFile> Files { get; set; } = new List<DownloadImportFile>();
    }

    public class DownloadImportSourceFile
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime ModifiedUtc { get; set; }
    }

    public class DownloadImportFile
    {
        public int BookId { get; set; }
        public int EditionId { get; set; }
        public int FileId { get; set; }
        public int Part { get; set; }
        public int PartCount { get; set; }
        public string SourcePath { get; set; }
        public string ImportedPath { get; set; }
        public long SourceSize { get; set; }
        public DateTime SourceModifiedUtc { get; set; }
    }

    public interface IDownloadImportSourceService
    {
        bool IsSafeRejectionSource(TrackedDownload trackedDownload);
        List<DownloadImportSourceFile> Scan(TrackedDownload trackedDownload);
        bool Validate(TrackedDownload trackedDownload, DownloadImportReceipt receipt);
        bool Retire(TrackedDownload trackedDownload, DownloadImportReceipt receipt);
    }

    public interface IDownloadImportCompletionService
    {
        DownloadImportReceipt VerifyAndRecord(TrackedDownload trackedDownload, List<ImportResult> importResults);
        DownloadImportReceipt ReadReceipt(TrackedDownload trackedDownload, DownloadHistory history);
    }
}
