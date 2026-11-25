namespace NzbDrone.Core.Indexers
{
    // Review is intentionally zero so old definitions retain their behavior.
    public enum RejectedImportAction
    {
        Review = 0,
        Blocklist = 1,
        BlocklistAndRetry = 2
    }
}
