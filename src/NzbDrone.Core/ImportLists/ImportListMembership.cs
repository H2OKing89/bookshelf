using System;
using System.Collections.Generic;
using System.Linq;
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

        public void SetResolvedIdentity(ImportListItemInfo report)
        {
            AuthorId = QualifiedId(report.AuthorGoodreadsId, true);
            BookId = QualifiedId(report.BookGoodreadsId, false);
            EditionId = QualifiedId(report.EditionGoodreadsId, false);
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
