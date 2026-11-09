using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookRichMetadataFixture : CoreTest
    {
        private static AudnexusBookResource Recording() => new AudnexusBookResource
        {
            Asin = "B08G9PRS1K",
            Region = "us",
            Title = "Published title",
            Authors = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = "Author One", Asin = "B000000001" }
            }
        };

        [Test]
        public void should_keep_full_summary_and_remove_markup_scripts_and_retailer_calls_to_action()
        {
            var resource = Recording();
            resource.Description = "An interrupted description...";
            resource.Summary = "<p>A complete &amp; useful premise.</p><script>unwanted()</script><p>Another paragraph.</p><p>Buy now at Audible.</p>";

            var overview = AudiobookMetadataMapper.Book(resource, "us").Editions.Value.Single().Overview;

            overview.Should().Be("A complete & useful premise.\n\nAnother paragraph.");
            resource.Summary.Should().Contain("<script>");
        }

        [Test]
        public void should_fall_back_to_description_when_summary_has_no_synopsis()
        {
            AudiobookSynopsis.Select("<p>Buy now</p>", "A complete premise.").Should().Be("A complete premise.");
            AudiobookSynopsis.Clean("&lt;script&gt;unwanted()&lt;/script&gt;<p>The story.</p>").Should().Be("The story.");
        }

        [Test]
        public void should_choose_actual_author_after_translator_and_preserve_all_recording_roles()
        {
            var resource = Recording();
            resource.Authors.Insert(0, new AudnexusContributorResource { Name = "Stephen Paul - translator", Asin = "B000000002" });
            resource.Authors.Add(new AudnexusContributorResource { Name = "Author Two", Asin = "B000000003" });
            resource.Authors.Add(new AudnexusContributorResource { Name = "Editor Name - editor" });
            resource.Narrators = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = "Anne-Marie Reader - narrator", Asin = "B000000004" }
            };

            var book = AudiobookMetadataMapper.Book(resource, "us");
            var edition = book.Editions.Value.Single();

            book.AuthorMetadata.Value.Name.Should().Be("Author One");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be("audible:us:B000000001");
            edition.Contributors.Should().HaveCount(5);
            edition.Contributors.Should().Contain(credit => credit.Name == "Stephen Paul" && credit.Role == "Translator" && credit.Asin == "B000000002");
            edition.Contributors.Should().Contain(credit => credit.Name == "Author Two" && credit.Role == "Author");
            edition.Contributors.Should().Contain(credit => credit.Name == "Editor Name" && credit.Role == "Editor");
            edition.Narrators.Should().Equal("Anne-Marie Reader");
            resource.Authors[0].Name.Should().Be("Stephen Paul - translator");
        }

        [Test]
        public void should_preserve_established_author_when_coauthor_credits_are_reordered()
        {
            var resource = Recording();
            var established = AudiobookMetadataMapper.Book(resource, "us").AuthorMetadata.Value;
            resource.Authors.Insert(0, new AudnexusContributorResource { Name = "Another Author", Asin = "B000000002" });

            var book = AudiobookMetadataMapper.Book(resource, "us", established);

            book.AuthorMetadata.Value.Should().BeSameAs(established);
            book.Editions.Value.Single().Contributors.Should().HaveCount(2);
        }

        [Test]
        public void should_not_reuse_established_author_when_only_their_translator_credit_matches()
        {
            var resource = Recording();
            var established = AudiobookMetadataMapper.Book(resource, "us").AuthorMetadata.Value;
            resource.Authors[0].Name += " - translator";
            resource.Authors.Insert(0, new AudnexusContributorResource { Name = "Another Author", Asin = "B000000002" });

            AudiobookMetadataMapper.Book(resource, "us", established).AuthorMetadata.Value.ForeignAuthorId.Should().Be("audible:us:B000000002");
        }

        [Test]
        public void should_not_invent_an_author_from_an_editor_or_translator_only_credit()
        {
            var resource = Recording();
            resource.Authors = new List<AudnexusContributorResource>
            {
                new AudnexusContributorResource { Name = "An Editor - editor" }
            };

            Assert.Throws<BookInfoException>(() => AudiobookMetadataMapper.Book(resource, "us"));
        }

        [Test]
        public void should_keep_distinct_taxonomy_ids_and_types_even_when_display_names_repeat()
        {
            var resource = Recording();
            resource.Genres = new List<AudnexusGenreResource>
            {
                new AudnexusGenreResource { Asin = "123", Name = " Fantasy ", Type = "genre" },
                new AudnexusGenreResource { Asin = "456", Name = "Fantasy", Type = "tag" }
            };
            resource.Copyright = 2021;
            resource.IsAdult = false;
            resource.LiteratureType = "Fiction";

            var book = AudiobookMetadataMapper.Book(resource, "us");
            book.Genres.Should().Equal("Fantasy");
            book.Classifications.Should().HaveCount(2);
            book.Classifications.Should().Contain(item => item.Asin == "456" && item.Type == "tag");
            var edition = book.Editions.Value.Single();
            edition.CopyrightYear.Should().Be(2021);
            edition.IsAdult.Should().BeFalse();
            edition.LiteratureType.Should().Be("fiction");
        }

        [Test]
        public void should_retain_author_genres_and_similar_author_identifiers()
        {
            var author = AudiobookMetadataMapper.Author(new AudnexusAuthorResource
            {
                Asin = "B000000001", Name = "Author One", Description = "<p>Biography.</p>",
                Genres = new List<AudnexusGenreResource> { new AudnexusGenreResource { Name = "Fantasy" } },
                Similar = new List<AudnexusContributorResource>
                {
                    new AudnexusContributorResource { Asin = "B000000002", Name = "Another Author" }
                }
            }, "us");

            author.Genres.Should().Equal("Fantasy");
            author.SimilarAuthors.Should().ContainSingle(person => person.Asin == "B000000002" && person.Name == "Another Author");
            author.Overview.Should().Be("Biography.");
        }

        [Test]
        public void should_preserve_source_placeholder_without_scheduling_it_as_a_real_release()
        {
            var resource = Recording();
            resource.ReleaseDate = new DateTime(2050, 12, 30);
            var book = AudiobookMetadataMapper.Book(resource, "us");
            book.ReleaseDate.Should().BeNull();
            var edition = book.Editions.Value.Single();
            edition.ReleaseDate.Should().BeNull();
            edition.SourceReleaseDate.Should().Be(new DateTime(2050, 12, 30));
            edition.ReleaseDateIsPlaceholder.Should().BeTrue();

            resource.ReleaseDate = new DateTime(2050, 12, 31);
            book = AudiobookMetadataMapper.Book(resource, "us");
            book.ReleaseDate.Should().Be(new DateTime(2050, 12, 31));
            book.Editions.Value.Single().ReleaseDateIsPlaceholder.Should().BeFalse();
        }

        private static AudnexusChapterResource Chapters() => new AudnexusChapterResource
        {
            Asin = "B08G9PRS1K", Region = "us", RuntimeLengthMs = 5000, IsAccurate = true,
            Chapters = new List<AudnexusChapter>
            {
                new AudnexusChapter { Title = "Opening Credits", StartOffsetMs = 0, LengthMs = 2000 },
                new AudnexusChapter { Title = "Chapter One", StartOffsetMs = 2000, LengthMs = 3000 }
            }
        };

        [Test]
        public void should_preserve_chapter_timings_titles_and_precise_runtime()
        {
            var result = AudiobookMetadataMapper.Chapters(Chapters(), "B08G9PRS1K", "us");
            result.RuntimeLengthMs.Should().Be(5000);
            result.Chapters.Select(chapter => chapter.Ordinal).Should().Equal(1, 2);
            result.Chapters[1].StartOffsetMs.Should().Be(2000);
            result.Chapters[1].LengthMs.Should().Be(3000);
            result.IsAccurate.Should().BeTrue();
        }

        [TestCase("B000000001", "us")]
        [TestCase("B08G9PRS1K", "uk")]
        public void should_not_attach_another_recording_or_marketplaces_chapters(string asin, string region)
        {
            Assert.Throws<BookInfoException>(() => AudiobookMetadataMapper.Chapters(Chapters(), asin, region));
        }

        [TestCase(-1, 3000)]
        [TestCase(1000, 3000)]
        [TestCase(2000, -1)]
        [TestCase(2000, 4000)]
        [TestCase(long.MaxValue, 1)]
        public void should_reject_negative_overlapping_or_out_of_runtime_chapters(long offset, long length)
        {
            var chapters = Chapters();
            chapters.Chapters[1].StartOffsetMs = offset;
            chapters.Chapters[1].LengthMs = length;
            Assert.Throws<BookInfoException>(() => AudiobookMetadataMapper.Chapters(chapters, "B08G9PRS1K", "us"));
        }
    }
}
