using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class QualityProfileRankFixture : DbTest<HistoryRepository, EntityHistory>
    {
        private QualityProfile _first;
        private QualityProfile _second;
        private Author _firstAuthor;
        private Author _secondAuthor;
        private Book _firstBook;
        private Book _secondBook;

        private static QualityProfileQualityItem Item(Quality quality) => new QualityProfileQualityItem { Quality = quality, Allowed = true };

        [SetUp]
        public void Setup()
        {
            _first = Db.Insert(new QualityProfile
            {
                Name = "Owned first profile", Items = new List<QualityProfileQualityItem>
                {
                    Item(Quality.FLAC),
                    new QualityProfileQualityItem { Id = 1000, Items = new List<QualityProfileQualityItem> { Item(Quality.MP3), Item(Quality.M4B) }, Allowed = true },
                    Item(Quality.EPUB)
                }
            });
            _second = Db.Insert(new QualityProfile
            {
                Name = "Owned second profile", Items = new List<QualityProfileQualityItem> { Item(Quality.M4B), Item(Quality.FLAC) }
            });
            _firstAuthor = AddAuthor(_first.Id);
            _secondAuthor = AddAuthor(_second.Id);
            _firstBook = AddBook(_firstAuthor);
            _secondBook = AddBook(_secondAuthor);
        }

        private Author AddAuthor(int profileId)
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "local:author:" + Guid.NewGuid().ToString("N"),
                Name = "Owned author", SortName = "Owned author", NameLastFirst = "Owned author", SortNameLastFirst = "Owned author", TitleSlug = Guid.NewGuid().ToString("N")
            });
            return Db.Insert(new Author { AuthorMetadataId = metadata.Id, Path = "/owned/" + metadata.Id, CleanName = "ownedauthor" + metadata.Id, QualityProfileId = profileId });
        }

        private Book AddBook(Author author)
        {
            return Db.Insert(new Book
            {
                AuthorMetadataId = author.AuthorMetadataId, ForeignBookId = "local:recording:" + Guid.NewGuid().ToString("N"),
                Title = "Owned recording", CleanTitle = "ownedrecording", TitleSlug = Guid.NewGuid().ToString("N")
            });
        }

        private EntityHistory History(Author author, Book book, Quality quality, EntityHistoryEventType eventType = EntityHistoryEventType.Grabbed)
        {
            return Subject.Insert(new EntityHistory
            {
                AuthorId = author.Id, BookId = book.Id, Date = DateTime.UtcNow, SourceTitle = "Owned recording", Quality = new QualityModel(quality), EventType = eventType
            });
        }

        [Test]
        public void history_sorts_full_filtered_result_by_own_profile_and_stable_group_ties()
        {
            var middleB = History(_firstAuthor, _firstBook, Quality.M4B);
            var highest = History(_secondAuthor, _secondBook, Quality.FLAC);
            var lowA = History(_firstAuthor, _firstBook, Quality.FLAC);
            var unknown = History(_secondAuthor, _secondBook, Quality.MP3);
            var middleA = History(_firstAuthor, _firstBook, Quality.MP3);
            var lowB = History(_secondAuthor, _secondBook, Quality.M4B);
            History(_firstAuthor, _firstBook, Quality.EPUB, EntityHistoryEventType.DownloadFailed);

            var page = new PagingSpec<EntityHistory> { Page = 1, PageSize = 2, SortKey = "quality", SortDirection = SortDirection.Descending };
            page.FilterExpressions.Add(row => row.EventType == EntityHistoryEventType.Grabbed);
            Subject.GetPaged(page).TotalRecords.Should().Be(6);
            page.Records.Select(row => row.Id).Should().Equal(highest.Id, middleB.Id);
            page.Page = 2;
            Subject.GetPaged(page).Records.Select(row => row.Id).Should().Equal(middleA.Id, lowA.Id);
            page.Page = 3;
            Subject.GetPaged(page).Records.Select(row => row.Id).Should().Equal(lowB.Id, unknown.Id);
            page.Page = 1;
            page.SortDirection = SortDirection.Ascending;
            Subject.GetPaged(page).Records.Select(row => row.Id).Should().Equal(unknown.Id, lowA.Id);
        }

        [Test]
        public void history_and_blocklist_use_new_profile_order_and_drop_removed_rank_without_rebuild()
        {
            var first = History(_firstAuthor, _firstBook, Quality.FLAC);
            var second = History(_firstAuthor, _firstBook, Quality.M4B);
            var historyPage = new PagingSpec<EntityHistory> { Page = 1, PageSize = 10, SortKey = "History.Quality", SortDirection = SortDirection.Descending };
            var blocklist = Mocker.Resolve<BlocklistRepository>();
            var blockedFirst = blocklist.Insert(new NzbDrone.Core.Blocklisting.Blocklist { AuthorId = _firstAuthor.Id, Quality = new QualityModel(Quality.FLAC), SourceTitle = "First", BookIds = new List<int> { _firstBook.Id } });
            var blockedSecond = blocklist.Insert(new NzbDrone.Core.Blocklisting.Blocklist { AuthorId = _firstAuthor.Id, Quality = new QualityModel(Quality.M4B), SourceTitle = "Second", BookIds = new List<int> { _firstBook.Id } });
            var blockPage = new PagingSpec<NzbDrone.Core.Blocklisting.Blocklist> { Page = 1, PageSize = 10, SortKey = "quality", SortDirection = SortDirection.Descending };
            Subject.GetPaged(historyPage).Records.Select(row => row.Id).Should().Equal(second.Id, first.Id);
            blocklist.GetPaged(blockPage).Records.Select(row => row.Id).Should().Equal(blockedSecond.Id, blockedFirst.Id);

            _first.Items.Reverse();
            Db.Update(_first);
            Subject.GetPaged(historyPage).Records.Select(row => row.Id).Should().Equal(first.Id, second.Id);
            blocklist.GetPaged(blockPage).Records.Select(row => row.Id).Should().Equal(blockedFirst.Id, blockedSecond.Id);
            Db.Delete(_first);

            // Both now unranked, so the stable id order remains and no old rank is reused.
            historyPage.SortDirection = SortDirection.Ascending;
            Subject.GetPaged(historyPage).Records.Select(row => row.Id).Should().Equal(first.Id, second.Id);
        }

        [Test]
        public void library_rank_represents_lowest_tracked_file_and_never_inflates_counts()
        {
            var edition = Db.Insert(new Edition { BookId = _firstBook.Id, ForeignEditionId = _firstBook.ForeignBookId, Title = _firstBook.Title, TitleSlug = _firstBook.TitleSlug, Monitored = true });
            Db.Insert(new Edition { BookId = _secondBook.Id, ForeignEditionId = _secondBook.ForeignBookId, Title = _secondBook.Title, TitleSlug = _secondBook.TitleSlug, Monitored = true });
            var flac = Db.Insert(new BookFile { EditionId = edition.Id, Quality = new QualityModel(Quality.FLAC), Path = "/owned/one", Size = 100 });
            Db.Insert(new BookFile { EditionId = edition.Id, Quality = new QualityModel(Quality.M4B), Path = "/owned/two", Size = 200 });
            var repository = Mocker.Resolve<AuthorStatisticsRepository>();
            var first = repository.AuthorStatistics(_firstAuthor.Id).Single();
            first.FileQualityRank.Should().Be(0);
            first.BookFileCount.Should().Be(2);
            first.SizeOnDisk.Should().Be(300);
            repository.AuthorStatistics(_secondAuthor.Id).Single().FileQualityRank.Should().BeNull();
            _first.Items.Reverse();
            Db.Update(_first);
            repository.AuthorStatistics(_firstAuthor.Id).Single().FileQualityRank.Should().Be(0.5);
            flac.Quality = new QualityModel(Quality.UnknownAudio);
            Db.Update(flac);
            repository.AuthorStatistics(_firstAuthor.Id).Single().FileQualityRank.Should().Be(-1);
        }

        [Test]
        public void in_memory_file_rank_matches_groups_single_item_and_missing_quality()
        {
            QualityProfileRank.GetRank(_first, Quality.FLAC).Should().Be(0);
            QualityProfileRank.GetRank(_first, Quality.MP3).Should().Be(0.5);
            QualityProfileRank.GetRank(_first, Quality.M4B).Should().Be(0.5);
            QualityProfileRank.GetRank(_first, Quality.EPUB).Should().Be(1);
            QualityProfileRank.GetRank(_first, Quality.UnknownAudio).Should().Be(-1);
            QualityProfileRank.GetRank(null, Quality.MP3).Should().Be(-1);
            QualityProfileRank.GetRank(new QualityProfile { Items = new List<QualityProfileQualityItem> { Item(Quality.FLAC) } }, Quality.FLAC).Should().Be(1);
        }
    }
}
