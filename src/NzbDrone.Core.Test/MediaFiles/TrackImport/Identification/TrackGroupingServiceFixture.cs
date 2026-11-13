using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FizzWare.NBuilder;
using FizzWare.NBuilder.PropertyNaming;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    // we need to use random strings to test the va (so we don't just get author1, author2 etc which are too similar)
    // but the standard random value namer would give paths that are too long on windows
    public class RandomValueNamerShortStrings : RandomValuePropertyNamer
    {
        private static readonly List<char> AllowedChars;
        private readonly IRandomGenerator _generator;

        public RandomValueNamerShortStrings(BuilderSettings settings)
            : base(settings)
        {
            _generator = new RandomGenerator();
        }

        static RandomValueNamerShortStrings()
        {
            AllowedChars = new List<char>();
            for (var c = 'a'; c < 'z'; c++)
            {
                AllowedChars.Add(c);
            }

            for (var c = 'A'; c < 'Z'; c++)
            {
                AllowedChars.Add(c);
            }

            for (var c = '0'; c < '9'; c++)
            {
                AllowedChars.Add(c);
            }
        }

        protected override string GetString(MemberInfo memberInfo)
        {
            var length = _generator.Next(1, 100);

            var chars = new char[length];

            for (var i = 0; i < length; i++)
            {
                var index = _generator.Next(0, AllowedChars.Count - 1);
                chars[i] = AllowedChars[index];
            }

            var bytes = Encoding.UTF8.GetBytes(chars);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }

    [TestFixture]
    public class TrackGroupingServiceFixture : CoreTest<TrackGroupingService>
    {
        private List<LocalBook> GivenTracks(string root, string author, string book, int count)
        {
            var fileInfos = Builder<ParsedTrackInfo>
                .CreateListOfSize(count)
                .All()
                .With(f => f.Authors = new List<string> { author })
                .With(f => f.BookTitle = book)
                .With(f => f.Asin = null)
                .With(f => f.AsinConflict = false)
                .With(f => f.BookMBId = null)
                .With(f => f.ReleaseMBId = null)
                .Build();

            var tracks = fileInfos.Select(x => Builder<LocalBook>
                                          .CreateNew()
                                          .With(y => y.FileTrackInfo = x)
                                          .With(y => y.Path = Path.Combine(root, x.Title))
                                          .Build()).ToList();

            return tracks;
        }

        private List<LocalBook> GivenTracksWithNoTags(string root, int count)
        {
            var outp = new List<LocalBook>();

            for (var i = 0; i < count; i++)
            {
                var track = Builder<LocalBook>
                    .CreateNew()
                    .With(y => y.FileTrackInfo = new ParsedTrackInfo())
                    .With(y => y.Path = Path.Combine(root, $"{i}.mp3"))
                    .Build();
                outp.Add(track);
            }

            return outp;
        }

        [Repeat(100)]
        private List<LocalBook> GivenVaTracks(string root, string book, int count)
        {
            var settings = new BuilderSettings();
            settings.SetPropertyNamerFor<ParsedTrackInfo>(new RandomValueNamerShortStrings(settings));

            var builder = new Builder(settings);

            var fileInfos = builder
                .CreateListOfSize<ParsedTrackInfo>(count)
                .All()
                .With(f => f.BookTitle = "book")
                .With(f => f.Asin = null)
                .With(f => f.AsinConflict = false)
                .With(f => f.BookMBId = null)
                .With(f => f.ReleaseMBId = null)
                .Build();

            var tracks = fileInfos.Select(x => Builder<LocalBook>
                                          .CreateNew()
                                          .With(y => y.FileTrackInfo = x)
                                          .With(y => y.Path = Path.Combine(@"C:\music\incoming".AsOsAgnostic(), x.Title))
                                          .Build()).ToList();

            return tracks;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(10)]
        public void single_author_is_not_various_authors(int count)
        {
            var tracks = GivenTracks(@"C:\music\incoming".AsOsAgnostic(), "author", "book", count);
            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
        }

        // GivenVaTracks uses random names so repeat multiple times to try to prompt any intermittent failures
        [Ignore("TODO: fix")]
        [Test]
        [Repeat(100)]
        public void all_different_authors_is_various_authors()
        {
            var tracks = GivenVaTracks(@"C:\music\incoming".AsOsAgnostic(), "book", 10);
            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(true);
        }

        [Test]
        public void two_authors_is_not_various_authors()
        {
            var dir = @"C:\music\incoming".AsOsAgnostic();
            var tracks = GivenTracks(dir, "author1", "book", 10);
            tracks.AddRange(GivenTracks(dir, "author2", "book", 10));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
        }

        [Ignore("TODO: fix")]
        [Test]
        [Repeat(100)]
        public void mostly_different_authors_is_various_authors()
        {
            var dir = @"C:\music\incoming".AsOsAgnostic();
            var tracks = GivenVaTracks(dir, "book", 10);
            tracks.AddRange(GivenTracks(dir, "single_author", "book", 2));
            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(true);
        }

        [TestCase("")]
        [TestCase("Various Authors")]
        [TestCase("Various")]
        [TestCase("VA")]
        [TestCase("Unknown")]
        public void va_author_title_is_various_authors(string author)
        {
            var tracks = GivenTracks(@"C:\music\incoming".AsOsAgnostic(), author, "book", 10);
            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(true);
        }

        [TestCase("Va?!")]
        [TestCase("Va Va Voom")]
        [TestCase("V.A. Jr.")]
        [TestCase("Ca Va")]
        public void va_in_author_name_is_not_various_authors(string author)
        {
            var tracks = GivenTracks(@"C:\music\incoming".AsOsAgnostic(), author, "book", 10);
            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(10)]
        public void should_group_single_author_book(int count)
        {
            var tracks = GivenTracks(@"C:\music\incoming".AsOsAgnostic(), "author", "book", count);
            var output = Subject.GroupTracks(tracks);

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(true);

            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(count);
        }

        [TestCase("cd")]
        [TestCase("disc")]
        [TestCase("disk")]
        public void should_group_multi_disc_release(string mediaName)
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\author - book\\{mediaName} 1".AsOsAgnostic(), "author", "book", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming\\author - book\\{mediaName} 2".AsOsAgnostic(), "author", "book", 5));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(true);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(15);
        }

        [TestCase("B0BXV7C8PX", "B0CCFTQW7R")]
        [TestCase("B0F14RFHS6", "B0F14RPXHR")]
        public void should_separate_different_recordings_even_when_all_other_tags_match(string firstAsin, string secondAsin)
        {
            var root = Path.Combine(Path.GetTempPath(), "bookshelf-incoming");
            var tracks = GivenTracksWithNoTags(root, 2);
            tracks[0].Path = Path.Combine(root, $"Book [{firstAsin}].m4b");
            tracks[1].Path = Path.Combine(root, $"Book [{secondAsin}].m4b");
            tracks.ForEach(x =>
            {
                x.FileTrackInfo.Authors = new List<string> { "Author" };
                x.FileTrackInfo.BookTitle = "Same misleading album";
            });

            var output = Subject.GroupTracks(tracks);

            output.Should().HaveCount(2);
            output.Should().OnlyContain(x => x.LocalBooks.Count == 1);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().BeFalse();
        }

        [Test]
        public void should_keep_multipart_tracks_from_the_same_recording_folder_together()
        {
            var root = Path.Combine(Path.GetTempPath(), "Book [B0BXV7C8PX]");
            var tracks = GivenTracksWithNoTags(Path.Combine(root, "CD 1"), 2);
            tracks.AddRange(GivenTracksWithNoTags(Path.Combine(root, "CD 2"), 2));

            var output = Subject.GroupTracks(tracks);

            output.Should().ContainSingle().Which.LocalBooks.Should().HaveCount(4);
            tracks.Should().OnlyContain(x => x.FileTrackInfo.Asin == "B0BXV7C8PX");
        }

        [Test]
        public void should_isolate_conflicting_file_identity_from_other_tracks()
        {
            var tracks = GivenTracksWithNoTags(Path.Combine(Path.GetTempPath(), "Book [B0BXV7C8PX]"), 2);
            tracks[0].FileTrackInfo.Asin = "B0CCFTQW7R";

            var output = Subject.GroupTracks(tracks);

            output.Should().HaveCount(2);
            output.Should().OnlyContain(x => x.LocalBooks.Count == 1);
            tracks[0].FileTrackInfo.AsinConflict.Should().BeTrue();
        }

        [Test]
        public void should_not_group_two_different_books_by_same_author()
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\author - book1".AsOsAgnostic(), "author", "book1", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming\\author - book2".AsOsAgnostic(), "author", "book2", 5));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(2);
            output[0].LocalBooks.Count.Should().Be(10);
            output[1].LocalBooks.Count.Should().Be(5);
        }

        [Test]
        public void should_group_books_with_typos()
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\author - book".AsOsAgnostic(), "author", "Rastaman Vibration (Remastered)", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming\\author - book".AsOsAgnostic(), "author", "Rastaman Vibration (Remastered", 5));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(true);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(15);
        }

        [Test]
        public void should_not_group_two_different_tracks_in_same_directory()
        {
            var tracks = GivenTracks($"C:\\music\\incoming".AsOsAgnostic(), "author", "book1", 1);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming".AsOsAgnostic(), "author", "book2", 1));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(2);
            output[0].LocalBooks.Count.Should().Be(1);
            output[1].LocalBooks.Count.Should().Be(1);
        }

        [Test]
        public void should_separate_two_books_in_same_directory()
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\author discog".AsOsAgnostic(), "author", "book1", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming\\author disog".AsOsAgnostic(), "author", "book2", 5));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(2);
            output[0].LocalBooks.Count.Should().Be(10);
            output[1].LocalBooks.Count.Should().Be(5);
        }

        [Test]
        public void should_separate_many_books_in_same_directory()
        {
            var tracks = new List<LocalBook>();
            for (var i = 0; i < 100; i++)
            {
                tracks.AddRange(GivenTracks($"C:\\music".AsOsAgnostic(), "author" + i, "book" + i, 10));
            }

            // don't test various authors here because it's designed to only work if there's a common book
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(100);
            output.Select(x => x.LocalBooks.Count).Distinct().Should().BeEquivalentTo(new List<int> { 10 });
        }

        [Test]
        public void should_separate_two_books_by_different_authors_in_same_directory()
        {
            var tracks = GivenTracks($"C:\\music\\incoming".AsOsAgnostic(), "author1", "book1", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming".AsOsAgnostic(), "author2", "book2", 5));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(2);
            output[0].LocalBooks.Count.Should().Be(10);
            output[1].LocalBooks.Count.Should().Be(5);
        }

        [Ignore("TODO: fix")]
        [Test]
        [Repeat(100)]
        public void should_group_va_release()
        {
            var tracks = GivenVaTracks(@"C:\music\incoming".AsOsAgnostic(), "book", 10);

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(true);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(true);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(10);
        }

        [Test]
        public void should_not_group_two_books_by_different_authors_with_same_title()
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\book".AsOsAgnostic(), "author1", "book", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming\\book".AsOsAgnostic(), "author2", "book", 5));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);

            output.Count.Should().Be(2);
            output[0].LocalBooks.Count.Should().Be(10);
            output[1].LocalBooks.Count.Should().Be(5);
        }

        [Test]
        public void should_not_fail_if_all_tags_null()
        {
            var tracks = GivenTracksWithNoTags($"C:\\music\\incoming\\book".AsOsAgnostic(), 10);

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(true);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(10);
        }

        [Test]
        public void should_not_fail_if_some_tags_null()
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\book".AsOsAgnostic(), "author1", "book", 10);
            tracks.AddRange(GivenTracksWithNoTags($"C:\\music\\incoming\\book".AsOsAgnostic(), 2));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(true);

            var output = Subject.GroupTracks(tracks);
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(12);
        }

        [Test]
        public void should_cope_with_one_book_in_subfolder_of_another()
        {
            var tracks = GivenTracks($"C:\\music\\incoming\\book".AsOsAgnostic(), "author1", "book", 10);
            tracks.AddRange(GivenTracks($"C:\\music\\incoming\\book\\anotherbook".AsOsAgnostic(), "author2", "book2", 10));

            TrackGroupingService.IsVariousAuthors(tracks).Should().Be(false);
            TrackGroupingService.LooksLikeSingleRelease(tracks).Should().Be(false);

            var output = Subject.GroupTracks(tracks);

            foreach (var group in output)
            {
                TestLogger.Debug($"*** group {group} ***");
                TestLogger.Debug(string.Join("\n", group.LocalBooks.Select(x => x.Path)));
            }

            output.Count.Should().Be(2);
            output[0].LocalBooks.Count.Should().Be(10);
            output[1].LocalBooks.Count.Should().Be(10);
        }
    }
}
