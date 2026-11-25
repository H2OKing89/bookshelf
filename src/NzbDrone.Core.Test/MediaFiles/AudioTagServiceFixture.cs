using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.AutoMoq;

namespace NzbDrone.Core.Test.MediaFiles.AudioTagServiceFixture
{
    [TestFixture]
    public class AudioTagServiceFixture : CoreTest<AudioTagService>
    {
        public static class TestCaseFactory
        {
            private static readonly string[] MediaFiles = new[] { "nin.mp2", "nin.mp3", "nin.flac", "nin.m4a", "nin.wma", "nin.ape", "nin.opus" };

            // Narration/language are read for local recording previews, not written by the retagger.
            private static readonly string[] SkipProperties = new[] { "IsValid", "Duration", "Quality", "MediaInfo", "ImageFile", "BookAuthors", "Narrators", "Language" };
            private static readonly Dictionary<string, string[]> SkipPropertiesByFile = new Dictionary<string, string[]>
            {
                { "nin.mp2", new[] { "OriginalReleaseDate" } }
            };

            public static IEnumerable TestCases
            {
                get
                {
                    foreach (var file in MediaFiles)
                    {
                        var toSkip = SkipProperties;
                        if (SkipPropertiesByFile.ContainsKey(file))
                        {
                            toSkip = toSkip.Union(SkipPropertiesByFile[file]).ToArray();
                        }

                        yield return new TestCaseData(file, toSkip).SetName($"{{m}}_{file.Replace("nin.", "")}");
                    }
                }
            }
        }

        private readonly string _testdir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media");
        private string _copiedFile;
        private AudioTag _testTags;
        private IDiskProvider _diskProvider;

        [SetUp]
        public void Setup()
        {
            _diskProvider = Mocker.Resolve<IDiskProvider>(FileSystemType.Actual);

            Mocker.GetMock<IConfigService>()
                .Setup(x => x.WriteAudioTags)
                .Returns(WriteAudioTagsType.Sync);

            var imageFile = Path.Combine(_testdir, "nin.png");
            var imageSize = _diskProvider.GetFileSize(imageFile);

            // have to manually set the arrays of string parameters and integers to values > 1
            _testTags = Builder<AudioTag>.CreateNew()
                .With(x => x.Track = 2)
                .With(x => x.TrackCount = 33)
                .With(x => x.Disc = 44)
                .With(x => x.DiscCount = 55)
                .With(x => x.Date = new DateTime(2019, 3, 1))
                .With(x => x.Year = 2019)
                .With(x => x.OriginalReleaseDate = new DateTime(2009, 4, 1))
                .With(x => x.OriginalYear = 2009)
                .With(x => x.Performers = new[] { "Performer1" })
                .With(x => x.BookAuthors = new[] { "방탄소년단" })
                .With(x => x.Genres = new[] { "Genre1", "Genre2" })
                .With(x => x.ImageFile = imageFile)
                .With(x => x.ImageSize = imageSize)
                .Build();
        }

        [TearDown]
        public void Cleanup()
        {
            if (File.Exists(_copiedFile))
            {
                File.Delete(_copiedFile);
            }
        }

        private void GivenFileCopy(string filename)
        {
            var original = Path.Combine(_testdir, filename);
            var tempname = $"temp_{Path.GetRandomFileName()}{Path.GetExtension(filename)}";
            _copiedFile = Path.Combine(_testdir, tempname);

            File.Copy(original, _copiedFile);
        }

        private void VerifyDifferent(AudioTag a, AudioTag b, string[] skipProperties)
        {
            foreach (var property in typeof(AudioTag).GetProperties())
            {
                if (skipProperties.Contains(property.Name))
                {
                    continue;
                }

                if (property.CanRead)
                {
                    if (property.PropertyType.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEquatable<>)) ||
                        Nullable.GetUnderlyingType(property.PropertyType) != null)
                    {
                        var val1 = property.GetValue(a, null);
                        var val2 = property.GetValue(b, null);
                        val1.Should().NotBe(val2, $"{property.Name} should not be equal.  Found {val1.NullSafe()} for both tags");
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                    {
                        var val1 = (IEnumerable)property.GetValue(a, null);
                        var val2 = (IEnumerable)property.GetValue(b, null);

                        if (val1 != null && val2 != null)
                        {
                            val1.Should().NotBeEquivalentTo(val2, $"{property.Name} should not be equal");
                        }
                    }
                }
            }
        }

