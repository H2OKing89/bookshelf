using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    [TestFixture]
    public class RefreshMediaInfoServiceFixture : CoreTest<RefreshMediaInfoService>
    {
        private const string BundleId = "owned-bundle";
        private static readonly DateTime Modified = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        private BookFile _file;
        private MediaInfoModel _previousMediaInfo;
        private MediaInfoModel _probedMediaInfo;
        private DateTime _modified;

        [SetUp]
        public void Setup()
        {
            _previousMediaInfo = new MediaInfoModel { AudioFormat = "old codec", AudioBitrate = 64 };
            _probedMediaInfo = new MediaInfoModel
            {
                AudioFormat = "AAC",
                AudioBitrate = 126,
                Source = "MediaInfo",
                AudioStreams = new List<AudioStreamInfo> { new AudioStreamInfo { AudioFormat = "AAC", AudioChannels = 2, AudioSampleRate = 44100 } }
            };
            _modified = Modified;
            _file = new BookFile
            {
                Id = 10,
                EditionId = 20,
                Path = "/library/book.m4b",
                OriginalFilePath = "/downloads/original.m4b",
                Size = 1000,
                Modified = Modified,
                Quality = new QualityModel(Quality.M4B),
                SceneName = "Original release",
                MediaInfo = _previousMediaInfo
            };

            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(new List<BookFile> { _file });
            Mocker.GetMock<IAudioTagService>().Setup(probe => probe.ReadAudioTag(_file.Path)).Returns(new AudioTag { MediaInfo = _probedMediaInfo });
            var observed = new Mock<IFileInfo>();
            observed.SetupGet(file => file.Exists).Returns(true);
            observed.SetupGet(file => file.Length).Returns(1000);
            observed.SetupGet(file => file.LastWriteTimeUtc).Returns(() => _modified);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetFileInfo(It.IsAny<string>())).Returns(observed.Object);
            Mocker.GetMock<IMediaInfoProbe>().SetupGet(probe => probe.BundleId).Returns(BundleId);
        }

        [Test]
        public void should_refresh_all_existing_files_without_changing_identification_or_tags()
        {
            var quality = _file.Quality;

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
            _file.EditionId.Should().Be(20);
            _file.Path.Should().Be("/library/book.m4b");
            _file.OriginalFilePath.Should().Be("/downloads/original.m4b");
            _file.SceneName.Should().Be("Original release");
            _file.Quality.Should().BeSameAs(quality);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.Is<List<BookFile>>(files => files.Count == 1 && files[0] == _file)), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFiles(), Times.Once());
            Mocker.GetMock<IMediaFileService>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_scope_refresh_to_one_author()
        {
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByAuthor(3)).Returns(new List<BookFile> { _file });

            Subject.Execute(new RefreshMediaInfoCommand { AuthorId = 3 });

            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesByAuthor(3), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFiles(), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesByBook(It.IsAny<int>()), Times.Never());
            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
        }

        [Test]
        public void should_scope_refresh_to_one_book()
        {
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByBook(4)).Returns(new List<BookFile> { _file });

            Subject.Execute(new RefreshMediaInfoCommand { BookId = 4 });

            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesByBook(4), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFiles(), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesByAuthor(It.IsAny<int>()), Times.Never());
            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
        }

        [Test]
        public void should_retain_previous_metadata_when_file_cannot_be_probed()
        {
            Mocker.GetMock<IAudioTagService>().Setup(probe => probe.ReadAudioTag(_file.Path)).Returns(new AudioTag { MediaInfo = new MediaInfoModel { Source = "TagLib", AudioBitrate = 32 } });

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.AudioFormat.Should().Be(_previousMediaInfo.AudioFormat);
            _file.MediaInfo.AudioBitrate.Should().Be(_previousMediaInfo.AudioBitrate);
            _file.MediaInfo.LastAnalysisAttempt.Target.BundleId.Should().Be(BundleId);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()), Times.Once());
        }

        [Test]
        public void should_preserve_fresh_taglib_measurements_when_native_probe_is_partial()
        {
            _file.Path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "nin.mp3");
            var expected = new AudioTag(_file.Path).MediaInfo;
            var partial = new MediaInfoModel
            {
                AudioFormat = "MPEG Audio",
                Source = "MediaInfo",
                AudioStreams = new List<AudioStreamInfo> { new AudioStreamInfo { AudioFormat = "MPEG Audio" } }
            };
            Mocker.GetMock<IMediaInfoProbe>().Setup(probe => probe.Read(_file.Path)).Returns(partial);
            Mocker.SetConstant<IAudioTagService>(Mocker.Resolve<AudioTagService>());

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.Source.Should().Be("MediaInfo");
            _file.MediaInfo.AudioBitrate.Should().Be(expected.AudioBitrate).And.BeGreaterThan(0);
            _file.MediaInfo.AudioChannels.Should().Be(expected.AudioChannels);
            _file.MediaInfo.AudioSampleRate.Should().Be(expected.AudioSampleRate);
            _file.MediaInfo.AudioStreams.Single().AudioBitrate.Should().Be(expected.AudioBitrate);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.Is<List<BookFile>>(files => files.Count == 1 && files[0] == _file)), Times.Once());
        }

        [Test]
        public void should_continue_after_one_probe_fails()
        {
            var unreadable = new BookFile { Id = 11, Path = "/library/unreadable.mp3", MediaInfo = _previousMediaInfo };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(new List<BookFile> { unreadable, _file });
            Mocker.GetMock<IAudioTagService>().Setup(probe => probe.ReadAudioTag(unreadable.Path)).Throws(new InvalidOperationException("Optional probe failed"));

            Subject.Execute(new RefreshMediaInfoCommand());

            unreadable.MediaInfo.AudioFormat.Should().Be(_previousMediaInfo.AudioFormat);
            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.Is<List<BookFile>>(files => files.Count == 2)), Times.Once());
        }

        [Test]
        public void should_skip_ebooks_and_unknown_file_types()
        {
            var ebook = new BookFile { Id = 11, Path = "/library/book.epub", MediaInfo = _previousMediaInfo };
            var other = new BookFile { Id = 12, Path = "/library/cover.jpg" };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(new List<BookFile> { ebook, other });

            Subject.Execute(new RefreshMediaInfoCommand());

            ebook.MediaInfo.Should().BeSameAs(_previousMediaInfo);
            Mocker.GetMock<IAudioTagService>().Verify(probe => probe.ReadAudioTag(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()), Times.Never());
        }

        [TestCase("M4B")]
        [TestCase("opus")]
        [TestCase("OPUS")]
        [TestCase("wv")]
        [TestCase("WV")]
        public void should_refresh_supported_audio_extensions(string extension)
        {
            _file.Path = "/library/BOOK." + extension;
            Mocker.GetMock<IAudioTagService>().Setup(probe => probe.ReadAudioTag(_file.Path)).Returns(new AudioTag { MediaInfo = _probedMediaInfo });

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
        }

        [Test]
        public void should_persist_large_libraries_in_batches()
        {
            var files = Enumerable.Range(1, 205).Select(id => new BookFile { Id = id, Path = $"/library/{id}.m4b" }).ToList();
            var batches = new List<List<BookFile>>();
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(files);
            Mocker.GetMock<IAudioTagService>().Setup(probe => probe.ReadAudioTag(It.IsAny<string>())).Returns(new AudioTag { MediaInfo = _probedMediaInfo });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()))
                .Callback<List<BookFile>>(batch => batches.Add(batch));

            Subject.Execute(new RefreshMediaInfoCommand());

            batches.Select(batch => batch.Count).Should().Equal(100, 100, 5);
            batches.SelectMany(batch => batch).Should().Equal(files);
            files.Should().OnlyContain(file => file.MediaInfo == _probedMediaInfo);
        }

        [Test]
        public void should_probe_an_older_revision_once_then_skip_the_unchanged_file()
        {
            Subject.Execute(new RefreshStaleMediaInfoCommand());
            Subject.Execute(new RefreshStaleMediaInfoCommand());

            var reader = Mocker.GetMock<IAudioTagService>();
            reader.Verify(service => service.ReadAudioTag(_file.Path), Times.Once());
            _file.MediaInfo.Analysis.Revision.Should().Be(MediaInfoAnalysis.CurrentRevision);
            _file.MediaInfo.Analysis.Complete.Should().BeTrue();

            _file.MediaInfo.Analysis.BundleId = "older-bundle";
            reader.Setup(service => service.ReadAudioTag(_file.Path)).Returns(() => new AudioTag
            {
                MediaInfo = new MediaInfoModel { AudioFormat = "AAC", Source = "MediaInfo", AudioStreams = _probedMediaInfo.AudioStreams }
            });

            Subject.Execute(new RefreshStaleMediaInfoCommand());
            reader.Verify(service => service.ReadAudioTag(_file.Path), Times.Exactly(2));
        }

        [Test]
        public void should_retain_partial_technical_details_and_back_off_an_unchanged_failed_target()
        {
            _previousMediaInfo.AudioStreams.Add(new AudioStreamInfo { Index = 0, AudioFormat = "AAC" });
            _previousMediaInfo.AudioStreams.Add(new AudioStreamInfo { Index = 1, AudioFormat = "Opus" });
            var reader = Mocker.GetMock<IAudioTagService>();
            reader.Setup(service => service.ReadAudioTag(_file.Path)).Returns(new AudioTag { MediaInfo = new MediaInfoModel { Source = "TagLib" } });

            Subject.Execute(new RefreshStaleMediaInfoCommand());
            Subject.Execute(new RefreshStaleMediaInfoCommand());

            reader.Verify(service => service.ReadAudioTag(_file.Path), Times.Once());
            _file.MediaInfo.AudioStreams.Select(stream => stream.AudioFormat).Should().Equal("AAC", "Opus");

            _modified = Modified.AddHours(1);
            _file.Modified = _modified;
            Subject.Execute(new RefreshStaleMediaInfoCommand());

            reader.Verify(service => service.ReadAudioTag(_file.Path), Times.Exactly(2));
            MediaInfoAnalysis.IsInvalidatedByFileState(_file.MediaInfo, _file.Size, _file.Modified).Should().BeTrue();
        }

        [Test]
        public void should_discard_results_if_the_file_changes_during_analysis()
        {
            Mocker.GetMock<IAudioTagService>().Setup(service => service.ReadAudioTag(_file.Path)).Returns(() =>
            {
                _modified = Modified.AddHours(1);
                return new AudioTag { MediaInfo = _probedMediaInfo };
            });

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.Should().BeSameAs(_previousMediaInfo);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()), Times.Never());
        }

        [TestCase(0, null)]
        [TestCase(-1, null)]
        [TestCase(null, 0)]
        [TestCase(null, -1)]
        [TestCase(3, 4)]
        public void should_reject_invalid_or_ambiguous_scope_before_reading_library(int? authorId, int? bookId)
        {
            Action refresh = () => Subject.Execute(new RefreshMediaInfoCommand { AuthorId = authorId, BookId = bookId });

            refresh.Should().Throw<ArgumentException>();
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFiles(), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesByAuthor(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesByBook(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IAudioTagService>().Verify(probe => probe.ReadAudioTag(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_expose_manual_media_info_command_with_client_updates()
        {
            var command = new RefreshMediaInfoCommand();

            command.Name.Should().Be("RefreshMediaInfo");
            command.SendUpdatesToClient.Should().BeTrue();
            command.RequiresDiskAccess.Should().BeTrue();
            command.UpdateScheduledTask.Should().BeFalse();
        }
    }
}
