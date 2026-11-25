using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists
{
    public class ImportListMembershipAttempt
    {
        public ImportListMembershipAttempt(int importListId, long revision, string token)
        {
            ImportListId = importListId;
            Revision = revision;
            Token = token;
        }

        public int ImportListId { get; }
        public long Revision { get; }
        public string Token { get; }
    }

    public class ImportListMembershipItem
    {
        public string SourceAuthor { get; set; }
        public string SourceTitle { get; set; }
        public string SourceAuthorId { get; set; }
        public string SourceBookId { get; set; }
        public string SourceEditionId { get; set; }
        public string AuthorId { get; set; }
        public string BookId { get; set; }
        public string EditionId { get; set; }
        public bool Excluded { get; set; }

        public static ImportListMembershipItem FromSource(ImportListItemInfo report) => new ()
        {
            SourceAuthor = report.Author,
            SourceTitle = report.Book,
            SourceAuthorId = report.AuthorGoodreadsId,
            SourceBookId = report.BookGoodreadsId,
            SourceEditionId = report.EditionGoodreadsId
        };

        public bool HasConflictingSourceRecordingIds()
        {
            var book = QualifiedId(SourceBookId, false);
            var edition = QualifiedId(SourceEditionId, false);
            return book != null && edition != null && book != edition;
        }

        public bool SetResolvedIdentity(ImportListItemInfo report)
        {
            var author = QualifiedId(report.AuthorGoodreadsId, true);
            var book = QualifiedId(report.BookGoodreadsId, false);
            var edition = QualifiedId(report.EditionGoodreadsId, false);
            var isBook = !string.IsNullOrWhiteSpace(SourceTitle) || !string.IsNullOrWhiteSpace(SourceBookId) || !string.IsNullOrWhiteSpace(SourceEditionId);
            if (!isBook)
            {
                AuthorId = author;
                return AuthorId != null;
            }

            var sourceBook = QualifiedId(SourceBookId, false);
            var sourceEdition = QualifiedId(SourceEditionId, false);
            var sourceAuthor = QualifiedId(SourceAuthorId, true);

            // Native audiobook books and editions represent an exact recording.
            // A contradictory row or an identity-changing lookup is retained as
            // raw membership, never effective ownership or a download/add action.
            if (book == null || edition == null || book != edition || author == null ||
                (sourceBook != null && sourceBook != book) || (sourceEdition != null && sourceEdition != edition) ||
                (sourceAuthor != null && sourceAuthor != author))
            {
                return false;
            }

            AuthorId = author;
            BookId = book;
            EditionId = edition;
            return true;
        }

        internal void MarkExcluded(HashSet<string> exclusions)
        {
            Excluded = exclusions.Contains(AuthorId ?? QualifiedRawId(SourceAuthorId, true)) ||
                exclusions.Contains(BookId ?? QualifiedRawId(SourceBookId, false)) ||
                exclusions.Contains(EditionId ?? QualifiedRawId(SourceEditionId, false));
        }

        private static string QualifiedRawId(string value, bool author)
        {
            if (author ? LocalAuthorId.IsLocal(value) : LocalRecordingId.IsLocal(value))
            {
                var parts = value.Split(':');
                return parts.Length == 3 && Guid.TryParse(parts[2], out _) ? value : null;
            }

            return QualifiedId(value, author);
        }

        private static string QualifiedId(string value, bool allowName)
        {
            if (!AudiobookId.IsNative(value))
            {
                return null;
            }

            try
            {
                var parsed = AudiobookId.Parse(value);
                return allowName || !parsed.IsName ? value : null;
            }
            catch (BookInfoException)
            {
                return null;
            }
        }
    }

    public class ImportListMembershipSnapshot
    {
        public int ImportListId { get; set; }
        public DateTime? LastSuccessfulSync { get; set; }
        public bool IsCurrent { get; set; }
        public List<ImportListMembershipItem> Items { get; set; } = new ();
    }

    public class ImportListFetchResult
    {
        public ImportListFetchResult(IList<ImportListItemInfo> items, bool complete)
        {
            Items = items;
            Complete = complete;
        }

        public IList<ImportListItemInfo> Items { get; }
        public bool Complete { get; }
    }

    public class ImportListFetchBatch
    {
        public ImportListFetchBatch(ImportListMembershipAttempt attempt, IList<ImportListItemInfo> items)
        {
            Attempt = attempt;
            Items = items;
        }

        public ImportListMembershipAttempt Attempt { get; }
        public IList<ImportListItemInfo> Items { get; }
    }

    internal static class ImportListItemIdentity
    {
        // Labels are not identities. In particular, two recordings with the
        // same title and author can have different marketplace-qualified IDs.
        public static List<ImportListItemInfo> DistinctItems(IEnumerable<ImportListItemInfo> items) => items.DistinctBy(item => new
        {
            item.AuthorGoodreadsId,
            item.BookGoodreadsId,
            item.EditionGoodreadsId,
            Author = string.IsNullOrWhiteSpace(item.AuthorGoodreadsId) ? item.Author : null,
            Book = string.IsNullOrWhiteSpace(item.BookGoodreadsId) && string.IsNullOrWhiteSpace(item.EditionGoodreadsId) ? item.Book : null
        }).ToList();
    }
}