        private void VerifySame(AudioTag a, AudioTag b, string[] skipProperties)
        {
            foreach (var property in typeof(AudioTag).GetProperties())
            {
                if (skipProperties.Contains(property.Name))
                {
                    continue;
                }

                if (property.CanRead)
                {
                    if (property.PropertyType.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEquatable<>)) ||
                        Nullable.GetUnderlyingType(property.PropertyType) != null)
                    {
                        var val1 = property.GetValue(a, null);
                        var val2 = property.GetValue(b, null);
                        val1.Should().Be(val2, $"{property.Name} should be equal");
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                    {
                        var val1 = (IEnumerable)property.GetValue(a, null);
                        var val2 = (IEnumerable)property.GetValue(b, null);

                        if (val1 != null || val2 != null)
                        {
                            val1.Should().BeEquivalentTo(val2, $"{property.Name} should be equal");
                        }
                    }
                }
            }
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_duration(string filename, string[] ignored)
        {
            var path = Path.Combine(_testdir, filename);

            var tags = Subject.ReadTags(path);

            tags.Duration.Should().BeCloseTo(new TimeSpan(0, 0, 1, 25, 130), TimeSpan.FromMilliseconds(100));
        }

        [TestCase("nin.mp3")]
        [TestCase("nin.flac")]
        [TestCase("nin.m4a")]
        public void should_read_narrator_and_language_tags_without_modifying_the_file(string filename)
        {
            GivenFileCopy(filename);
            var narrators = new[] { "Narrator One", "Narrator Two" };
            using (var file = TagLib.File.Create(_copiedFile))
            {
                file.Tag.Composers = narrators;
                file.Tag.Performers = new[] { "A different performer" };
                if (file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Id3v2))
                {
                    ((TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2)).SetTextFrame("TLAN", "eng");
                }
                else if (file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Xiph))
                {
                    ((TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph)).SetField("LANGUAGE", "eng");
                }
                else
                {
                    ((TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple)).SetDashBox("com.apple.iTunes", "LANGUAGE", "eng");
                }

