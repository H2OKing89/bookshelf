using System.Collections.Generic;

namespace NzbDrone.Core.Notifications
{
    // Only providers with a proven library-refresh operation implement this
    // interface. Other import notifications keep their existing per-book contract.
    public interface IBatchedMediaServerNotification
    {
        // Queue the entire eligible committed batch atomically. A blocked
        // connection must retain refresh targets but must not send chat messages.
        bool QueueImportBatch(IReadOnlyList<BookDownloadMessage> messages, bool sendNotifications, bool refreshForRemovedFiles = false);
        bool ProcessPendingUpdates();
    }
}
