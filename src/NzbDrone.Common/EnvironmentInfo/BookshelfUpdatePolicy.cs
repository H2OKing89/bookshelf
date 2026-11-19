using System;

namespace NzbDrone.Common.EnvironmentInfo
{
    public interface IBookshelfUpdatePolicy
    {
        bool IsEnabled { get; }
        string UnavailableReason { get; }
        void EnsureEnabled();
    }

    public sealed class BookshelfUpdatePolicy : IBookshelfUpdatePolicy
    {
        public const string DisabledReason = "In-app updates are unavailable for this Bookshelf build. Install a verified Bookshelf release manually.";

        // No compatible fork-owned feed is configured. Saved legacy update
        // settings must never authorize downloads or replacement of this fork.
        public bool IsEnabled => false;

        public string UnavailableReason => DisabledReason;

        public void EnsureEnabled()
        {
            throw new InvalidOperationException(DisabledReason);
        }
    }
}
