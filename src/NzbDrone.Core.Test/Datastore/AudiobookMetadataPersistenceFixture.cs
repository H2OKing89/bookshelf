using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class AudiobookMetadataPersistenceFixture : DbTest
    {
        [Test]
        public void should_round_trip_recording_credits_chapters_and_optional_scalars()
        {
            var edition = new Edition
            {
                ForeignEditionId = "audible:us:B08G9PRS1K",
                Asin = "B08G9PRS1K",
                AudibleRegion = "us",
                Title = "A recording",
                TitleSlug = "a-recording",
                Narrators = new List<string> { "A narrator" },
                RuntimeLengthMinutes = 970,
                CopyrightYear = 2021,
                IsAdult = false,
                LiteratureType = "fiction",
                SourceReleaseDate = new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc),
                ReleaseDateIsPlaceholder = true,
                Contributors = new List<AudiobookContributor>
                {
                    new AudiobookContributor { Name = "First author", Asin = "B000000001", Role = "Author" },
                    new AudiobookContributor { Name = "Second author", Asin = "B000000002", Role = "Author" },
                    new AudiobookContributor { Name = "A narrator", Asin = "B000000003", Role = "Narrator" }
                },
                ChapterMetadata = new AudiobookChapterMetadata
                {
                    RuntimeLengthMs = 2500000000L,
                    BrandIntroDurationMs = 3970,
                    BrandOutroDurationMs = 0,
                    IsAccurate = true,
                    Chapters = new List<AudiobookChapter>
                    {
                        new AudiobookChapter { Title = "Opening", Ordinal = 1, StartOffsetMs = 0, LengthMs = 2500000000L }
                    }
                }
            };

            Db.Insert(edition);

            var stored = Db.Single<Edition>();
            stored.Contributors.Should().BeEquivalentTo(edition.Contributors, options => options.WithStrictOrdering());
            stored.ChapterMetadata.Should().BeEquivalentTo(edition.ChapterMetadata);
            stored.CopyrightYear.Should().Be(2021);
            stored.IsAdult.Should().BeFalse();
            stored.LiteratureType.Should().Be("fiction");
            stored.SourceReleaseDate.Should().Be(edition.SourceReleaseDate);
            stored.ReleaseDate.Should().BeNull();
            stored.ReleaseDateIsPlaceholder.Should().BeTrue();
            stored.Narrators.Should().Equal("A narrator");
            stored.RuntimeLengthMinutes.Should().Be(970);
            stored.Asin.Should().Be("B08G9PRS1K");
        }

        [Test]
        public void should_round_trip_typed_taxonomy_without_changing_flat_genres_or_primary_owner()
        {
            var book = new Book
            {
                ForeignBookId = "audible:us:B08G9PRS1K",
                Title = "A recording",
                TitleSlug = "a-recording",
                CleanTitle = "arecording",
                AuthorMetadataId = 27,
                Genres = new List<string> { "Science Fiction", "Space Opera" },
                Classifications = new List<AudiobookClassification>
                {
                    new AudiobookClassification { Name = "Science Fiction", Asin = "18580606011", Type = "genre" },
                    new AudiobookClassification { Name = "Space Opera", Asin = "18580608011", Type = "tag" }
                }
            };

            Db.Insert(book);

            var stored = Db.Single<Book>();
            stored.Classifications.Should().BeEquivalentTo(book.Classifications, options => options.WithStrictOrdering());
            stored.Genres.Should().Equal("Science Fiction", "Space Opera");
            stored.AuthorMetadataId.Should().Be(27);
            stored.ForeignBookId.Should().Be(book.ForeignBookId);
        }

        [Test]
        public void should_round_trip_author_genres_and_related_author_references()
        {
            var author = new AuthorMetadata
            {
                ForeignAuthorId = "audible:us:B000000001",
                Name = "An author",
                TitleSlug = "an-author",
                SortName = "an author",
                NameLastFirst = "author, An",
                SortNameLastFirst = "author, an",
                Genres = new List<string> { "Fantasy" },
                SimilarAuthors = new List<AudiobookPersonReference>
                {
                    new AudiobookPersonReference { Name = "A related author", Asin = "B000000002" }
                }
            };

            Db.Insert(author);

            var stored = Db.Single<AuthorMetadata>();
            stored.Genres.Should().Equal("Fantasy");
            stored.SimilarAuthors.Should().BeEquivalentTo(author.SimilarAuthors);
        }
    }
}
