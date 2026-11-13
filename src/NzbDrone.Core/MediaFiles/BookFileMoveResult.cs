using System.Collections.Generic;
using NzbDrone.Core.Books.Calibre;

namespace NzbDrone.Core.MediaFiles
{
    public class BookFileMoveResult
    {
        public BookFileMoveResult()
        {
            OldFiles = new List<BookFile>();
        }

        public BookFile BookFile { get; set; }
        public List<BookFile> OldFiles { get; set; }
        public string SourcePath { get; set; }
        public string StagingPath { get; set; }
        public string BackupPath { get; set; }
        public bool Published { get; set; }
        public bool CopyOnly { get; set; }
        public bool Committed { get; set; }
        public bool Aborted { get; set; }
        public CalibreSettings CalibreSettings { get; set; }
    }
}
