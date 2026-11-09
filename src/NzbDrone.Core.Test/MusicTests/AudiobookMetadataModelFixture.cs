using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AudiobookMetadataModelFixture
    {
        [Test]
        public void should_initialize_additive_collections_without_inventing_recording_details()
        {
            new Book().Classifications.Should().BeEmpty();
            new AuthorMetadata().SimilarAuthors.Should().BeEmpty();
            var edition = new Edition();
            edition.Contributors.Should().BeEmpty();
            edition.ChapterMetadata.Should().BeNull();
            edition.IsAdult.Should().BeNull();
            edition.CopyrightYear.Should().BeNull();
            edition.SourceReleaseDate.Should().BeNull();
            edition.ReleaseDateIsPlaceholder.Should().BeFalse();
            new AudiobookChapterMetadata().Chapters.Should().BeEmpty();
        }

        [Test]
        public void should_copy_nested_metadata_without_aliasing_cached_provider_objects()
        {
            var incomingBook = new Book
            {
                Classifications = new List<AudiobookClassification>
                {
                    new AudiobookClassification { Name = "Fantasy", Type = "genre", Asin = "100" }
                }
            };
            var incomingEdition = new Edition
            {
                Contributors = new List<AudiobookContributor>
                {
                    new AudiobookContributor { Name = "An author", Asin = "B000000001", Role = "Author" }
                },
                ChapterMetadata = new AudiobookChapterMetadata
                {
                    RuntimeLengthMs = 60000,
                    Chapters = new List<AudiobookChapter>
                    {
                        new AudiobookChapter { Title = "Chapter 1", Ordinal = 1, StartOffsetMs = 0, LengthMs = 60000 }
                    }
                }
            };
            var incomingAuthor = new AuthorMetadata
            {
                SimilarAuthors = new List<AudiobookPersonReference>
                {
                    new AudiobookPersonReference { Name = "A related author", Asin = "B000000002" }
                }
            };
            var book = new Book();
            var edition = new Edition();
            var author = new AuthorMetadata();

            book.UseMetadataFrom(incomingBook);
            edition.UseMetadataFrom(incomingEdition);
            author.UseMetadataFrom(incomingAuthor);
            incomingBook.Classifications[0].Name = "Changed";
            incomingEdition.Contributors[0].Name = "Changed";
            incomingEdition.ChapterMetadata.Chapters[0].Title = "Changed";
            incomingAuthor.SimilarAuthors[0].Name = "Changed";

            book.Classifications[0].Name.Should().Be("Fantasy");
            edition.Contributors[0].Name.Should().Be("An author");
            edition.ChapterMetadata.Chapters[0].Title.Should().Be("Chapter 1");
            author.SimilarAuthors[0].Name.Should().Be("A related author");
        }

        [Test]
        public void should_preserve_enrichment_when_a_refresh_omits_optional_metadata()
        {
            var book = new Book
            {
                Classifications = new List<AudiobookClassification> { new AudiobookClassification { Name = "Fantasy" } }
            };
            var edition = new Edition
            {
                Contributors = new List<AudiobookContributor> { new AudiobookContributor { Name = "An author", Role = "Author" } },
                ChapterMetadata = new AudiobookChapterMetadata { RuntimeLengthMs = 60000 },
                CopyrightYear = 2021,
                IsAdult = true,
                LiteratureType = "fiction"
            };
            var author = new AuthorMetadata
            {
                SimilarAuthors = new List<AudiobookPersonReference> { new AudiobookPersonReference { Name = "A related author" } }
            };

            book.UseMetadataFrom(new Book());
            edition.UseMetadataFrom(new Edition());
            author.UseMetadataFrom(new AuthorMetadata());

            book.Classifications.Should().ContainSingle();
            edition.Contributors.Should().ContainSingle();
            edition.ChapterMetadata.RuntimeLengthMs.Should().Be(60000);
            edition.CopyrightYear.Should().Be(2021);
            edition.IsAdult.Should().BeTrue();
            edition.LiteratureType.Should().Be("fiction");
            author.SimilarAuthors.Should().ContainSingle();
        }

        [Test]
        public void should_accept_false_adult_flag_and_replace_placeholder_with_a_confirmed_release_date()
        {
            var placeholder = new DateTime(2050, 12, 30, 0, 0, 0, DateTimeKind.Utc);
            var confirmed = new DateTime(2027, 3, 9, 0, 0, 0, DateTimeKind.Utc);
            var edition = new Edition { SourceReleaseDate = placeholder, ReleaseDateIsPlaceholder = true, IsAdult = true };

            edition.UseMetadataFrom(new Edition { SourceReleaseDate = confirmed, ReleaseDate = confirmed, IsAdult = false });

            edition.ReleaseDate.Should().Be(confirmed);
            edition.SourceReleaseDate.Should().Be(confirmed);
            edition.ReleaseDateIsPlaceholder.Should().BeFalse();
            edition.IsAdult.Should().BeFalse();
        }
    }
}
