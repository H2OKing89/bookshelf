using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using Readarr.Api.V1.Author;
using Readarr.Api.V1.Books;
using Readarr.Http.REST;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class AudiobookMetadataResourceFixture
    {
        [Test]
        public void should_expose_selected_recording_enrichment_without_embedding_chapter_lists_in_book_results()
        {
            var book = new Book
            {
                Title = "A recording",
                ForeignBookId = "audible:us:B000000001",
                Classifications = new List<AudiobookClassification> { new AudiobookClassification { Name = "Fantasy", Asin = "100", Type = "genre" } }
            };
            var edition = EnrichedEdition();
            edition.Book = book;
            book.Editions = new List<Edition> { edition };

            var resource = book.ToResource();

            resource.Contributors.Should().BeEquivalentTo(edition.Contributors);
            resource.Classifications.Should().BeEquivalentTo(book.Classifications);
            resource.RuntimeLengthMs.Should().Be(60000);
            resource.RuntimeLengthMinutes.Should().Be(1);
            resource.ChapterCount.Should().Be(1);
            resource.CopyrightYear.Should().Be(2021);
            resource.IsAdult.Should().BeFalse();
            resource.LiteratureType.Should().Be("fiction");
            resource.SourceReleaseDate.Should().Be(edition.SourceReleaseDate);
            resource.ReleaseDateIsPlaceholder.Should().BeTrue();
            resource.Overview.Should().Be("A complete synopsis.");
            resource.Publisher.Should().Be("A publisher");
            resource.Language.Should().Be("eng");
            resource.ToJson().Should().NotContain("\"chapterMetadata\"");
        }

        [Test]
        public void should_round_trip_enriched_edition_through_api_json()
        {
            var edition = EnrichedEdition();

            var resource = Json.Deserialize<EditionResource>(edition.ToResource().ToJson());
            var restored = resource.ToModel();

            restored.Contributors.Should().BeEquivalentTo(edition.Contributors);
            restored.ChapterMetadata.Should().BeEquivalentTo(edition.ChapterMetadata);
            restored.CopyrightYear.Should().Be(2021);
            restored.IsAdult.Should().BeFalse();
            restored.LiteratureType.Should().Be("fiction");
            restored.SourceReleaseDate.Should().Be(edition.SourceReleaseDate);
            restored.ReleaseDateIsPlaceholder.Should().BeTrue();
            restored.Asin.Should().Be(edition.Asin);
            restored.Narrators.Should().Equal("A narrator");
        }

        [Test]
        public void should_round_trip_author_genres_and_related_authors_through_api_json()
        {
            var author = new NzbDrone.Core.Books.Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "An author",
                    ForeignAuthorId = "audible:us:B000000001",
                    Genres = new List<string> { "Fantasy" },
                    SimilarAuthors = new List<AudiobookPersonReference> { new AudiobookPersonReference { Name = "Another author", Asin = "B000000002" } }
                }
            };

            var restored = Json.Deserialize<AuthorResource>(author.ToResource().ToJson()).ToModel();

            restored.Metadata.Value.Genres.Should().Equal("Fantasy");
            restored.Metadata.Value.SimilarAuthors.Should().BeEquivalentTo(author.Metadata.Value.SimilarAuthors);
            restored.ForeignAuthorId.Should().Be(author.ForeignAuthorId);
        }

        [Test]
        public void should_preserve_provider_metadata_when_an_older_client_changes_edition_monitoring()
        {
            var edition = EnrichedEdition();
            edition.Id = 9;
            edition.BookId = 4;
            var book = new Book { Id = 4, ForeignBookId = edition.ForeignEditionId, Editions = new List<Edition> { edition } };
            var request = Json.Deserialize<BookResource>(@"{
                ""id"": 4, ""foreignBookId"": ""audible:us:B000000001"", ""monitored"": true,
                ""editions"": [{ ""id"": 9, ""bookId"": 4, ""foreignEditionId"": ""audible:us:B000000001"", ""monitored"": false }]
            }");

            request.ToModel(book);

            edition.Monitored.Should().BeFalse();
            edition.Contributors.Should().ContainSingle();
            edition.ChapterMetadata.Chapters.Should().ContainSingle();
            edition.ChapterMetadata.RuntimeLengthMs.Should().Be(60000);
            edition.CopyrightYear.Should().Be(2021);
            edition.IsAdult.Should().BeFalse();
            edition.LiteratureType.Should().Be("fiction");
            edition.ReleaseDateIsPlaceholder.Should().BeTrue();
            edition.SourceReleaseDate.Should().Be(new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc));
            edition.Overview.Should().Be("A complete synopsis.");
            edition.Publisher.Should().Be("A publisher");
            edition.Narrators.Should().Equal("A narrator");
            edition.Asin.Should().Be("B000000001");
            edition.BookId.Should().Be(4);
            book.Editions.Value.Should().ContainSingle().Which.Should().BeSameAs(edition);
        }

        [Test]
        public void should_reject_an_edition_identity_change_before_mutating_the_book()
        {
            var edition = EnrichedEdition();
            edition.Id = 9;
            edition.BookId = 4;
            var book = new Book { Id = 4, Monitored = true, ForeignBookId = edition.ForeignEditionId, Editions = new List<Edition> { edition } };
            var request = new BookResource
            {
                Id = 4,
                Monitored = false,
                ForeignBookId = book.ForeignBookId,
                Editions = new List<EditionResource> { new EditionResource { Id = 9, BookId = 4, Asin = "B000000099", Monitored = false } }
            };

            Action apply = () => request.ToModel(book);

            apply.Should().Throw<BadRequestException>();
            edition.Monitored.Should().BeTrue();
            edition.Asin.Should().Be("B000000001");
            book.Monitored.Should().BeTrue();
        }

        [Test]
        public void should_allow_switching_the_monitored_edition_without_replacing_provider_metadata()
        {
            var current = EnrichedEdition();
            current.Id = 9;
            current.BookId = 4;
            var alternate = EnrichedEdition();
            alternate.Id = 10;
            alternate.BookId = 4;
            alternate.Asin = "B000000002";
            alternate.ForeignEditionId = "audible:us:B000000002";
            alternate.Monitored = false;
            var book = new Book { Id = 4, ForeignBookId = current.ForeignEditionId, Editions = new List<Edition> { current, alternate } };
            var request = new BookResource
            {
                Id = 4,
                ForeignBookId = book.ForeignBookId,
                Editions = new List<EditionResource>
                {
                    new EditionResource { Id = 9, Monitored = false },
                    new EditionResource { Id = 10, Monitored = true }
                }
            };

            request.ToModel(book);

            current.Monitored.Should().BeFalse();
            alternate.Monitored.Should().BeTrue();
            alternate.ForeignEditionId.Should().Be("audible:us:B000000002");
            alternate.Asin.Should().Be("B000000002");
            current.ChapterMetadata.Chapters.Should().ContainSingle();
            alternate.ChapterMetadata.Chapters.Should().ContainSingle();
            book.Editions.Value.Should().HaveCount(2);
        }

        [Test]
        public void should_reject_null_edition_entries_before_mutating_the_book()
        {
            var edition = EnrichedEdition();
            edition.Id = 9;
            edition.BookId = 4;
            var book = new Book { Id = 4, Monitored = true, ForeignBookId = edition.ForeignEditionId, Editions = new List<Edition> { edition } };
            var request = Json.Deserialize<BookResource>(@"{
                ""id"": 4, ""foreignBookId"": ""audible:us:B000000001"", ""monitored"": false, ""editions"": [null]
            }");

            Action apply = () => request.ToModel(book);

            apply.Should().Throw<BadRequestException>();
            book.Monitored.Should().BeTrue();
            edition.Monitored.Should().BeTrue();
            edition.ChapterMetadata.Chapters.Should().ContainSingle();
        }

        private static Edition EnrichedEdition()
        {
            return new Edition
            {
                Title = "A recording",
                Asin = "B000000001",
                ForeignEditionId = "audible:us:B000000001",
                Monitored = true,
                Overview = "A complete synopsis.",
                Publisher = "A publisher",
                Language = "eng",
                Narrators = new List<string> { "A narrator" },
                RuntimeLengthMinutes = 1,
                CopyrightYear = 2021,
                IsAdult = false,
                LiteratureType = "fiction",
                SourceReleaseDate = new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc),
                ReleaseDateIsPlaceholder = true,
                Contributors = new List<AudiobookContributor> { new AudiobookContributor { Name = "An author", Asin = "B000000002", Role = "Author" } },
                ChapterMetadata = new AudiobookChapterMetadata
                {
                    RuntimeLengthMs = 60000,
                    IsAccurate = true,
                    Chapters = new List<AudiobookChapter> { new AudiobookChapter { Title = "Chapter 1", Ordinal = 1, StartOffsetMs = 0, LengthMs = 60000 } }
                }
            };
        }
    }
}
