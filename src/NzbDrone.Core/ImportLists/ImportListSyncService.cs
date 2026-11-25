using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists
{
    public class ImportListSyncService : IExecute<ImportListSyncCommand>
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly IFetchAndParseImportList _listFetcherAndParser;
        private readonly IImportListMembershipRepository _membership;
        private readonly IImportListStatusService _listStatus;
        private readonly ISearchForNewBook _bookSearch;
        private readonly ISearchForNewAuthor _authorSearch;
        private readonly IProvideBookInfo _bookInfoProxy;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IAddBookService _addBookService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ImportListSyncService(IImportListFactory importListFactory,
                                     IImportListExclusionService importListExclusionService,
                                     IFetchAndParseImportList listFetcherAndParser,
                                     IImportListMembershipRepository membership,
                                     IImportListStatusService listStatus,
                                     ISearchForNewBook bookSearch,
                                     ISearchForNewAuthor authorSearch,
                                     IProvideBookInfo bookInfoProxy,
                                     IAuthorService authorService,
                                     IBookService bookService,
                                     IAddAuthorService addAuthorService,
                                     IAddBookService addBookService,
                                     IEventAggregator eventAggregator,
                                     IManageCommandQueue commandQueueManager,
                                     Logger logger)
        {
            _importListFactory = importListFactory;
            _importListExclusionService = importListExclusionService;
            _listFetcherAndParser = listFetcherAndParser;
            _membership = membership;
            _listStatus = listStatus;
            _bookSearch = bookSearch;
            _authorSearch = authorSearch;
            _bookInfoProxy = bookInfoProxy;
            _authorService = authorService;
            _bookService = bookService;
            _addAuthorService = addAuthorService;
            _addBookService = addBookService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        private List<Book> SyncAll()
        {
            if (_importListFactory.AutomaticAddEnabled().Empty())
            {
                _logger.Debug("No import lists with automatic add enabled");

                return new List<Book>();
            }

            _logger.ProgressInfo("Starting Import List Sync");

            var listItems = _listFetcherAndParser.Fetch().ToList();

            return ProcessListItems(listItems);
        }

        private List<Book> SyncList(ImportListDefinition definition)
        {
            if (definition == null)
            {
                return new List<Book>();
            }

            _logger.ProgressInfo($"Starting Import List Refresh for List {definition.Name}");

            var listItems = _listFetcherAndParser.FetchSingleList(definition).ToList();

            return ProcessListItems(listItems);
        }

        private List<Book> ProcessListItems(List<ImportListFetchBatch> batches)
        {
            var items = new List<(ImportListMembershipAttempt Attempt, ImportListItemInfo Report)>();
            foreach (var batch in batches)
            {
                try
                {
                    var entries = new List<ImportListMembershipItem>();
                    var resolved = new List<ImportListItemInfo>();
                    foreach (var report in batch.Items)
                    {
                        var entry = ImportListMembershipItem.FromSource(report);

                        // An exact local ID from another application is not an
                        // Audible title lookup; keep its raw membership only.
                        if (!IsLocalSource(report) && !entry.HasConflictingSourceRecordingIds())
                        {
                            ResolveReport(report);
                            if (entry.SetResolvedIdentity(report))
                            {
                                report.AuthorGoodreadsId = entry.AuthorId;
                                report.BookGoodreadsId = entry.BookId;
                                report.EditionGoodreadsId = entry.EditionId;
                                resolved.Add(report);
                            }
                        }

                        entries.Add(entry);
                    }

                    if (_membership.Complete(batch.Attempt, entries))
                    {
                        _listStatus.UpdateListSyncStatus(batch.Attempt.ImportListId);
                        var definition = _importListFactory.Find(batch.Attempt.ImportListId);
                        if (definition != null && definition.EnableAutomaticAdd && definition.MembershipRevision == batch.Attempt.Revision)
                        {
                            items.AddRange(resolved.Select(report => (batch.Attempt, report)));
                        }
                    }
                }
                catch (Exception exception)
                {
                    // A failed identity resolution must not replace a valid
                    // snapshot with a partially mapped set.
                    _logger.Error(exception, "Import List membership refresh failed for list {0}", batch.Attempt.ImportListId);
                }
            }

            return ProcessResolvedItems(items);
        }

        private static bool IsLocalSource(ImportListItemInfo report) => LocalRecordingId.IsLocal(report.BookGoodreadsId) ||
            LocalRecordingId.IsLocal(report.EditionGoodreadsId) || LocalAuthorId.IsLocal(report.AuthorGoodreadsId);

        private void ResolveReport(ImportListItemInfo report)
        {
            if (report.Book.IsNotNullOrWhiteSpace() || report.EditionGoodreadsId.IsNotNullOrWhiteSpace() || report.BookGoodreadsId.IsNotNullOrWhiteSpace())
            {
                if (!AudiobookId.IsNative(report.BookGoodreadsId) || !AudiobookId.IsNative(report.AuthorGoodreadsId) || !AudiobookId.IsNative(report.EditionGoodreadsId))
                {
                    MapBookReport(report);
                }
            }
            else if ((report.Author.IsNotNullOrWhiteSpace() || report.AuthorGoodreadsId.IsNotNullOrWhiteSpace()) && !AudiobookId.IsNative(report.AuthorGoodreadsId))
            {
                MapAuthorReport(report);
            }
        }

        private List<Book> ProcessResolvedItems(List<(ImportListMembershipAttempt Attempt, ImportListItemInfo Report)> items)
        {
            var processed = new List<Book>();
            var authorsToAdd = new List<Author>();
            var booksToAdd = new List<Book>();

            if (items.Count == 0)
            {
                _logger.ProgressInfo("No list items to process");

                return new List<Book>();
            }

            _logger.ProgressInfo("Processing {0} list items", items.Count);

            var reportNumber = 1;

            var listExclusions = _importListExclusionService.All();

            foreach (var (attempt, report) in items)
            {
                _logger.ProgressTrace("Processing list item {0}/{1}", reportNumber, items.Count);

                reportNumber++;

                var importList = _importListFactory.Find(report.ImportListId);
                if (importList == null || !importList.EnableAutomaticAdd || importList.MembershipRevision != attempt.Revision)
                {
                    continue;
                }

                if (report.Book.IsNotNullOrWhiteSpace() || report.EditionGoodreadsId.IsNotNullOrWhiteSpace() || report.BookGoodreadsId.IsNotNullOrWhiteSpace())
                {
                    if (AudiobookId.IsNative(report.BookGoodreadsId))
                    {
                        ProcessBookReport(importList, report, listExclusions, booksToAdd, authorsToAdd);
                    }
                }
                else if (report.Author.IsNotNullOrWhiteSpace() || report.AuthorGoodreadsId.IsNotNullOrWhiteSpace())
                {
                    ProcessAuthorReport(importList, report, listExclusions, authorsToAdd);
                }
            }

            var addedAuthors = _addAuthorService.AddAuthors(authorsToAdd, false);
            var addedBooks = _addBookService.AddBooks(booksToAdd, false);

            var message = string.Format($"Import List Sync Completed. Items found: {items.Count}, Authors added: {authorsToAdd.Count}, Books added: {booksToAdd.Count}");

            _logger.ProgressInfo(message);

            var toRefresh = addedAuthors.Select(x => x.Id).Concat(addedBooks.Select(x => x.Author.Value.Id)).Distinct().ToList();
            if (toRefresh.Any())
            {
                _commandQueueManager.Push(new BulkRefreshAuthorCommand(toRefresh, true));
            }

            return processed;
        }

        private void MapBookReport(ImportListItemInfo report)
        {
            var nativeId = AudiobookId.IsNative(report.BookGoodreadsId) ? report.BookGoodreadsId :
                AudiobookId.IsNative(report.EditionGoodreadsId) ? report.EditionGoodreadsId : null;
            Book mapped = null;
            if (nativeId != null)
            {
                try
                {
                    mapped = _bookInfoProxy.GetBookInfo(nativeId).Item2;
                }
                catch (BookNotFoundException)
                {
                    _logger.Debug("No audiobook found for {0}", nativeId);
                }
            }
            else if (AudiobookId.IsAsin(report.EditionGoodreadsId))
            {
                var matches = _bookSearch.SearchByAsin(report.EditionGoodreadsId).DistinctBy(book => book.ForeignBookId).ToList();
                mapped = matches.Count == 1 ? matches[0] : null;
            }
            else if (report.Book.IsNotNullOrWhiteSpace())
            {
                var matches = _bookSearch.SearchForNewBook(report.Book, report.Author)
                    .Where(b => Parser.Parser.CleanBookTitle(b.Title) == Parser.Parser.CleanBookTitle(report.Book))
                    .Where(b => report.Author.IsNullOrWhiteSpace() ||
                        Parser.Parser.CleanAuthorName(b.AuthorMetadata.Value.Name) == Parser.Parser.CleanAuthorName(report.Author))
                    .DistinctBy(b => b.ForeignBookId).ToList();
                if (matches.Count == 1)
                {
                    mapped = matches[0];
                }
            }

            report.BookGoodreadsId = mapped?.ForeignBookId;
            report.AuthorGoodreadsId = mapped?.AuthorMetadata.Value.ForeignAuthorId;
            report.EditionGoodreadsId = mapped?.Editions.Value.Single(e => e.Monitored).ForeignEditionId;
            if (mapped != null)
            {
                report.Book = mapped.Title;
                report.Author = mapped.AuthorMetadata.Value.Name;
            }
            else
            {
                _logger.Info("No unambiguous audiobook match for {0} by {1}; skipping this list item", report.Book, report.Author);
            }
        }

        private void ProcessBookReport(ImportListDefinition importList, ImportListItemInfo report, List<ImportListExclusion> listExclusions, List<Book> booksToAdd, List<Author> authorsToAdd)
        {
            // Check to see if book in DB
            var existingBook = _bookService.FindById(report.BookGoodreadsId);

            // Check to see if book excluded
            var excludedBook = listExclusions.FirstOrDefault(s => s.ForeignId == report.BookGoodreadsId || s.ForeignId == report.EditionGoodreadsId);

            // Check to see if author excluded
            var excludedAuthor = listExclusions.SingleOrDefault(s => s.ForeignId == report.AuthorGoodreadsId);

            if (excludedBook != null)
            {
                _logger.Debug("{0} [{1}] Rejected due to list exclusion", report.EditionGoodreadsId, report.Book);
                return;
            }

            if (excludedAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected due to list exclusion for parent author", report.EditionGoodreadsId, report.Book);
                return;
            }

            if (existingBook != null)
            {
                _logger.Debug("{0} [{1}] Rejected, Book Exists in DB.  Ensuring Book and Author monitored.", report.EditionGoodreadsId, report.Book);

                if (importList.ShouldMonitorExisting && importList.ShouldMonitor != ImportListMonitorType.None)
                {
                    if (!existingBook.Monitored)
                    {
                        _bookService.SetBookMonitored(existingBook.Id, true);

                        if (importList.ShouldMonitor == ImportListMonitorType.SpecificBook)
                        {
                            _commandQueueManager.Push(new BookSearchCommand(new List<int> { existingBook.Id }));
                        }
                    }

                    var existingAuthor = existingBook.Author.Value;
                    var doSearch = false;

                    if (importList.ShouldMonitor == ImportListMonitorType.EntireAuthor)
                    {
                        if (existingAuthor.Books.Value.Any(x => !x.Monitored))
                        {
                            doSearch = true;
                            _bookService.SetMonitored(existingAuthor.Books.Value.Select(x => x.Id), true);
                        }
                    }

                    if (!existingAuthor.Monitored)
                    {
                        doSearch = true;
                        existingAuthor.Monitored = true;
                        _authorService.UpdateAuthor(existingAuthor);
                    }

                    if (doSearch)
                    {
                        _commandQueueManager.Push(new MissingBookSearchCommand(existingAuthor.Id));
                    }
                }

                return;
            }

            // Append Book if not already in DB or already on add list
            if (booksToAdd.All(s => s.ForeignBookId != report.BookGoodreadsId))
            {
                var monitored = importList.ShouldMonitor != ImportListMonitorType.None;

                var toAddAuthor = new Author
                {
                    Monitored = monitored,
                    MonitorNewItems = importList.MonitorNewItems,
                    RootFolderPath = importList.RootFolderPath,
                    QualityProfileId = importList.ProfileId,
                    MetadataProfileId = importList.MetadataProfileId,
                    Tags = importList.Tags,
                    AddOptions = new AddAuthorOptions
                    {
                        SearchForMissingBooks = importList.ShouldSearch,
                        Monitored = monitored,
                        Monitor = monitored ? MonitorTypes.All : MonitorTypes.None
                    }
                };

                if (report.AuthorGoodreadsId != null && report.Author != null)
                {
                    toAddAuthor = ProcessAuthorReport(importList, report, listExclusions, authorsToAdd);
                }

                var toAdd = new Book
                {
                    ForeignBookId = report.BookGoodreadsId,
                    Monitored = monitored,
                    AnyEditionOk = true,
                    Editions = new List<Edition>(),
                    Author = toAddAuthor,
                    AddOptions = new AddBookOptions
                    {
                        // Only search for new book for existing authors
                        // New author searches are triggered by SearchForMissingBooks
                        SearchForNewBook = importList.ShouldSearch && toAddAuthor.Id > 0
                    }
                };

                if (AudiobookId.IsNative(report.EditionGoodreadsId))
                {
                    toAdd.Editions.Value.Add(new Edition
                    {
                        ForeignEditionId = report.EditionGoodreadsId,
                        Monitored = true
                    });
                }

                if (importList.ShouldMonitor == ImportListMonitorType.SpecificBook && toAddAuthor.AddOptions != null)
                {
                    Debug.Assert(toAddAuthor.Id == 0, "new author added but ID is not 0");
                    toAddAuthor.AddOptions.BooksToMonitor.Add(toAdd.ForeignBookId);
                }

                booksToAdd.Add(toAdd);
            }
        }

        private void MapAuthorReport(ImportListItemInfo report)
        {
            var matches = report.Author.IsNullOrWhiteSpace() ? new List<Author>() : _authorSearch.SearchForNewAuthor(report.Author)
                .Where(a => Parser.Parser.CleanAuthorName(a.Name) == Parser.Parser.CleanAuthorName(report.Author))
                .DistinctBy(a => a.ForeignAuthorId).ToList();
            report.AuthorGoodreadsId = matches.Count == 1 ? matches[0].ForeignAuthorId : null;
            if (matches.Count == 1)
            {
                report.Author = matches[0].Name;
            }
        }

        private Author ProcessAuthorReport(ImportListDefinition importList, ImportListItemInfo report, List<ImportListExclusion> listExclusions, List<Author> authorsToAdd)
        {
            if (report.AuthorGoodreadsId == null)
            {
                return null;
            }

            // Check to see if author in DB
            var existingAuthor = _authorService.FindById(report.AuthorGoodreadsId);

            // Check to see if author excluded
            var excludedAuthor = listExclusions.SingleOrDefault(s => s.ForeignId == report.AuthorGoodreadsId);

            // Check to see if author in import
            var existingImportAuthor = authorsToAdd.Find(i => i.ForeignAuthorId == report.AuthorGoodreadsId);

            if (excludedAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected due to list exclusion", report.AuthorGoodreadsId, report.Author);
                return null;
            }

            if (existingAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected, Author Exists in DB.  Ensuring Author monitored", report.AuthorGoodreadsId, report.Author);

                if (importList.ShouldMonitorExisting && !existingAuthor.Monitored)
                {
                    existingAuthor.Monitored = true;
                    _authorService.UpdateAuthor(existingAuthor);
                }

                return existingAuthor;
            }

            if (existingImportAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected, Author Exists in Import.", report.AuthorGoodreadsId, report.Author);

                return existingImportAuthor;
            }

            var monitored = importList.ShouldMonitor != ImportListMonitorType.None;

            var toAdd = new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = report.AuthorGoodreadsId,
                    Name = report.Author
                },
                Monitored = monitored,
                MonitorNewItems = importList.MonitorNewItems,
                RootFolderPath = importList.RootFolderPath,
                QualityProfileId = importList.ProfileId,
                MetadataProfileId = importList.MetadataProfileId,
                Tags = importList.Tags,
                AddOptions = new AddAuthorOptions
                {
                    SearchForMissingBooks = importList.ShouldSearch,
                    Monitored = monitored,
                    Monitor = monitored ? MonitorTypes.All : MonitorTypes.None
                }
            };

            authorsToAdd.Add(toAdd);

            return toAdd;
        }

        public void Execute(ImportListSyncCommand message)
        {
            var processed = message.DefinitionId.HasValue ? SyncList(_importListFactory.Find(message.DefinitionId.Value)) : SyncAll();

            _eventAggregator.PublishEvent(new ImportListSyncCompleteEvent(processed));
        }
    }
}
