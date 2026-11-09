using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class audiobook_metadata_completenessFixture : MigrationTest<audiobook_metadata_completeness>
    {
        [Test]
        public void should_preserve_existing_metadata_and_only_normalize_native_audible_language_and_known_placeholder()
        {
            var placeholder = new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc);
            var legitimateFuture = new DateTime(2028, 3, 28, 0, 0, 0, DateTimeKind.Utc);
            var db = WithMigrationTestDb(migration =>
            {
                migration.Insert.IntoTable("AuthorMetadata").Row(new
                {
                    Id = 7, ForeignAuthorId = "audible:us:B000000007", TitleSlug = "existing-author", Name = "Existing author",
                    Overview = "A saved biography", Images = "[]", Genres = "[\"Fantasy\"]", Status = 0, Aliases = "[]",
                    SortName = "existing author", NameLastFirst = "author, Existing", SortNameLastFirst = "author, existing"
                });

                foreach (var id in new[] { 1, 2, 3, 4 })
                {
                    migration.Insert.IntoTable("Books").Row(new
                    {
                        Id = id, AuthorMetadataId = 7, ForeignBookId = id == 3 ? "goodreads:3" : $"audible:us:B00000000{id}",
                        TitleSlug = $"book-{id}", Title = $"Existing book {id}", CleanTitle = $"book{id}",
                        ReleaseDate = id == 2 ? legitimateFuture : placeholder, Monitored = true, AnyEditionOk = false,
                        Genres = "[\"Fantasy\"]", RelatedBooks = "[]"
                    });
                    migration.Insert.IntoTable("Editions").Row(new
                    {
                        Id = id, BookId = id, ForeignEditionId = id == 3 ? "goodreads:edition3" : $"audible:us:B00000000{id}",
                        Asin = $"B00000000{id}", AudibleRegion = "us", Title = $"Existing recording {id}", TitleSlug = $"recording-{id}",
                        ReleaseDate = id == 2 ? legitimateFuture : id == 4 ? placeholder.AddDays(1) : placeholder,
                        Language = id == 4 ? "french" : "English", Images = "[]", Monitored = true, ManualAdd = false,
                        Narrators = "[\"Saved narrator\"]", RuntimeLengthMinutes = 600, Overview = "A saved overview"
                    });
                }
            });

            var editions = db.Query<Edition>("SELECT * FROM \"Editions\" ORDER BY \"Id\"").ToList();
            var books = db.Query<Book>("SELECT * FROM \"Books\" ORDER BY \"Id\"").ToList();
            var author = db.Query<PreservedAuthorMetadata>("SELECT \"Overview\", \"Genres\", \"SimilarAuthors\" FROM \"AuthorMetadata\"").Single();

            editions.Should().HaveCount(4);
            editions[0].ReleaseDate.Should().BeNull();
            editions[0].SourceReleaseDate.Should().Be(placeholder);
            editions[0].ReleaseDateIsPlaceholder.Should().BeTrue();
            books[0].ReleaseDate.Should().BeNull();
            editions[0].Language.Should().Be("eng");
            editions[1].ReleaseDate.Should().Be(legitimateFuture);
            editions[1].SourceReleaseDate.Should().BeNull();
            editions[1].ReleaseDateIsPlaceholder.Should().BeFalse();
            editions[1].Language.Should().Be("eng");
            books[1].ReleaseDate.Should().Be(legitimateFuture);
            editions[2].ReleaseDate.Should().Be(placeholder);
            editions[2].ReleaseDateIsPlaceholder.Should().BeFalse();
            editions[2].Language.Should().Be("English");
            books[2].ReleaseDate.Should().Be(placeholder);
            editions[3].ReleaseDate.Should().Be(placeholder.AddDays(1));
            editions[3].Language.Should().Be("french");
            books[3].ReleaseDate.Should().Be(placeholder);

            editions.Should().OnlyContain(edition => edition.Monitored && edition.Contributors.Count == 0 &&
                edition.ChapterMetadata == null && edition.CopyrightYear == null && edition.IsAdult == null &&
                edition.LiteratureType == null && edition.RuntimeLengthMinutes == 600 && edition.Overview == "A saved overview");
            editions.Should().OnlyContain(edition => edition.Narrators.Single() == "Saved narrator");
            books.Should().OnlyContain(book => book.AuthorMetadataId == 7 && book.Monitored && book.Classifications.Count == 0);
            author.Overview.Should().Be("A saved biography");
            author.Genres.Should().Equal("Fantasy");
            author.SimilarAuthors.Should().BeEmpty();
        }

        public class PreservedAuthorMetadata
        {
            public string Overview { get; set; }
            public List<string> Genres { get; set; }
            public List<AudiobookPersonReference> SimilarAuthors { get; set; }
        }
    }
}
