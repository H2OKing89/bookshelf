using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Readarr;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListPolicyRepositoryFixture : DbTest<ImportListPolicyRepository, Book>
    {
        private ImportListDefinition _first;
        private ImportListDefinition _second;
        private Author _author;
        private Book _recording;
        private int _manualTag;
        private int _syncTag;

        [SetUp]
        public void Setup()
        {
            _manualTag = Db.Insert(new Tag { Label = "manual" }).Id;
            _syncTag = Db.Insert(new Tag { Label = "list" }).Id;
            var metadata = Db.Insert(new AuthorMetadata
            {
                Name = "Owned author", SortName = "Owned author", NameLastFirst = "Owned author", SortNameLastFirst = "Owned author", TitleSlug = "owned-author",
                ForeignAuthorId = "audible:us:B055555555"
            });
            _author = Db.Insert(new Author { AuthorMetadataId = metadata.Id, Path = "/owned/author", CleanName = "ownedauthor", Tags = new HashSet<int> { _manualTag } });
            _first = AddList("First");
            _second = AddList("Second");
            _recording = AddRecording("audible:us:B011111111", _first.Id);
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthor(It.IsAny<int>())).Returns(_author);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBook(It.IsAny<int>())).Returns(_recording);
        }

        [Test]
        public void should_keep_manual_tags_and_remove_a_shared_sync_tag_only_after_all_lists_leave()
        {
            Complete(_first, Entry(_recording));
            Complete(_second, Entry(_recording));
            Subject.ReconcileTags(new[] { _first.Id, _second.Id });
            Tags().Should().BeEquivalentTo(new[] { _manualTag, _syncTag });
            Claims().Should().Be(2);
            Complete(_first);
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().Contain(_syncTag);
            Membership.Begin(_second).Should().NotBeNull();
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().Contain(_syncTag);
            Complete(_second);
            Subject.ReconcileTags(new[] { _second.Id });
            Tags().Should().BeEquivalentTo(new[] { _manualTag });
            Claims().Should().Be(0);
        }

        [Test]
        public void should_never_claim_a_preexisting_tag_and_keep_add_tags_permanently()
        {
            WriteTags(_manualTag, _syncTag);
            Complete(_first, Entry(_recording));
            Complete(_second);
            Subject.ReconcileTags(new[] { _first.Id, _second.Id });
            Claims().Should().Be(0);
            Complete(_first);
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().Contain(_syncTag);

            WriteTags(_manualTag);
            _first = ChangeMode(_first, ImportListTagExisting.Add);
            Complete(_first, Entry(_recording));
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().Contain(_syncTag);
            Claims().Should().Be(0);
            Complete(_first);
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().Contain(_syncTag);
        }

        [Test]
        public void should_relinquish_explicitly_reasserted_tag_and_preserve_it_after_list_leaves()
        {
            Complete(_first, Entry(_recording));
            Complete(_second);
            Subject.ReconcileTags(new[] { _first.Id, _second.Id });
            Claims().Should().Be(1);
            Subject.ReleaseAuthorOwnership(_author.Id, false, new[] { _syncTag });
            Claims().Should().Be(0);
            Complete(_first);
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().Contain(_syncTag);
        }

        [Test]
        public void should_keep_list_tags_after_manual_tag_change_and_protect_manual_removal_and_readdition()
        {
            Complete(_first, Entry(_recording));
            Complete(_second);
            Subject.ReconcileTags(new[] { _first.Id, _second.Id });
            Claims().Should().Be(1);
            var extra = Db.Insert(new Tag { Label = "another-manual" }).Id;
            WriteTags(_manualTag, _syncTag, extra);
            Claims().Should().Be(1);
            WriteTags(_manualTag, extra);
            Claims().Should().Be(0);
            WriteTags(_manualTag, extra, _syncTag);
            Complete(_first);
            Subject.ReconcileTags(new[] { _first.Id });
            Tags().Should().BeEquivalentTo(new[] { _manualTag, extra, _syncTag });
        }

        [Test]
        public void should_not_use_a_recording_membership_with_a_contradictory_catalog_author()
        {
            var item = Entry(_recording);
            item.AuthorId = "audible:us:B099999999";
            item.SourceAuthorId = item.AuthorId;
            Complete(_first, item);
            Complete(_second);
            Subject.ReconcileTags(new[] { _first.Id, _second.Id });
            Claims().Should().Be(0);
            Subject.Preview(ImportListCleanupMode.Preview, null).Blockers.Should().NotBeEmpty();
        }

        [Test]
        public void should_keep_claimed_tag_when_list_is_disabled_deleted_or_mode_changed()
        {
            Complete(_first, Entry(_recording));
            Complete(_second);
            Subject.ReconcileTags(new[] { _first.Id, _second.Id });
            _first = ChangeMode(_first, ImportListTagExisting.None);
            Complete(_first);
            Subject.ReconcileTags(new[] { _first.Id });
            Claims().Should().Be(0);
            Tags().Should().Contain(_syncTag);

            WriteTags(_manualTag);
            _first = ChangeMode(_first, ImportListTagExisting.Sync);
            Complete(_first, Entry(_recording));
            Subject.ReconcileTags(new[] { _first.Id });
            Claims().Should().Be(1);
            Mocker.Resolve<ImportListRepository>().Delete(_first.Id);
            Complete(_second);
            Subject.ReconcileTags(new[] { _second.Id });
            Claims().Should().Be(0);
            Tags().Should().Contain(_syncTag);
        }

        [Test]
        public void should_only_preview_former_list_created_recordings_and_require_current_confirmation()
        {
            var manual = AddRecording("audible:us:B022222222", null);
            var local = AddRecording("local:recording:" + Guid.NewGuid().ToString("N"), _first.Id);
            var stillListed = AddRecording("audible:us:B033333333", _first.Id);
            Complete(_first);
            Complete(_second, Entry(stillListed));
            var preview = Subject.Preview(ImportListCleanupMode.Unmonitor, null);
            preview.Blockers.Should().BeEmpty();
            preview.Candidates.Select(candidate => candidate.BookId).Should().Equal(_recording.Id);
            preview.ConfirmationToken.Should().NotBeNullOrEmpty();
            ReadBook(_recording.Id).Monitored.Should().BeTrue();
            var receipt = Subject.Apply(Request(preview));
            receipt.Candidates.Select(candidate => candidate.BookId).Should().Equal(_recording.Id);
            receipt.ConfirmationToken.Should().BeNull();
            ReadBook(_recording.Id).Monitored.Should().BeFalse();
            ReadBook(manual.Id).Monitored.Should().BeTrue();
            ReadBook(local.Id).Monitored.Should().BeTrue();
            ReadBook(stillListed.Id).Monitored.Should().BeTrue();
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Candidates.Should().BeEmpty();
        }

        [Test]
        public void should_block_a_cleanup_on_failed_stale_or_incomplete_list_and_reject_old_confirmation()
        {
            Complete(_first);
            Complete(_second);
            var preview = Subject.Preview(ImportListCleanupMode.Unmonitor, null);
            preview.Candidates.Should().ContainSingle();
            Membership.Begin(_second);
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Blockers.Should().NotBeEmpty();
            Assert.Throws<NzbDroneClientException>(() => Subject.Apply(Request(preview))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            Complete(_second);
            using var connection = Main.OpenConnection();
            connection.Execute("UPDATE \"ImportListMembershipSnapshots\" SET \"LastSuccessfulSync\" = @Date WHERE \"ImportListId\" = @Id",
                new { Date = DateTime.UtcNow.AddDays(-2), _second.Id });
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Blockers.Should().NotBeEmpty();
            Complete(_second);
            var unresolved = new ImportListMembershipItem { SourceTitle = "Unresolved recording", SourceAuthor = "Unknown" };
            Complete(_second, unresolved);
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Blockers.Should().NotBeEmpty();
            ReadBook(_recording.Id).Monitored.Should().BeTrue();
        }

        [Test]
        public void should_never_unmonitor_after_explicit_same_value_monitor_edit_or_monitor_change()
        {
            Complete(_first);
            Complete(_second);
            var preview = Subject.Preview(ImportListCleanupMode.Unmonitor, null);
            Subject.ReleaseBookOwnership(new[] { _recording.Id });
            Assert.Throws<NzbDroneClientException>(() => Subject.Apply(Request(preview))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            ReadBook(_recording.Id).Monitored.Should().BeTrue();
            var changed = AddRecording("audible:us:B022222222", _first.Id);
            using var connection = Main.OpenConnection();
            connection.Execute("UPDATE \"Books\" SET \"Monitored\" = FALSE WHERE \"Id\" = @Id", changed);
            connection.Execute("UPDATE \"Books\" SET \"Monitored\" = TRUE WHERE \"Id\" = @Id", changed);
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Candidates.Should().BeEmpty();
        }

        [Test]
        public void should_tag_only_confirmed_affected_authors_without_unmonitoring_any_book()
        {
            Complete(_first);
            Complete(_second);
            var preview = Subject.Preview(ImportListCleanupMode.Tag, _syncTag);
            preview.Candidates.Should().ContainSingle();
            Subject.Apply(Request(preview));
            Tags().Should().BeEquivalentTo(new[] { _manualTag, _syncTag });
            ReadBook(_recording.Id).Monitored.Should().BeTrue();
            Claims().Should().Be(0);
            Subject.Preview(ImportListCleanupMode.Tag, _syncTag).Candidates.Should().BeEmpty();
        }

        [Test]
        public void should_protect_author_wide_memberships_and_exact_exclusions()
        {
            Complete(_first);
            Complete(_second, new ImportListMembershipItem { SourceAuthor = "Owned author", AuthorId = "audible:us:B055555555", SourceAuthorId = "audible:us:B055555555" });
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Candidates.Should().BeEmpty();
            Complete(_second);
            using var connection = Main.OpenConnection();
            connection.Execute("INSERT INTO \"ImportListExclusions\" (\"ForeignId\", \"Name\") VALUES (@ForeignBookId, 'Owned exclusion')", _recording);
            Subject.Preview(ImportListCleanupMode.Unmonitor, null).Candidates.Should().BeEmpty();
        }

        private IMainDatabase Main => Mocker.Resolve<IMainDatabase>();
        private ImportListMembershipRepository Membership => Mocker.Resolve<ImportListMembershipRepository>();

        private ImportListDefinition AddList(string name) => Mocker.Resolve<ImportListRepository>().Insert(new ImportListDefinition
        {
            Name = name, Implementation = "ReadarrImport", ConfigContract = "ReadarrSettings", Settings = new ReadarrSettings { BaseUrl = "http://127.0.0.1:1234", ApiKey = "owned" },
            EnableAutomaticAdd = true, TagExisting = ImportListTagExisting.Sync, Tags = new HashSet<int> { _manualTag, _syncTag },
            RootFolderPath = "/owned", ProfileId = 1, MetadataProfileId = 1
        });

        private ImportListDefinition ChangeMode(ImportListDefinition list, ImportListTagExisting mode)
        {
            var repository = Mocker.Resolve<ImportListRepository>();
            list.TagExisting = mode;
            repository.Update(list);
            return repository.Get(list.Id);
        }

        private Book AddRecording(string id, int? origin)
        {
            var book = Db.Insert(new Book
            {
                AuthorMetadataId = _author.AuthorMetadataId, ForeignBookId = id, Title = id, CleanTitle = id, TitleSlug = Guid.NewGuid().ToString("N"),
                Monitored = true, Added = DateTime.UtcNow, ImportListOriginId = origin
            });
            var native = id.StartsWith("audible:", StringComparison.Ordinal);
            Db.Insert(new Edition
            {
                BookId = book.Id, ForeignEditionId = id, Title = id, TitleSlug = book.TitleSlug, Monitored = true,
                AudibleRegion = native ? id.Split(':')[1] : null, Asin = native ? id.Split(':')[2] : null
            });
            return book;
        }

        private static ImportListMembershipItem Entry(Book book) => new ()
        {
            SourceTitle = book.Title, SourceAuthor = "Owned author",
            SourceBookId = book.ForeignBookId, SourceEditionId = book.ForeignBookId, SourceAuthorId = "audible:us:B055555555",
            BookId = book.ForeignBookId, EditionId = book.ForeignBookId, AuthorId = "audible:us:B055555555"
        };

        private void Complete(ImportListDefinition list, params ImportListMembershipItem[] items) => Membership.Complete(Membership.Begin(list), items).Should().BeTrue();

        private HashSet<int> Tags()
        {
            using var connection = Main.OpenConnection();
            return connection.QuerySingle<HashSet<int>>("SELECT \"Tags\" FROM \"Authors\" WHERE \"Id\" = @Id", _author);
        }

        private void WriteTags(params int[] tags)
        {
            using var connection = Main.OpenConnection();
            connection.Execute("UPDATE \"Authors\" SET \"Tags\" = @Tags WHERE \"Id\" = @Id", new { Tags = System.Text.Json.JsonSerializer.Serialize(tags), _author.Id });
        }

        private int Claims()
        {
            using var connection = Main.OpenConnection();
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"ImportListTagOwners\"");
        }

        private Book ReadBook(int id)
        {
            using var connection = Main.OpenConnection();
            return connection.QuerySingle<Book>("SELECT * FROM \"Books\" WHERE \"Id\" = @Id", new { Id = id });
        }

        private static ImportListCleanupRequest Request(ImportListCleanupPreview preview) =>
            new () { Mode = preview.Mode, TagId = preview.TagId, ConfirmationToken = preview.ConfirmationToken };
    }
}
