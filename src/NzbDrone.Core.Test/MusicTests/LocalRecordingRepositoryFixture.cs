using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class LocalRecordingRepositoryFixture : DbTest<LocalRecordingRepository, BookFile>
    {
        private Book Book() => new Book { Author = new Author { Id = 1 }, AuthorMetadataId = 1, ForeignBookId = LocalRecordingId.NewId(), Title = "Kaiju", TitleSlug = Guid.NewGuid().ToString(), CleanTitle = "kaiju", Added = DateTime.UtcNow };
        private Edition Edition() => new Edition { ForeignEditionId = LocalRecordingId.NewId(), Title = "Kaiju", TitleSlug = Guid.NewGuid().ToString(), Monitored = true };
        private BookFile File(string path) => new BookFile { Path = path, Modified = DateTime.UtcNow, DateAdded = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), SceneName = "original", ReleaseGroup = "group", Size = 12, Part = 1, Quality = new QualityModel(Quality.MP3) };

        [SetUp]
        public void Setup()
        {
            Db.Insert(new AuthorMetadata { ForeignAuthorId = "local:author:existing", TitleSlug = "existing-author", Name = "Matt Dinniman", SortName = "Matt Dinniman", NameLastFirst = "Dinniman, Matt", SortNameLastFirst = "Dinniman, Matt" });
            Db.Insert(new Author { AuthorMetadataId = 1, Path = "/books/Matt Dinniman", CleanName = "mattdinniman", Added = DateTime.UtcNow });
        }

        [Test]
        public void should_link_files_and_preserve_import_history_in_one_transaction()
        {
            var file = File("/books/Kaiju/01.mp3");
            Storage.Insert(file);
            var book = Book();
            var edition = Edition();
            Subject.Create(book, edition, new List<BookFile> { StoredModel });
            StoredModel.EditionId.Should().Be(edition.Id);
            StoredModel.Id.Should().Be(file.Id);
            StoredModel.DateAdded.Should().Be(file.DateAdded);
            StoredModel.SceneName.Should().Be("original");
            StoredModel.ReleaseGroup.Should().Be("group");
            Db.All<Book>().Single().Id.Should().Be(book.Id);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_compare_legacy_sqlite_timestamp_by_value_without_losing_guard(bool changedAfterPreview)
        {
            if (Db.DatabaseType != DatabaseType.SQLite)
            {
                Assert.Ignore("This fixture covers legacy SQLite timestamp encodings.");
            }

            var file = File("/books/Kaiju/01.mp3");
            Storage.Insert(file);
            const string originalModified = "2026-11-25 00:34:49.199266";
            using var connection = Db.OpenConnection();
            connection.Execute("UPDATE \"BookFiles\" SET \"Modified\" = @Modified WHERE \"Id\" = @Id", new { Modified = originalModified, file.Id });
            var selected = Storage.Get(file.Id);
            var book = Book();
            var edition = Edition();

            if (changedAfterPreview)
            {
                connection.Execute("UPDATE \"BookFiles\" SET \"Modified\" = @Modified WHERE \"Id\" = @Id", new { Modified = "2026-11-25 00:34:50.199266", file.Id });
                Assert.Throws<NzbDroneClientException>(() => Subject.Create(book, edition, new List<BookFile> { selected }));
                Db.All<Book>().Should().BeEmpty();
                Db.All<Edition>().Should().BeEmpty();
                Storage.Get(file.Id).EditionId.Should().Be(0);
            }
            else
            {
                Subject.Create(book, edition, new List<BookFile> { selected });
                Storage.Get(file.Id).EditionId.Should().Be(edition.Id);
                connection.ExecuteScalar<string>("SELECT CAST(\"Modified\" AS TEXT) FROM \"BookFiles\" WHERE \"Id\" = @Id", new { file.Id }).Should().Be(originalModified);
            }
        }

        [Test]
        public void should_rollback_books_editions_and_first_file_if_another_file_was_claimed()
        {
            var first = File("/books/Kaiju/01.mp3");
            var second = File("/books/Kaiju/02.mp3");
            Storage.InsertMany(new List<BookFile> { first, second });
            var selected = Storage.All().OrderBy(file => file.Id).ToList();
            second.EditionId = 99;
            Storage.Update(second);
            Assert.Throws<NzbDroneClientException>(() => Subject.Create(Book(), Edition(), selected));
            Db.All<Book>().Should().BeEmpty();
            Db.All<Edition>().Should().BeEmpty();
            Storage.Get(first.Id).EditionId.Should().Be(0);
            Storage.Get(second.Id).EditionId.Should().Be(99);
        }

        [Test]
        public void should_update_local_metadata_atomically_and_reject_stale_revision()
        {
            var file = File("/books/Matt Dinniman/Kaiju/01.mp3");
            Storage.Insert(file);
            var book = Book();
            var edition = Edition();
            edition.ForeignEditionId = book.ForeignBookId;
            edition.TitleSlug = book.TitleSlug;
            edition.Narrators = new List<string> { "Narrator One", "Narrator Two" };
            var provenance = new Dictionary<string, string> { ["title"] = "fileTags", ["narrators"] = "fileTags" };
            Subject.Create(book, edition, new List<BookFile> { StoredModel }, provenance);

            var original = Subject.Get(book.Id);
            original.Revision.Should().NotBeNullOrEmpty();
            original.RecordingId.Should().Be(book.ForeignBookId);
            original.FileIds.Should().Equal(file.Id);
            original.AuthorName.Should().Be("Matt Dinniman");
            original.Provenance["title"].Should().Be("fileTags");
            original.Year.Should().BeNull();

            var stale = Subject.Get(book.Id);
            original.Title = "Edited title";
            original.Narrators = new List<string> { "Second", "First" };
            original.Year = 2021;
            original.ChapterMetadata = new AudiobookChapterMetadata
            {
                RuntimeLengthMs = 3000,
                Chapters = new List<AudiobookChapter>
                {
                    new AudiobookChapter { Ordinal = 0, Title = "Chapter zero", StartOffsetMs = 0, LengthMs = 1000 },
                    new AudiobookChapter { Ordinal = 1, Title = "Chapter one", StartOffsetMs = 1000, LengthMs = 2000 }
                }
            };

            Subject.Update(book.Id, original);
            var saved = Subject.Get(book.Id);
            saved.Revision.Should().NotBe(original.Revision);
            saved.Title.Should().Be("Edited title");
            saved.RecordingId.Should().Be(book.ForeignBookId);
            saved.EditionId.Should().Be(edition.Id);
            saved.FileIds.Should().Equal(file.Id);
            saved.Narrators.Should().Equal("Second", "First");
            saved.Year.Should().Be(2021);
            saved.ChapterMetadata.Chapters.Select(chapter => chapter.Ordinal).Should().Equal(0, 1);
            saved.Provenance["title"].Should().Be("manual");
            saved.Provenance["chapters"].Should().Be("manual");
            saved.Provenance["publisher"].Should().Be("unknown");
            Db.All<Edition>().Single().ReleaseDateIsPlaceholder.Should().BeTrue();
            Db.All<Book>().Single().TitleSlug.Should().Be(book.TitleSlug);
            StoredModel.EditionId.Should().Be(edition.Id);

            stale.Title = "Stale change";
            Assert.Throws<NzbDroneClientException>(() => Subject.Update(book.Id, stale));
            Subject.Get(book.Id).Title.Should().Be("Edited title");
            Db.All<Book>().Single().Title.Should().Be("Edited title");
        }

        [Test]
        public void legacy_local_recording_can_be_edited_without_losing_chapters_or_unknown_year()
        {
            var book = Book();
            var edition = Edition();
            edition.ForeignEditionId = book.ForeignBookId;
            Db.Insert(book);
            edition.BookId = book.Id;
            edition.ChapterMetadata = new AudiobookChapterMetadata
            {
                Chapters = new List<AudiobookChapter>
                {
                    new AudiobookChapter { Ordinal = 0, Title = "Original", LengthMs = 1000 }
                }
            };
            Db.Insert(edition);

            var legacy = Subject.Get(book.Id);
            legacy.Revision.Should().StartWith("legacy:");
            legacy.Provenance["chapters"].Should().Be("legacy");
            legacy.Provenance["year"].Should().Be("unknown");
            legacy.Publisher = "Manual publisher";
            Subject.Update(book.Id, legacy);

            var edited = Subject.Get(book.Id);
            edited.Provenance["chapters"].Should().Be("legacy");
            edited.Provenance["publisher"].Should().Be("manual");
            edited.ChapterMetadata.Chapters.Single().Ordinal.Should().Be(0);
            edited.Year.Should().BeNull();
        }

        [Test]
        public async Task concurrent_first_edits_of_legacy_metadata_allow_only_one_revision()
        {
            var book = Book();
            var edition = Edition();
            edition.ForeignEditionId = book.ForeignBookId;
            Db.Insert(book);
            edition.BookId = book.Id;
            Db.Insert(edition);
            var repository = Subject;
            var first = repository.Get(book.Id);
            var second = repository.Get(book.Id);
            first.Revision.Should().StartWith("legacy:");
            first.Title = "First edit";
            second.Title = "Second edit";

            using var barrier = new Barrier(2);
            Task<HttpStatusCode> Apply(LocalRecording recording) => Task.Run(() =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Both editing transactions did not start.");
                }

                try
                {
                    repository.Update(book.Id, recording);
                    return HttpStatusCode.OK;
                }
                catch (NzbDroneClientException exception)
                {
                    return exception.StatusCode;
                }
            });

            var statuses = await Task.WhenAll(Apply(first), Apply(second));
            statuses.Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            repository.Get(book.Id).Revision.Should().NotStartWith("legacy:");
            Db.All<Book>().Single().Title.Should().Be(Db.All<Edition>().Single().Title);
        }

        [Test]
        public void mixed_bulk_updates_preserve_local_metadata_and_update_nonlocal_rows()
        {
            var book = Book();
            var edition = Edition();
            edition.ForeignEditionId = book.ForeignBookId;
            Db.Insert(book);
            edition.BookId = book.Id;
            Db.Insert(edition);
            var nativeBook = Book();
            nativeBook.ForeignBookId = "audible:us:B011111111";
            Db.Insert(nativeBook);
            var nativeEdition = Edition();
            nativeEdition.ForeignEditionId = nativeBook.ForeignBookId;
            nativeEdition.BookId = nativeBook.Id;
            Db.Insert(nativeEdition);

            var staleBook = Db.All<Book>().Single(item => item.Id == book.Id);
            var staleEdition = Db.All<Edition>().Single(item => item.Id == edition.Id);
            var metadata = Subject.Get(book.Id);
            metadata.Title = "Edited title";
            metadata.Narrators = new List<string> { "Correct narrator" };
            Subject.Update(book.Id, metadata);

            staleBook.AuthorMetadataId = 2;
            staleBook.Monitored = true;
            staleEdition.Monitored = false;
            nativeBook.Title = "Updated native book";
            nativeEdition.Title = "Updated native edition";
            Mocker.Resolve<BookRepository>().UpdateMany(new List<Book> { staleBook, nativeBook });
            Mocker.Resolve<EditionRepository>().UpdateMany(new List<Edition> { staleEdition, nativeEdition });

            var storedBook = Db.All<Book>().Single(item => item.Id == book.Id);
            storedBook.Title.Should().Be("Edited title");
            storedBook.AuthorMetadataId.Should().Be(2);
            storedBook.Monitored.Should().BeTrue();
            var storedEdition = Db.All<Edition>().Single(item => item.Id == edition.Id);
            storedEdition.Title.Should().Be("Edited title");
            storedEdition.Narrators.Should().Equal("Correct narrator");
            storedEdition.Monitored.Should().BeFalse();
            Db.All<Book>().Single(item => item.Id == nativeBook.Id).Title.Should().Be("Updated native book");
            Db.All<Edition>().Single(item => item.Id == nativeEdition.Id).Title.Should().Be("Updated native edition");
        }

        [Test]
        public void local_author_creation_rolls_back_with_conflicting_file_claim()
        {
            var quality = new QualityProfile { Name = "Owned quality", Items = new List<QualityProfileQualityItem>() };
            var metadata = new MetadataProfile { Name = "Owned metadata" };
            Db.Insert(quality);
            Db.Insert(metadata);
            var root = new RootFolder { Path = "/books", Name = "Owned root", DefaultQualityProfileId = quality.Id, DefaultMetadataProfileId = metadata.Id };
            Db.Insert(root);

            var author = new Author
            {
                Path = "/books/New Author", CleanName = "newauthor", QualityProfileId = quality.Id, MetadataProfileId = metadata.Id,
                Metadata = new AuthorMetadata { ForeignAuthorId = LocalAuthorId.NewId(), TitleSlug = "a-new-local-author", Name = "New Author", SortName = "New Author", NameLastFirst = "Author, New", SortNameLastFirst = "Author, New" }
            };
            var book = Book();
            book.Author = author;
            var edition = Edition();
            edition.ForeignEditionId = book.ForeignBookId;
            var first = File("/books/New Author/Kaiju/01.mp3");
            var second = File("/books/New Author/Kaiju/02.mp3");
            Storage.InsertMany(new List<BookFile> { first, second });
            var selected = Storage.All().OrderBy(file => file.Id).ToList();
            second.EditionId = 99;
            Storage.Update(second);

            Assert.Throws<NzbDroneClientException>(() => Subject.Create(book, edition, selected, null, root));
            Db.All<Author>().Should().ContainSingle();
            Db.All<AuthorMetadata>().Should().ContainSingle();
            Db.All<Book>().Should().BeEmpty();
            Db.All<Edition>().Should().BeEmpty();
            Storage.Get(first.Id).EditionId.Should().Be(0);
        }
    }
}
