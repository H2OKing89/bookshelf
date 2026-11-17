using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IRefreshBookService
    {
        IDisposable PrepareBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData);
        bool RefreshBookInfo(Book book, List<Book> remoteBooks, Author remoteData, bool forceUpdateFileTags);
        bool RefreshBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate);
    }

    public class RefreshBookService : RefreshEntityServiceBase<Book, Edition>,
        IRefreshBookService,
        IExecute<RefreshBookCommand>,
        IExecute<BulkRefreshBookCommand>
    {
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IEditionService _editionService;
        private readonly ISeriesService _seriesService;
        private readonly ISeriesBookLinkService _seriesBookLinkService;
        private readonly IProvideAuthorInfo _authorInfo;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IRefreshEditionService _refreshEditionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICheckIfBookShouldBeRefreshed _checkIfBookShouldBeRefreshed;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly Logger _logger;
        private readonly AsyncLocal<PreparedBookData> _preparedBooks = new AsyncLocal<PreparedBookData>();

        public RefreshBookService(IBookService bookService,
                                  IAuthorService authorService,
                                  IRootFolderService rootFolderService,
                                  IAddAuthorService addAuthorService,
                                  IEditionService editionService,
                                  ISeriesService seriesService,
                                  ISeriesBookLinkService seriesBookLinkService,
                                  IAuthorMetadataService authorMetadataService,
                                  IProvideAuthorInfo authorInfo,
                                  IProvideBookInfo bookInfo,
                                  IRefreshEditionService refreshEditionService,
                                  IMediaFileService mediaFileService,
                                  IHistoryService historyService,
                                  IEventAggregator eventAggregator,
                                  ICheckIfBookShouldBeRefreshed checkIfBookShouldBeRefreshed,
                                  IMapCoversToLocal mediaCoverService,
                                  Logger logger)
        : base(logger, authorMetadataService)
        {
            _bookService = bookService;
            _authorService = authorService;
            _rootFolderService = rootFolderService;
            _addAuthorService = addAuthorService;
            _editionService = editionService;
            _seriesService = seriesService;
            _seriesBookLinkService = seriesBookLinkService;
            _authorInfo = authorInfo;
            _bookInfo = bookInfo;
            _refreshEditionService = refreshEditionService;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _checkIfBookShouldBeRefreshed = checkIfBookShouldBeRefreshed;
            _mediaCoverService = mediaCoverService;
            _logger = logger;
        }

        private Author GetSkyhookData(Book book)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tuple = _bookInfo.GetBookInfo(book.ForeignBookId);
                var author = AudiobookId.IsNative(book.ForeignBookId)
                    ? tuple.Item2.Author.Value
                    : _authorInfo.GetAuthorInfo(tuple.Item1);
                var newbook = tuple.Item2;

                newbook.Author = author;
                newbook.AuthorMetadata = author.Metadata.Value;
                newbook.AuthorMetadataId = book.AuthorMetadataId;
                newbook.AuthorMetadata.Value.Id = book.AuthorMetadataId;

                author.Books = new List<Book> { newbook };
                return author;
            }
            catch (BookNotFoundException)
            {
                _logger.Error($"Could not find book with id {book.ForeignBookId}");
            }

            return null;
        }

        protected override RemoteData GetRemoteData(Book local, List<Book> remote, Author data)
        {
            if (_preparedBooks.Value?.Books.TryGetValue(local.ForeignBookId, out var prepared) == true)
            {
                if (prepared.Entity != null)
                {
                    prepared.Entity.Id = local.Id;
                }

                return prepared;
            }

            return ReadRemoteData(local, remote, data);
        }

        private RemoteData ReadRemoteData(Book local, List<Book> remote, Author data)
        {
            var result = new RemoteData();

            var book = remote.SingleOrDefault(x => x.ForeignBookId == local.ForeignBookId);

            if (book == null && ShouldDelete(local))
            {
                return result;
            }

            if (book == null)
            {
                data = GetSkyhookData(local);
                book = data?.Books?.Value?.SingleOrDefault(x => x.ForeignBookId == local.ForeignBookId);
            }

            result.Entity = book;
            if (result.Entity != null)
            {
                result.Entity.Id = local.Id;
            }

            return result;
        }

        public IDisposable PrepareBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData)
        {
            var prepared = new PreparedBookData();
            foreach (var book in books.Where(book => !LocalRecordingId.IsLocal(book.ForeignBookId)))
            {
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                prepared.Books[book.ForeignBookId] = ReadRemoteData(book, remoteBooks, remoteData);
            }

            foreach (var book in remoteBooks.Concat(prepared.Books.Values.Select(data => data.Entity)).Where(book => book != null))
            {
                PrepareParent(book, prepared);
            }

            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var scope = new PreparedBookScope(_preparedBooks, _preparedBooks.Value);
            _preparedBooks.Value = prepared;
            return scope;
        }

        protected override IDisposable PrepareRefresh(Book local, Book remote, Author data)
        {
            if (_preparedBooks.Value != null || !CommandExecutionContext.CancellationToken.CanBeCanceled)
            {
                return null;
            }

            return PrepareBookInfo(new List<Book> { local }, new List<Book> { remote }, data);
        }

        private void PrepareParent(Book remote, PreparedBookData prepared)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var metadata = remote.AuthorMetadata.Value;
            if (_authorService.FindById(metadata.ForeignAuthorId) == null && !prepared.Parents.ContainsKey(metadata.ForeignAuthorId))
            {
                prepared.Parents[metadata.ForeignAuthorId] = _addAuthorService.PrepareAuthor(new Author { Metadata = metadata });
            }
        }

        private sealed class PreparedBookData
        {
            public Dictionary<string, RemoteData> Books { get; } = new Dictionary<string, RemoteData>(StringComparer.Ordinal);
            public Dictionary<string, Author> Parents { get; } = new Dictionary<string, Author>(StringComparer.Ordinal);
        }

        private sealed class PreparedBookScope : IDisposable
        {
            private readonly AsyncLocal<PreparedBookData> _storage;
            private readonly PreparedBookData _previous;
            private bool _disposed;

            public PreparedBookScope(AsyncLocal<PreparedBookData> storage, PreparedBookData previous)
            {
                _storage = storage;
                _previous = previous;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _storage.Value = _previous;
                    _disposed = true;
                }
            }
        }

        protected override void EnsureNewParent(Book local, Book remote)
        {
            // Make sure the appropriate author exists (it could be that an book changes parent)
            // The authorMetadata entry will be in the db but make sure a corresponding author is too
            // so that the book doesn't just disappear.

            // TODO filter by metadata id before hitting database
            _logger.Trace($"Ensuring parent author exists [{remote.AuthorMetadata.Value.ForeignAuthorId}]");

            var newAuthor = _authorService.FindById(remote.AuthorMetadata.Value.ForeignAuthorId);

            if (newAuthor == null)
            {
                var oldAuthor = local.Author.Value;
                var addAuthor = new Author
                {
                    Metadata = remote.AuthorMetadata.Value,
                    MetadataProfileId = oldAuthor.MetadataProfileId,
                    QualityProfileId = oldAuthor.QualityProfileId,
                    RootFolderPath = _rootFolderService.GetBestRootFolderPath(oldAuthor.Path),
                    Monitored = oldAuthor.Monitored,
                    Tags = oldAuthor.Tags
                };
                _logger.Debug($"Adding missing parent author {addAuthor}");
                if (_preparedBooks.Value?.Parents.TryGetValue(remote.AuthorMetadata.Value.ForeignAuthorId, out var preparedAuthor) == true)
                {
                    _addAuthorService.AddAuthor(addAuthor, preparedAuthor);
                }
                else
                {
                    _addAuthorService.AddAuthor(addAuthor);
                }
            }
        }

        protected override bool ShouldDelete(Book local)
        {
            // Catalog omissions and regional availability are not deletion signals.
            if (AudiobookId.IsNative(local.ForeignBookId) || LocalRecordingId.IsLocal(local.ForeignBookId))
            {
                return false;
            }

            // not manually added and has no files
            return local.AddOptions.AddType != BookAddType.Manual &&
                !_mediaFileService.GetFilesByBook(local.Id).Any();
        }

        protected override void LogProgress(Book local)
        {
            _logger.ProgressInfo("Updating Info for {0}", local.Title);
        }

        protected override bool IsMerge(Book local, Book remote)
        {
            if (local.ForeignBookId != remote.ForeignBookId &&
                (AudiobookId.IsNative(local.ForeignBookId) || AudiobookId.IsNative(remote.ForeignBookId) ||
                    LocalRecordingId.IsLocal(local.ForeignBookId) || LocalRecordingId.IsLocal(remote.ForeignBookId)))
            {
                throw new InvalidOperationException("Metadata refresh cannot replace an audiobook's recording identity.");
            }

            return local.ForeignBookId != remote.ForeignBookId;
        }

        protected override UpdateResult UpdateEntity(Book local, Book remote)
        {
            UpdateResult result;

            remote.UseDbFieldsFrom(local);

            if (local.Title != (remote.Title ?? "Unknown") ||
                local.ForeignBookId != remote.ForeignBookId ||
                local.AuthorMetadata.Value.ForeignAuthorId != remote.AuthorMetadata.Value.ForeignAuthorId)
            {
                result = UpdateResult.UpdateTags;
            }
            else if (!local.Equals(remote))
            {
                result = UpdateResult.Standard;
            }
            else
            {
                result = UpdateResult.None;
            }

            // Force update and fetch covers if images have changed so that we can write them into tags
            // if (remote.Images.Any() && !local.Images.SequenceEqual(remote.Images))
            // {
            //     _mediaCoverService.EnsureBookCovers(remote);
            //     result = UpdateResult.UpdateTags;
            // }
            local.UseMetadataFrom(remote);

            local.AuthorMetadataId = remote.AuthorMetadata.Value.Id;
            local.LastInfoSync = DateTime.UtcNow;

            return result;
        }

        protected override UpdateResult MergeEntity(Book local, Book target, Book remote)
        {
            if (AudiobookId.IsNative(local.ForeignBookId) || AudiobookId.IsNative(target.ForeignBookId))
            {
                throw new InvalidOperationException("Distinct audiobook recording IDs cannot be merged or have their files reassigned during metadata refresh.");
            }

            _logger.Warn($"Book {local} was merged with {remote} because the original was a duplicate.");

            // Update book ids for trackfiles
            var files = _mediaFileService.GetFilesByBook(local.Id);
            files.ForEach(x => x.EditionId = target.Editions.Value.Single(e => e.Monitored).Id);
            _mediaFileService.Update(files);

            // Update book ids for history
            var items = _historyService.GetByBook(local.Id, null);
            items.ForEach(x => x.BookId = target.Id);
            _historyService.UpdateMany(items);

            // Finally delete the old book
            _bookService.DeleteMany(new List<Book> { local });

            return UpdateResult.UpdateTags;
        }

        protected override Book GetEntityByForeignId(Book local)
        {
            return _bookService.FindById(local.ForeignBookId);
        }

        protected override void SaveEntity(Book local)
        {
            // Use UpdateMany to avoid firing the book edited event
            _bookService.UpdateMany(new List<Book> { local });
        }

        protected override void DeleteEntity(Book local, bool deleteFiles)
        {
            _bookService.DeleteBook(local.Id, deleteFiles);
        }

        protected override List<Edition> GetRemoteChildren(Book local, Book remote)
        {
            return remote.Editions.Value.DistinctBy(m => m.ForeignEditionId).ToList();
        }

        protected override List<Edition> GetLocalChildren(Book entity, List<Edition> remoteChildren)
        {
            return _editionService.GetEditionsForRefresh(entity.Id, remoteChildren.Select(x => x.ForeignEditionId).ToList());
        }

        protected override Tuple<Edition, List<Edition>> GetMatchingExistingChildren(List<Edition> existingChildren, Edition remote)
        {
            var existingChild = existingChildren.SingleOrDefault(x => x.ForeignEditionId == remote.ForeignEditionId);
            return Tuple.Create(existingChild, new List<Edition>());
        }

        protected override void PrepareNewChild(Edition child, Book entity)
        {
            child.BookId = entity.Id;
            child.Book = entity;
        }

        protected override void PrepareExistingChild(Edition local, Edition remote, Book entity)
        {
            local.BookId = entity.Id;
            local.Book = entity;

            remote.UseDbFieldsFrom(local);
        }

        protected override void AddChildren(List<Edition> children)
        {
            // hack - add the chilren in refresh children so we can control monitored status
        }

        private void MonitorSingleEdition(SortedChildren children)
        {
            children.Old.ForEach(x => x.Monitored = false);
            var monitored = children.Future.Where(x => x.Monitored).ToList();

            if (monitored.Count == 1)
            {
                return;
            }

            if (monitored.Count == 0)
            {
                monitored = children.Future;
            }

            if (monitored.Count == 0)
            {
                // there are no future children so nothing to do
                return;
            }

            var toMonitor = monitored.OrderByDescending(x => x.Id > 0 ? _mediaFileService.GetFilesByEdition(x.Id).Count : 0)
                .ThenByDescending(x => x.Ratings.Popularity).First();

            monitored.ForEach(x => x.Monitored = false);
            toMonitor.Monitored = true;

            // force update of anything we've messed with
            var extraToUpdate = children.UpToDate.Where(x => monitored.Contains(x));
            children.UpToDate = children.UpToDate.Except(extraToUpdate).ToList();
            children.Updated.AddRange(extraToUpdate);

            Debug.Assert(!children.Future.Any() || children.Future.Count(x => x.Monitored) == 1, "one edition monitored");
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<Edition> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            // make sure only one of the releases ends up monitored
            MonitorSingleEdition(localChildren);

            localChildren.All.ForEach(x => _logger.Trace($"release: {x} monitored: {x.Monitored}"));

            _editionService.InsertMany(localChildren.Added);

            return _refreshEditionService.RefreshEditionInfo(localChildren.Added, localChildren.Updated, localChildren.Merged, localChildren.Deleted, localChildren.UpToDate, remoteChildren, forceUpdateFileTags);
        }

        protected override void PublishEntityUpdatedEvent(Book entity)
        {
            // Fetch fresh from DB so all lazy loads are available
            _eventAggregator.PublishEvent(new BookUpdatedEvent(_bookService.GetBook(entity.Id)));
        }

        public bool RefreshBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var updated = false;

            foreach (var book in books)
            {
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                if (forceBookRefresh || _checkIfBookShouldBeRefreshed.ShouldRefresh(book))
                {
                    updated |= RefreshBookInfo(book, remoteBooks, remoteData, forceUpdateFileTags);
                }
                else
                {
                    _logger.Debug("Skipping refresh of book: {0}", book.Title);
                }
            }

            return updated;
        }

        public bool RefreshBookInfo(Book book, List<Book> remoteBooks, Author remoteData, bool forceUpdateFileTags)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            return !LocalRecordingId.IsLocal(book.ForeignBookId) &&
                RefreshEntityInfo(book, remoteBooks, remoteData, true, forceUpdateFileTags, null);
        }

        public bool RefreshBookInfo(Book book)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            if (LocalRecordingId.IsLocal(book.ForeignBookId))
            {
                return false;
            }

            var data = GetSkyhookData(book);

            if (data == null)
            {
                return false;
            }

            // The author-wide refresh persists its complete series graph separately.
            // A direct recording refresh must save just this book's links before the
            // normal updated event reloads the book from the database.
            var remote = data.Books.Value.Single();
            using var preparedRefresh = CommandExecutionContext.CancellationToken.CanBeCanceled
                ? PrepareBookInfo(new List<Book> { book }, data.Books, data)
                : null;
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            bool seriesUpdated;
            bool updated;
            using (CommandExecutionContext.Begin(CancellationToken.None))
            {
                seriesUpdated = RefreshBookSeries(book, remote);
                updated = RefreshBookInfo(book, data.Books, data, false);
            }

            if (seriesUpdated && !updated)
            {
                PublishEntityUpdatedEvent(book);
            }

            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            if (AudiobookId.IsNative(book.ForeignBookId))
            {
                // Direct recording refreshes do not raise the author refresh
                // event that normally downloads covers for the bibliography.
                _mediaCoverService.EnsureBookCovers(_bookService.GetBook(book.Id) ?? book);
            }

            return updated || seriesUpdated;
        }

        private bool RefreshBookSeries(Book book, Book remote)
        {
            if (!AudiobookId.IsNative(book.ForeignBookId) || remote.SeriesLinks?.Value?.Any() != true)
            {
                return false;
            }

            var storedLinks = (_seriesBookLinkService.GetLinksByBook(new List<int> { book.Id }) ?? new List<SeriesBookLink>())
                .Where(link => link.BookId == book.Id).ToList();
            var combined = new Book
            {
                Id = book.Id,
                ForeignBookId = book.ForeignBookId,
                AuthorMetadata = book.AuthorMetadata,
                SeriesLinks = remote.SeriesLinks.Value
            };
            AudiobookMetadataMerger.MergeSeries(combined, new Book { ForeignBookId = book.ForeignBookId, SeriesLinks = storedLinks });
            var desired = combined.SeriesLinks.Value;
            var ids = desired.Select(link => link.Series.Value.ForeignSeriesId).ToList();
            var existingSeries = _seriesService.FindById(ids) ?? new List<Series>();
            var byForeignId = existingSeries.ToDictionary(series => series.ForeignSeriesId);
            var toAdd = desired.Where(link => !byForeignId.ContainsKey(link.Series.Value.ForeignSeriesId))
                .Select(link => new Series
                {
                    ForeignSeriesId = link.Series.Value.ForeignSeriesId,
                    Title = link.Series.Value.Title,
                    Description = link.Series.Value.Description,
                    Numbered = link.Position.IsNotNullOrWhiteSpace(),
                    WorkCount = 1,
                    PrimaryWorkCount = link.IsPrimary ? 1 : 0
                }).ToList();
            if (toAdd.Any())
            {
                _seriesService.InsertMany(toAdd);
                foreach (var series in toAdd)
                {
                    byForeignId.Add(series.ForeignSeriesId, series);
                }
            }

            // Do not replace shared series metadata or counts with a one-book view,
            // and never remove omitted memberships from an incomplete provider response.
            var addedLinks = new List<SeriesBookLink>();
            var updatedLinks = new List<SeriesBookLink>();
            foreach (var link in desired)
            {
                var series = byForeignId[link.Series.Value.ForeignSeriesId];
                var previous = storedLinks.SingleOrDefault(item => item.SeriesId == series.Id);
                link.Book = book;
                link.BookId = book.Id;
                link.Series = series;
                link.SeriesId = series.Id;
                if (previous == null)
                {
                    addedLinks.Add(link);
                }
                else
                {
                    link.Id = previous.Id;
                    if (previous.Position != link.Position || previous.SeriesPosition != link.SeriesPosition || previous.IsPrimary != link.IsPrimary)
                    {
                        updatedLinks.Add(link);
                    }
                }
            }

            if (addedLinks.Any())
            {
                _seriesBookLinkService.InsertMany(addedLinks);
            }

            if (updatedLinks.Any())
            {
                _seriesBookLinkService.UpdateMany(updatedLinks);
            }

            book.SeriesLinks = desired;
            return addedLinks.Any() || updatedLinks.Any();
        }

        public void Execute(BulkRefreshBookCommand message)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            var books = _bookService.GetBooks(message.BookIds);

            foreach (var book in books)
            {
                RefreshBookInfo(book);
            }
        }

        public void Execute(RefreshBookCommand message)
        {
            CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
            if (message.BookId.HasValue)
            {
                var book = _bookService.GetBook(message.BookId.Value);

                RefreshBookInfo(book);
            }
        }
    }
}
