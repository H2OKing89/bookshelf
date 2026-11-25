using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    public class BookFileDeletedEvent : IEvent
    {
        public BookFile BookFile { get; private set; }
        public DeleteMediaFileReason Reason { get; private set; }
        public bool IsPartOfImportBatch { get; }

        public BookFileDeletedEvent(BookFile bookFile, DeleteMediaFileReason reason, bool isPartOfImportBatch = false)
        {
            BookFile = bookFile;
            Reason = reason;
            IsPartOfImportBatch = isPartOfImportBatch;
        }
    }
}
