using System;
using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    public class BookImportBatchCompletedEvent : IEvent
    {
        public IReadOnlyList<BookImportedEvent> Imports { get; }
        public IReadOnlyList<BookFile> ReplacedFiles { get; }

        public BookImportBatchCompletedEvent(IReadOnlyList<BookImportedEvent> imports, IReadOnlyList<BookFile> replacedFiles = null)
        {
            Imports = imports;
            ReplacedFiles = replacedFiles ?? Array.Empty<BookFile>();
        }
    }
}
