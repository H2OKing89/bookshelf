using System;
using System.Globalization;
using Dapper;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.RecordingReview;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class RecordingReviewRepositoryFixture : DbTest<RecordingReviewRepository, BookFile>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;

        [SetUp]
        public void Setup()
        {
            var metadata = Builder<AuthorMetadata>.CreateNew().With(model => model.Id = 0).Build();
            Db.Insert(metadata);
            _author = Builder<Author>.CreateNew().With(model => model.Id = 0)
                .With(model => model.AuthorMetadataId = metadata.Id).Build();
            Db.Insert(_author);
            _book = Builder<Book>.CreateNew().With(model => model.Id = 0)
                .With(model => model.AuthorMetadataId = metadata.Id).Build();
            Db.Insert(_book);
            _edition = Builder<Edition>.CreateNew().With(model => model.Id = 0)
                .With(model => model.BookId = _book.Id)
                .With(model => model.Asin = "B011111111")
                .With(model => model.ForeignEditionId = "audible:us:B011111111")
                .With(model => model.AudibleRegion = "us")
                .With(model => model.IsEbook = false).Build();
            Db.Insert(_edition);
        }

        [Test]
        public void should_save_one_exact_link_preserving_existing_file_provenance()
        {
            var file = AddFile("owned.m4b");
            var database = Mocker.Resolve<IMainDatabase>();
            if (database.DatabaseType == DatabaseType.SQLite)
            {
                // SQLite backups can use a plain UTC timestamp with a different
                // text representation from a typed DateTime query parameter.
                using var connection = database.OpenConnection();
                var storedTime = file.Modified.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                connection.Execute(@"UPDATE ""BookFiles"" SET ""Modified"" = @StoredTime WHERE ""Id"" = @Id", new { StoredTime = storedTime, file.Id });
            }

            var target = Subject.FindCatalogEditions(new[] { _edition.Asin }, new[] { _edition.ForeignEditionId }, 4)
                .Should().ContainSingle().Subject;

            Subject.Associate(file, target, _book, _author).Should().BeTrue();
            Subject.Associate(file, target, _book, _author).Should().BeFalse();

            var saved = Storage.Get(file.Id);
            saved.EditionId.Should().Be(_edition.Id);
            saved.Path.Should().Be(file.Path);
            saved.OriginalFilePath.Should().Be(file.OriginalFilePath);
            saved.ReleaseGroup.Should().Be(file.ReleaseGroup);
            saved.DateAdded.Should().Be(file.DateAdded);
            saved.Part.Should().Be(1);
        }

        [Test]
        public void should_refuse_a_changed_source_or_an_occupied_recording()
        {
            var file = AddFile("owned.m4b");
            var changed = Storage.Get(file.Id);
            changed.Size++;
            Storage.SetFields(changed, item => item.Size);
            Subject.Associate(file, _edition, _book, _author).Should().BeFalse();

            var occupant = AddFile("existing.m4b");
            occupant.EditionId = _edition.Id;
            Storage.SetFields(occupant, item => item.EditionId);
            Subject.Associate(changed, _edition, _book, _author).Should().BeFalse();
            Storage.Get(file.Id).EditionId.Should().Be(0);
        }

        private BookFile AddFile(string path)
        {
            var file = new BookFile
            {
                Path = path,
                OriginalFilePath = "original-" + path,
                ReleaseGroup = "Owned",
                DateAdded = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc),
                Modified = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc).AddTicks(3964430),
                Size = 500,
                Quality = new QualityModel(Quality.M4B)
            };
            Db.Insert(file);
            return file;
        }
    }
}
