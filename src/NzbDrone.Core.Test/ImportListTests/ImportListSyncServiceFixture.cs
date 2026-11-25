using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListSyncServiceFixture : CoreTest<ImportListSyncService>
    {
        private List<ImportListItemInfo> _importListReports;

        [SetUp]
        public void SetUp()
        {
            var importListItem1 = new ImportListItemInfo
            {
                Author = "Linkin Park"
            };

            _importListReports = new List<ImportListItemInfo> { importListItem1 };

            var mockImportList = new Mock<IImportList>();
            Mocker.GetMock<IImportListMembershipRepository>().Setup(v => v.Complete(It.IsAny<ImportListMembershipAttempt>(), It.IsAny<IReadOnlyList<ImportListMembershipItem>>())).Returns(true);

            Mocker.GetMock<IFetchAndParseImportList>()
                .Setup(v => v.Fetch())
                .Returns(() => new List<ImportListFetchBatch> { new (new ImportListMembershipAttempt(0, 0, "fixture"), _importListReports) });

            Mocker.GetMock<ISearchForNewBook>()
                .Setup(v => v.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());
            Mocker.GetMock<ISearchForNewAuthor>()
                .Setup(v => v.SearchForNewAuthor(It.IsAny<string>()))
                .Returns(new List<Author>());
            Mocker.GetMock<IProvideBookInfo>()
                .Setup(v => v.GetBookInfo(It.IsAny<string>()))
                .Returns<string>(id =>
                {
                    var metadata = new AuthorMetadata { ForeignAuthorId = "audible:us:B000000001", Name = "Linkin Park" };
                    var book = Builder<Book>.CreateNew()
                        .With(b => b.AuthorMetadata = metadata)
                        .With(b => b.ForeignBookId = id)
                        .With(b => b.Title = "Meteora")
                        .With(b => b.Editions = new List<Edition> { new Edition { ForeignEditionId = id, Monitored = true } })
                        .Build();
                    return Tuple.Create(metadata.ForeignAuthorId, book, new List<AuthorMetadata> { metadata });
                });

            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.Find(It.IsAny<int>()))
                .Returns(new ImportListDefinition { EnableAutomaticAdd = true, ShouldMonitor = ImportListMonitorType.SpecificBook });

            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                .Returns(new List<IImportList> { mockImportList.Object });

            Mocker.GetMock<IFetchAndParseImportList>()
                .Setup(v => v.Fetch())
                .Returns(() => new List<ImportListFetchBatch> { new (new ImportListMembershipAttempt(0, 0, "fixture"), _importListReports) });

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(v => v.All())
                .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IAddBookService>()
                .Setup(v => v.AddBooks(It.IsAny<List<Book>>(), false))
                .Returns<List<Book>, bool>((x, y) => x);

            Mocker.GetMock<IAddAuthorService>()
                .Setup(v => v.AddAuthors(It.IsAny<List<Author>>(), false))
                .Returns<List<Author>, bool>((x, y) => x);
        }

        private void WithBook()
        {
            _importListReports.First().Book = "Meteora";
        }

        private void WithAuthorId()
        {
            _importListReports.First().AuthorGoodreadsId = "audible:us:B000000001";
        }

        private void WithBookId()
        {
            _importListReports.First().EditionGoodreadsId = "audible:us:B000000002";
        }

        private void WithSecondBook()
        {
            var importListItem2 = new ImportListItemInfo
            {
                Author = "Linkin Park",
                AuthorGoodreadsId = "audible:us:B000000001",
                Book = "Meteora 2",
                EditionGoodreadsId = "audible:us:B000000003",
                BookGoodreadsId = "audible:us:B000000003"
            };
            _importListReports.Add(importListItem2);
        }

        private void WithExistingAuthor()
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.FindById(_importListReports.First().AuthorGoodreadsId))
                .Returns(new Author { Id = 1, ForeignAuthorId = _importListReports.First().AuthorGoodreadsId });
        }

        private void WithExistingBook()
        {
            Mocker.GetMock<IBookService>()
                .Setup(v => v.FindById("audible:us:B000000002"))
                .Returns(new Book { Id = 1, ForeignBookId = _importListReports.First().BookGoodreadsId });
        }

        private void WithExcludedAuthor()
        {
            Mocker.GetMock<IImportListExclusionService>()
                .Setup(v => v.All())
                .Returns(new List<ImportListExclusion>
                {
                    new ImportListExclusion
                    {
                        ForeignId = "audible:us:B000000001"
                    }
                });
        }

        private void WithExcludedBook()
        {
            Mocker.GetMock<IImportListExclusionService>()
                .Setup(v => v.All())
                .Returns(new List<ImportListExclusion>
                {
                    new ImportListExclusion
                    {
                        ForeignId = "audible:us:B000000002"
                    }
                });
        }

        private void WithMonitorType(ImportListMonitorType monitor)
        {
            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.Find(It.IsAny<int>()))
                .Returns(new ImportListDefinition { EnableAutomaticAdd = true, ShouldMonitor = monitor });
        }

        [Test]
        public void should_search_if_author_title_and_no_author_id()
        {
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewAuthor>()
                .Verify(v => v.SearchForNewAuthor(It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_not_search_if_author_title_and_author_id()
        {
            WithAuthorId();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewAuthor>()
                .Verify(v => v.SearchForNewAuthor(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_search_if_book_title_and_no_book_id()
        {
            WithBook();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(v => v.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true), Times.Once());
        }

        [Test]
        public void should_not_search_if_book_title_and_book_id()
        {
            WithAuthorId();
            WithBookId();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(v => v.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void should_not_search_if_all_info()
        {
            WithAuthorId();
            WithBook();
            WithBookId();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(v => v.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true), Times.Never());

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(v => v.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void should_not_add_if_existing_author()
        {
            WithAuthorId();
            WithExistingAuthor();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAuthorService>()
                .Verify(v => v.AddAuthors(It.Is<List<Author>>(t => t.Count == 0), false));
        }

        [Test]
        public void should_not_add_if_existing_book()
        {
            WithBookId();
            WithExistingBook();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAuthorService>()
                .Verify(v => v.AddAuthors(It.Is<List<Author>>(t => t.Count == 0), false));
        }

        [Test]
        public void should_add_if_existing_author_but_new_book()
        {
            WithBookId();
            WithAuthorId();
            WithExistingAuthor();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddBookService>()
                .Verify(v => v.AddBooks(It.Is<List<Book>>(t => t.Count == 1), false));
        }

        [TestCase(ImportListMonitorType.None, false)]
        [TestCase(ImportListMonitorType.SpecificBook, true)]
        [TestCase(ImportListMonitorType.EntireAuthor, true)]
        public void should_add_if_not_existing_author(ImportListMonitorType monitor, bool expectedAuthorMonitored)
        {
            WithAuthorId();
            WithMonitorType(monitor);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAuthorService>()
                .Verify(v => v.AddAuthors(It.Is<List<Author>>(t => t.Count == 1 && t.First().Monitored == expectedAuthorMonitored), false));
        }

        [TestCase(ImportListMonitorType.None, false)]
        [TestCase(ImportListMonitorType.SpecificBook, true)]
        [TestCase(ImportListMonitorType.EntireAuthor, true)]
        public void should_add_if_not_existing_book(ImportListMonitorType monitor, bool expectedBookMonitored)
        {
            WithBookId();
            WithMonitorType(monitor);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddBookService>()
                .Verify(v => v.AddBooks(It.Is<List<Book>>(t => t.Count == 1 && t.First().Monitored == expectedBookMonitored), false));
        }

        [Test]
        public void should_not_add_author_if_excluded_author()
        {
            WithAuthorId();
            WithExcludedAuthor();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAuthorService>()
                .Verify(v => v.AddAuthors(It.Is<List<Author>>(t => t.Count == 0), false));
        }

        [Test]
        public void should_not_add_book_if_excluded_book()
        {
            WithBookId();
            WithExcludedBook();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddBookService>()
                .Verify(v => v.AddBooks(It.Is<List<Book>>(t => t.Count == 0), false));
        }

        [Test]
        public void should_not_add_book_if_excluded_author()
        {
            WithBookId();
            WithAuthorId();
            WithExcludedAuthor();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddBookService>()
                .Verify(v => v.AddBooks(It.Is<List<Book>>(t => t.Count == 0), false));
        }

        [TestCase(ImportListMonitorType.None, 0, false)]
        [TestCase(ImportListMonitorType.SpecificBook, 2, true)]
        [TestCase(ImportListMonitorType.EntireAuthor, 0, true)]
        public void should_add_two_books(ImportListMonitorType monitor, int expectedBooksMonitored, bool expectedAuthorMonitored)
        {
            WithBook();
            WithBookId();
            WithSecondBook();
            WithAuthorId();
            WithMonitorType(monitor);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddBookService>()
                .Verify(v => v.AddBooks(It.Is<List<Book>>(t => t.Count == 2), false));
            Mocker.GetMock<IAddAuthorService>()
                .Verify(v => v.AddAuthors(It.Is<List<Author>>(t => t.Count == 1 &&
                                                                   t.First().AddOptions.BooksToMonitor.Count == expectedBooksMonitored &&
                                                                   t.First().Monitored == expectedAuthorMonitored), false));
        }

        [Test]
        public void should_retain_conflicting_regional_source_ids_without_adding_a_recording()
        {
            WithBook();
            WithAuthorId();
            var report = _importListReports.First();
            report.BookGoodreadsId = "audible:us:B000000002";
            report.EditionGoodreadsId = "audible:uk:B000000002";

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IImportListMembershipRepository>().Verify(repository => repository.Complete(It.IsAny<ImportListMembershipAttempt>(), It.Is<IReadOnlyList<ImportListMembershipItem>>(items =>
                items.Count == 1 && items[0].SourceBookId == "audible:us:B000000002" && items[0].SourceEditionId == "audible:uk:B000000002" && items[0].BookId == null && items[0].EditionId == null && items[0].AuthorId == null)));
            Mocker.GetMock<IAddBookService>().Verify(service => service.AddBooks(It.IsAny<List<Book>>(), false), Times.Never());
            Mocker.GetMock<IAddAuthorService>().Verify(service => service.AddAuthors(It.IsAny<List<Author>>(), false), Times.Never());
            Mocker.GetMock<IProvideBookInfo>().Verify(service => service.GetBookInfo(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_reject_a_lookup_that_changes_the_exact_source_recording()
        {
            WithBook();
            var report = _importListReports.First();
            report.BookGoodreadsId = "audible:us:B000000002";
            var metadata = new AuthorMetadata { ForeignAuthorId = "audible:us:B000000001", Name = report.Author };
            var mapped = new Book
            {
                ForeignBookId = "audible:uk:B000000002",
                Title = report.Book,
                AuthorMetadata = metadata,
                Editions = new List<Edition> { new Edition { ForeignEditionId = "audible:uk:B000000002", Monitored = true } }
            };
            Mocker.GetMock<IProvideBookInfo>().Setup(service => service.GetBookInfo("audible:us:B000000002"))
                .Returns(Tuple.Create(metadata.ForeignAuthorId, mapped, new List<AuthorMetadata> { metadata }));

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IImportListMembershipRepository>().Verify(repository => repository.Complete(It.IsAny<ImportListMembershipAttempt>(), It.Is<IReadOnlyList<ImportListMembershipItem>>(items =>
                items.Count == 1 && items[0].SourceBookId == "audible:us:B000000002" && items[0].BookId == null && items[0].EditionId == null && items[0].AuthorId == null)));
            Mocker.GetMock<IAddBookService>().Verify(service => service.AddBooks(It.IsAny<List<Book>>(), false), Times.Never());
        }

        [Test]
        public void should_not_fetch_if_no_lists_are_enabled()
        {
            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                .Returns(new List<IImportList>());

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IFetchAndParseImportList>()
                .Verify(v => v.Fetch(), Times.Never);
        }

        [Test]
        public void should_not_process_if_no_items_are_returned()
        {
            Mocker.GetMock<IFetchAndParseImportList>()
                .Setup(v => v.Fetch())
                .Returns(new List<ImportListFetchBatch>());

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IImportListExclusionService>()
                .Verify(v => v.All(), Times.Never);
        }
    }
}