                file.Save();
            }

            var original = File.ReadAllBytes(_copiedFile);
            var tags = Subject.ReadAudioTag(_copiedFile);

            tags.Narrators.Should().Equal(narrators);
            tags.Performers.Should().Equal("A different performer");
            tags.Language.Should().Be("eng");
            File.ReadAllBytes(_copiedFile).Should().Equal(original);
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_write_tags(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            var initialtags = Subject.ReadAudioTag(path);

            VerifyDifferent(initialtags, _testTags, skipProperties);

            _testTags.Write(path);

            var writtentags = Subject.ReadAudioTag(path);

            VerifySame(writtentags, _testTags, skipProperties);
            writtentags.BookAuthors.Should().BeEquivalentTo(
                _testTags.BookAuthors.Concat(_testTags.Performers),
                options => options.WithStrictOrdering());
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_audiotag_from_file_with_no_tags(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            var tag = Subject.ReadAudioTag(path);
            var expected = new AudioTag()
            {
                Performers = new string[0],
                BookAuthors = new string[0],
                Genres = new string[0]
            };

            VerifySame(tag, expected, skipProperties);
            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_parsedtrackinfo_from_file_with_no_tags(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            var tag = Subject.ReadTags(path);

            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_set_quality_and_mediainfo_for_corrupt_file(string filename, string[] skipProperties)
        {
            // use missing to simulate corrupt
            var tag = Subject.ReadAudioTag(filename.Replace("nin", "missing"));
            var expected = new AudioTag();

            VerifySame(tag, expected, skipProperties);
            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_file_with_only_title_tag(string filename, string[] ignored)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            var nametag = new AudioTag();
            nametag.Title = "test";
            nametag.Write(path);

            var tag = Subject.ReadTags(path);
            tag.Title.Should().Be("test");

            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_remove_date_from_tags_when_not_in_metadata(string filename, string[] ignored)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            _testTags.Write(path);

            _testTags.Date = null;
            _testTags.OriginalReleaseDate = null;

            _testTags.Write(path);

            var onDisk = Subject.ReadAudioTag(path);

            onDisk.Date.HasValue.Should().BeFalse();
            onDisk.OriginalReleaseDate.HasValue.Should().BeFalse();
        }

        [Test]
        public void should_ignore_non_parsable_id3v23_date()
        {
            GivenFileCopy("nin.mp2");

            using (var file = TagLib.File.Create(_copiedFile))
            {
                var id3tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                id3tag.SetTextFrame("TORY", "0");
                file.Save();
            }

            var tag = Subject.ReadAudioTag(_copiedFile);
            tag.OriginalReleaseDate.HasValue.Should().BeFalse();
        }

        private BookFile GivenPopulatedTrackfile(int mediumOffset)
        {
            var meta = Builder<AuthorMetadata>.CreateNew().Build();
            var author = Builder<Author>.CreateNew()
                .With(x => x.Metadata = meta)
                .Build();

            var book = Builder<Book>.CreateNew()
                .With(x => x.Author = author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(x => x.Book = book)
                .Build();

            var file = Builder<BookFile>.CreateNew()
                .With(x => x.Edition = edition)
                .With(x => x.Author = author)
                .Build();

            edition.BookFiles = new List<BookFile> { file };

            return file;
        }

        [Test]
        public void get_metadata_should_not_fail_with_missing_country()
        {
            GivenFileCopy("nin.mp3");

            var file = GivenPopulatedTrackfile(0);
            file.Path = _copiedFile;

            var tag = Subject.GetTrackMetadata(file);
        }

        [Test]
        public void should_not_fail_if_media_has_been_omitted()
        {
            GivenFileCopy("nin.mp3");

            var file = GivenPopulatedTrackfile(100);
            file.Path = _copiedFile;

            Assert.DoesNotThrow(() => Subject.GetTrackMetadata(file));
        }

        [TestCase("nin.mp3")]
        public void write_tags_should_update_trackfile_size_and_modified(string filename)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.ScrubAudioTags)
                .Returns(true);

            GivenFileCopy(filename);

            var file = GivenPopulatedTrackfile(0);

            file.Path = _copiedFile;
            Subject.WriteTags(file, false, true);

            var fileInfo = _diskProvider.GetFileInfo(file.Path);
            file.Modified.Should().Be(fileInfo.LastWriteTimeUtc);
            file.Size.Should().Be(fileInfo.Length);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [TestCase("nin.mp3")]
        public void write_tags_should_not_update_tags_if_already_updated(string filename)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.ScrubAudioTags)
                .Returns(true);

            GivenFileCopy(filename);

            var file = GivenPopulatedTrackfile(0);

            file.Path = _copiedFile;
            Subject.WriteTags(file, false, true);
            Subject.WriteTags(file, false, true);
            Subject.WriteTags(file, false, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        private BookFile GivenRecordingFile(string fileAsin, string editionAsin, string otherEmbeddedAsin = null)
        {
            GivenFileCopy("nin.m4a");
            using (var audio = TagLib.File.Create(_copiedFile))
            {
                var tag = (TagLib.Mpeg4.AppleTag)audio.GetTag(TagLib.TagTypes.Apple);
                tag.SetDashBox("com.pilabor.tone", "AUDIBLE_ASIN", fileAsin);
                if (otherEmbeddedAsin != null)
                {
                    tag.SetDashBox("com.apple.iTunes", "ASIN", otherEmbeddedAsin);
                }

                audio.Save();
            }

            var file = GivenPopulatedTrackfile(0);
            file.Path = _copiedFile;
            file.Edition.Value.Asin = editionAsin;
            file.Edition.Value.ForeignEditionId = $"audible:us:{editionAsin}";
            return file;
        }

        [TestCase("B0BXV7C8PX", "B0HLLTHK38", null, false)]
        [TestCase("B0CCFTQW7R", "B0HLLTHK38", null, true)]
        [TestCase("B0F14RFHS6", "B0F14RPXHR", null, false)]
        [TestCase("B0F14RFHS6", "B0F14RPXHR", null, true)]
        [TestCase("B0F14RFHS6", "B0F14RPXHR", "B0F14RPXHR", true)]
        [TestCase("not-an-asin", "B0F14RPXHR", null, true)]
        public void should_not_scrub_or_retag_a_conflicting_recording(string fileAsin, string editionAsin, string otherEmbeddedAsin, bool force)
        {
            var file = GivenRecordingFile(fileAsin, editionAsin, otherEmbeddedAsin);
            Mocker.GetMock<IConfigService>().Setup(x => x.ScrubAudioTags).Returns(true);
            var original = File.ReadAllBytes(file.Path);

            if (force)
            {
                Subject.WriteTags(file, false, true);
            }
            else
            {
                Subject.SyncTags(new List<Edition> { file.Edition.Value });
            }

            File.ReadAllBytes(file.Path).Should().Equal(original);
            Mocker.GetMock<IRootFolderWatchingService>().Verify(x => x.ReportFileSystemChangeBeginning(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Update(It.IsAny<BookFile>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(x => x.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_prefer_path_for_import_without_allowing_conflicting_tags_to_be_scrubbed()
        {
            var file = GivenRecordingFile("B079TJTK8S", "B0D1CW8W56");
            var path = Path.Combine(Path.GetDirectoryName(file.Path), $"{Guid.NewGuid():N} [B0D1CW8W56].m4a");
            File.Move(file.Path, path);
            _copiedFile = file.Path = path;
            Mocker.GetMock<IConfigService>().Setup(x => x.PreferPathAsin).Returns(true);
            Mocker.GetMock<IConfigService>().Setup(x => x.ScrubAudioTags).Returns(true);
            var original = File.ReadAllBytes(path);

            var parsed = Subject.ReadTags(path);
            parsed.Asin.Should().Be("B0D1CW8W56");
            parsed.AsinConflict.Should().BeFalse();
            AudioRecordingIdentifier.MatchesEdition(parsed, path, file.Edition.Value).Should().BeTrue();

            Subject.WriteTags(file, false, true);

            File.ReadAllBytes(path).Should().Equal(original);
            ParsedTrackInfo raw = Subject.ReadAudioTag(path);
            raw.Asin.Should().Be("B079TJTK8S");
            raw.AsinConflict.Should().BeTrue();
            Mocker.GetMock<IEventAggregator>().Verify(x => x.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_allow_retagging_the_exact_recording()
        {
            var file = GivenRecordingFile("B0F14RFHS6", "B0F14RFHS6");
            file.Edition.Value.Title = "Correct recording title";

            Subject.WriteTags(file, false, true);

            ParsedTrackInfo actual = new AudioTag(file.Path);
            actual.Title.Should().Be("Correct recording title");
            actual.Asin.Should().Be("B0F14RFHS6");
            actual.AsinConflict.Should().BeFalse();
            Mocker.GetMock<IEventAggregator>().Verify(x => x.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [TestCase("B0F14RFHS6", "B0F14RPXHR", null)]
        [TestCase("B0F14RFHS6", "B0F14RFHS6", "B0F14RPXHR")]
        public void should_exclude_conflicting_recordings_from_retag_previews(string fileAsin, string editionAsin, string otherEmbeddedAsin)
        {
            var file = GivenRecordingFile(fileAsin, editionAsin, otherEmbeddedAsin);
            var bookId = file.Edition.Value.Book.Value.Id;
            Mocker.GetMock<IMediaFileService>().Setup(x => x.GetFilesByBook(bookId)).Returns(new List<BookFile> { file });
            var original = File.ReadAllBytes(file.Path);

            Subject.GetRetagPreviewsByBook(bookId).Should().BeEmpty();

            File.ReadAllBytes(file.Path).Should().Equal(original);
        }

        [Test]
        public void should_not_fail_reading_metadata_with_dates_omitted()
        {
            GivenFileCopy("nin.mp3");

            var bookFile = GivenPopulatedTrackfile(0);
            bookFile.Path = _copiedFile;
            bookFile.Edition.Value.ReleaseDate = null;
            bookFile.Edition.Value.Book.Value.ReleaseDate = null;

            Assert.DoesNotThrow(() => Subject.GetTrackMetadata(bookFile));
        }
    }
}
