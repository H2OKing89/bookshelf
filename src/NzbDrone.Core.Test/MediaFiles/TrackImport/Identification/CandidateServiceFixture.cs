using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class CandidateServiceFixture : CoreTest<CandidateService>
    {
        private static LocalEdition AudioBook(string asin, string title = "English title", string album = "日本語のアルバム")
        {
            var filename = string.Concat(title.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
            return new LocalEdition(new List<LocalBook>
            {
                new LocalBook
                {
                    Path = Path.Combine(Path.GetTempPath(), "bookshelf-candidate-fixture", $"{filename} [{asin}].m4b"),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = title,
                        BookTitle = album,
                        Authors = new List<string> { "Miya Kazuki" }
                    }
                }
            });
        }

        private static Edition Recording(string asin, string title, string region = "us")
        {
            var author = new Author { Id = 126, AuthorMetadataId = 125, Name = "Miya Kazuki", ForeignAuthorId = $"audible:{region}:B00M9KPHF2" };
            var book = new Book { Id = 3671, Title = title, ForeignBookId = $"audible:{region}:{asin}", Author = author, AuthorMetadataId = 125, AuthorMetadata = author.Metadata };
            var edition = new Edition { Id = 3671, BookId = book.Id, Book = book, Asin = asin, AudibleRegion = region, Title = title, ForeignEditionId = book.ForeignBookId, Language = "eng" };
            book.Editions = new List<Edition> { edition };
            return edition;
        }

        private (LocalEdition Local, Edition Recording) GivenProtectedLocalRecording()
        {
            var edition = Recording(null, "User-confirmed local title");
            edition.ForeignEditionId = "local:recording:01a2781fd6c14f41821ad36307456f47";
            edition.Book.Value.ForeignBookId = edition.ForeignEditionId;
            var local = AudioBook(null, "Recording", "A closer Audible catalog title");
            var file = new BookFile { Id = 900, Path = local.LocalBooks[0].Path, EditionId = edition.Id, Edition = edition };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(file.Path)).Returns(file);
            return (local, edition);
        }

        private Edition GivenUnlinkedLocalCandidate(string title)
        {
            var recording = Recording(null, title);
            recording.ForeignEditionId = "local:recording:01a2781fd6c14f41821ad36307456f47";
            recording.Book.Value.ForeignBookId = recording.ForeignEditionId;
            var book = recording.Book.Value;
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetCandidates(It.IsAny<string>())).Returns(new List<Author> { book.Author.Value });
            Mocker.GetMock<IBookService>().Setup(service => service.GetCandidates(It.IsAny<int>(), It.IsAny<string>())).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(book.Id)).Returns(new List<Edition> { recording });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetCandidates(It.IsAny<int>(), It.IsAny<string>())).Returns(new List<Edition> { recording });
            return recording;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_match_an_unlinked_performance_to_a_local_recording_by_title_and_author(bool authorOverride)
        {
            var recording = GivenUnlinkedLocalCandidate("A shared title");
            var local = AudioBook(null, recording.Title, recording.Title);
            var overrides = authorOverride ? new IdentificationOverrides { Author = recording.Book.Value.Author.Value } : null;

            Subject.GetDbCandidatesFromTags(local, overrides, false).Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_allow_explicit_local_recording_selection_for_unlinked_files(bool bookOverride)
        {
            var recording = GivenUnlinkedLocalCandidate("A shared title");
            var local = AudioBook(null, recording.Title, recording.Title);
            var overrides = bookOverride ? new IdentificationOverrides { Book = recording.Book.Value } : new IdentificationOverrides { Edition = recording };

            Subject.GetDbCandidatesFromTags(local, overrides, false).Should().ContainSingle().Which.Edition.Should().BeSameAs(recording);
        }

        [Test]
        public void should_not_reintroduce_an_unlinked_local_recording_from_remote_search()
        {
            var recording = GivenUnlinkedLocalCandidate("A shared title");
            var local = AudioBook(null, recording.Title, recording.Title);
            Mocker.GetMock<ISearchForNewBook>().Setup(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book> { recording.Book.Value });

            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();
        }

        [Test]
        public void should_preserve_a_local_recording_over_a_closer_audible_candidate_during_rescan()
        {
            var (local, recording) = GivenProtectedLocalRecording();
            var audible = Recording("B0CR45YNXY", "A closer Audible catalog title");
            Mocker.GetMock<IBookService>().Setup(service => service.GetCandidates(It.IsAny<int>(), It.IsAny<string>())).Returns(new List<Book> { audible.Book.Value });
            Mocker.GetMock<ISearchForNewBook>().Setup(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true)).Returns(new List<Book> { audible.Book.Value });

            Subject.GetDbCandidatesFromTags(local, null, false).Should().ContainSingle().Which.Edition.Should().BeSameAs(recording);
            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();

            Mocker.GetMock<IBookService>().Verify(service => service.GetCandidates(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFileWithPath(local.LocalBooks[0].Path), Times.Exactly(2));
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFileWithPath(It.IsAny<List<string>>()), Times.Never());
        }

        [Test]
        public void should_preserve_local_recordings_when_windows_path_casing_changes()
        {
            WindowsOnly();
            var (local, recording) = GivenProtectedLocalRecording();
            var storedPath = local.LocalBooks[0].Path;
            local.LocalBooks[0].Path = storedPath.ToUpperInvariant();
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>()))
                .Returns(new List<BookFile> { new BookFile { Path = storedPath, EditionId = recording.Id, Edition = recording } });

            Subject.GetDbCandidatesFromTags(local, null, false).Should().ContainSingle().Which.Edition.Should().BeSameAs(recording);

            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFileWithPath(It.Is<List<string>>(paths => paths.Count == 1 && paths[0] == local.LocalBooks[0].Path)), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_require_review_if_asin_tags_are_added_to_a_local_recording(bool conflicting)
        {
            var (local, _) = GivenProtectedLocalRecording();
            local.LocalBooks[0].FileTrackInfo.Asin = "B0CR45YNXY";
            local.LocalBooks[0].FileTrackInfo.AsinConflict = conflicting;
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId("audible:us:B0CR45YNXY"))
                .Returns(Recording("B0CR45YNXY", "Audible recording"));

            Subject.GetDbCandidatesFromTags(local, null, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();

            Mocker.GetMock<IEditionService>().Verify(service => service.GetEditionByForeignEditionId(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchByAsin(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_combine_a_local_recording_with_unmapped_files_during_rescan()
        {
            var (local, _) = GivenProtectedLocalRecording();
            local.LocalBooks.AddRange(AudioBook(null, "Another recording", "A closer Audible catalog title").LocalBooks);

            Subject.GetDbCandidatesFromTags(local, null, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();
        }

        [Test]
        public void should_not_combine_different_confirmed_local_recordings()
        {
            var (local, _) = GivenProtectedLocalRecording();
            var another = AudioBook(null, "Another recording", "A closer Audible catalog title").LocalBooks[0];
            local.LocalBooks.Add(another);
            var second = Recording(null, "Another local performance");
            second.ForeignEditionId = "local:recording:54bd184e86cb4fd5aac03af969f7e2b4";
            second.Book.Value.ForeignBookId = second.ForeignEditionId;
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(another.Path))
                .Returns(new BookFile { Path = another.Path, EditionId = second.Id, Edition = second });

            Subject.GetDbCandidatesFromTags(local, null, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();
        }

        [Test]
        public void should_not_automatically_apply_a_different_book_override_to_a_local_recording()
        {
            var (local, _) = GivenProtectedLocalRecording();
            var overrides = new IdentificationOverrides { Book = Recording("B0CR45YNXY", "Audible recording").Book.Value };

            Subject.GetDbCandidatesFromTags(local, overrides, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(local, overrides).Should().BeEmpty();
        }

        [TestCase("B0CR45YNXY", "Ascendance of a Bookworm: Part 1 Volume 1", "本好きの下剋上～司書になるためには手段を選んでいられません～第一部「兵士の娘1」")]
        [TestCase("B0DFHMW24T", "Ascendance of a Bookworm: Part 1 Volume 2", "本好きの下剋上~司書になるためには手段を選んでいられません~第一部「兵士の娘2」")]
        public void should_identify_exact_english_recording_despite_japanese_album(string asin, string title, string album)
        {
            var edition = Recording(asin, title);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId($"audible:us:{asin}")).Returns(edition);

            var candidates = Subject.GetDbCandidatesFromTags(AudioBook(asin, title, album), new IdentificationOverrides { Author = edition.Book.Value.Author.Value }, false);

            candidates.Should().ContainSingle().Which.Edition.Should().BeSameAs(edition);
            Mocker.GetMock<IBookService>().Verify(service => service.GetCandidates(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(service => service.GetCandidates(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase("The Tower of Swallows")]
        [TestCase("The Tower of Swallows (Full Cast Edition)")]
        [TestCase("Crossroads of Ravens (Full Cast Edition)")]
        public void should_not_choose_different_volume_or_cast_when_exact_recording_is_absent(string misleadingTitle)
        {
            var misleading = Recording("B0HHDLR3ZK", misleadingTitle);
            Mocker.GetMock<ISearchForNewBook>().Setup(service => service.SearchByAsin("B01DDZ3KHI")).Returns(new List<Book> { misleading.Book.Value });

            Subject.GetDbCandidatesFromTags(AudioBook("B01DDZ3KHI", misleadingTitle), null, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(AudioBook("B01DDZ3KHI", misleadingTitle), null).Should().BeEmpty();

            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_use_fuzzy_candidates_for_conflicting_explicit_identifiers()
        {
            var local = AudioBook("B0CR45YNXY");
            local.LocalBooks[0].FileTrackInfo.Asin = "B0DFHMW24T";

            Subject.GetDbCandidatesFromTags(local, null, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();

            local.LocalBooks[0].FileTrackInfo.AsinConflict.Should().BeTrue();
            Mocker.GetMock<IEditionService>().Verify(service => service.GetEditionByForeignEditionId(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchByAsin(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_choose_one_of_multiple_asins_in_grouped_files()
        {
            var local = AudioBook("B0CR45YNXY");
            local.LocalBooks.AddRange(AudioBook("B0DFHMW24T").LocalBooks);

            Subject.GetDbCandidatesFromTags(local, null, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(local, null).Should().BeEmpty();
        }

        [Test]
        public void should_respect_selected_marketplace_without_filtering_language()
        {
            var edition = Recording("B07R6K1H2Z", "本好きの下剋上", "jp");
            edition.Language = "jpn";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudnexusRegion).Returns("jp");
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId(edition.ForeignEditionId)).Returns(edition);

            Subject.GetDbCandidatesFromTags(AudioBook(edition.Asin), null, false).Select(candidate => candidate.Edition).Should().Equal(edition);
        }

        [Test]
        public void should_search_the_explicit_override_marketplace_instead_of_the_global_marketplace()
        {
            var edition = Recording("B07R6K1H2Z", "本好きの下剋上", "jp");
            edition.Language = "jpn";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudnexusRegion).Returns("us");
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(service => service.SearchForNewBook(edition.ForeignEditionId, null, true))
                .Returns(new List<Book> { edition.Book.Value });

            var candidates = Subject.GetRemoteCandidates(AudioBook(edition.Asin), new IdentificationOverrides { Book = edition.Book.Value }).ToList();

            candidates.Should().ContainSingle().Which.Edition.Should().BeSameAs(edition);
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchByAsin(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchForNewBook(edition.ForeignEditionId, null, true), Times.Once());
        }

        [Test]
        public void should_find_an_exact_regional_recording_under_an_author_added_in_another_marketplace()
        {
            var uk = Recording("B0F14RFHS6", "Philosopher's Stone", "uk");
            var usAuthor = Recording("B0F14RPXHR", "Sorcerer's Stone", "us").Book.Value.Author.Value;
            Mocker.GetMock<IConfigService>().SetupGet(config => config.AudnexusRegion).Returns("us");
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId(uk.ForeignEditionId)).Returns(uk);
            Mocker.GetMock<ISearchForNewBook>().Setup(service => service.SearchByAsin(uk.Asin)).Returns(new List<Book> { uk.Book.Value });
            var overrides = new IdentificationOverrides { Author = usAuthor };

            Subject.GetDbCandidatesFromTags(AudioBook(uk.Asin), overrides, false).Should().ContainSingle().Which.Edition.Should().BeSameAs(uk);
            Subject.GetRemoteCandidates(AudioBook(uk.Asin), overrides).Should().ContainSingle().Which.Edition.Should().BeSameAs(uk);

            uk.ForeignEditionId.Should().Be("audible:uk:B0F14RFHS6");
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_use_a_matching_author_name_to_cross_explicit_contributor_identities()
        {
            var uk = Recording("B0F14RFHS6", "Philosopher's Stone", "uk");
            var usAuthor = Recording("B0F14RPXHR", "Sorcerer's Stone", "us").Book.Value.Author.Value;
            usAuthor.ForeignAuthorId = "audible:us:B000000001";
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId(uk.ForeignEditionId)).Returns(uk);
            Mocker.GetMock<ISearchForNewBook>().Setup(service => service.SearchByAsin(uk.Asin)).Returns(new List<Book> { uk.Book.Value });
            var overrides = new IdentificationOverrides { Author = usAuthor };

            Subject.GetDbCandidatesFromTags(AudioBook(uk.Asin), overrides, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(AudioBook(uk.Asin), overrides).Should().BeEmpty();
        }

        [Test]
        public void should_not_cross_an_explicit_recording_marketplace_even_for_the_same_asin()
        {
            var uk = Recording("B0F14RFHS6", "Philosopher's Stone", "uk");
            var forced = Recording(uk.Asin, "Explicit US selection", "us");
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId(uk.ForeignEditionId)).Returns(uk);
            Mocker.GetMock<ISearchForNewBook>().Setup(service => service.SearchForNewBook(forced.ForeignEditionId, null, true))
                .Returns(new List<Book> { uk.Book.Value });
            var overrides = new IdentificationOverrides { Edition = forced };

            Subject.GetDbCandidatesFromTags(AudioBook(uk.Asin), overrides, false).Should().BeEmpty();
            Subject.GetRemoteCandidates(AudioBook(uk.Asin), overrides).Should().BeEmpty();

            Mocker.GetMock<IEditionService>().Verify(service => service.GetEditionByForeignEditionId(uk.ForeignEditionId), Times.Never());
            Mocker.GetMock<ISearchForNewBook>().Verify(service => service.SearchByAsin(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_cross_an_explicit_book_override()
        {
            var edition = Recording("B0CR45YNXY", "Volume 1");
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId(edition.ForeignEditionId)).Returns(edition);

            Subject.GetDbCandidatesFromTags(AudioBook(edition.Asin), new IdentificationOverrides { Book = Recording("B0DFHMW24T", "Volume 2").Book.Value }, false).Should().BeEmpty();
        }

        [Test]
        public void should_not_throw_on_goodreads_exception()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Throws(new GoodreadsException("Bad search"));

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Author" },
                            BookTitle = "Book"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();
        }
    }
}
