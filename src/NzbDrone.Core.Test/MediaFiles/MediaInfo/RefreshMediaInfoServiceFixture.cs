using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
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
        private BookFile _file;
        private MediaInfoModel _previousMediaInfo;
        private MediaInfoModel _probedMediaInfo;

        [SetUp]
        public void Setup()
        {
            _previousMediaInfo = new MediaInfoModel { AudioFormat = "old codec", AudioBitrate = 64 };
            _probedMediaInfo = new MediaInfoModel { AudioFormat = "AAC", AudioBitrate = 126, Source = "MediaInfo" };
            _file = new BookFile
            {
                Id = 10,
                EditionId = 20,
                Path = "/library/book.m4b",
                OriginalFilePath = "/downloads/original.m4b",
                Quality = new QualityModel(Quality.M4B),
                SceneName = "Original release",
                MediaInfo = _previousMediaInfo
            };

            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(new List<BookFile> { _file });
            Mocker.GetMock<IMediaInfoProbe>().Setup(probe => probe.Read(_file.Path)).Returns(_probedMediaInfo);
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
            Mocker.GetMock<IMediaInfoProbe>().Setup(probe => probe.Read(_file.Path)).Returns((MediaInfoModel)null);

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.Should().BeSameAs(_previousMediaInfo);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()), Times.Never());
        }

        [Test]
        public void should_continue_after_one_probe_fails()
        {
            var unreadable = new BookFile { Id = 11, Path = "/library/unreadable.mp3", MediaInfo = _previousMediaInfo };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(new List<BookFile> { unreadable, _file });
            Mocker.GetMock<IMediaInfoProbe>().Setup(probe => probe.Read(unreadable.Path)).Throws(new InvalidOperationException("Optional probe failed"));

            Subject.Execute(new RefreshMediaInfoCommand());

            unreadable.MediaInfo.Should().BeSameAs(_previousMediaInfo);
            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.Is<List<BookFile>>(files => files.Count == 1 && files[0] == _file)), Times.Once());
        }

        [Test]
        public void should_skip_ebooks_and_unknown_file_types()
        {
            var ebook = new BookFile { Id = 11, Path = "/library/book.epub", MediaInfo = _previousMediaInfo };
            var other = new BookFile { Id = 12, Path = "/library/cover.jpg" };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(new List<BookFile> { ebook, other });

            Subject.Execute(new RefreshMediaInfoCommand());

            ebook.MediaInfo.Should().BeSameAs(_previousMediaInfo);
            Mocker.GetMock<IMediaInfoProbe>().Verify(probe => probe.Read(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()), Times.Never());
        }

        [Test]
        public void should_accept_case_insensitive_audio_extensions()
        {
            _file.Path = "/library/BOOK.M4B";
            Mocker.GetMock<IMediaInfoProbe>().Setup(probe => probe.Read(_file.Path)).Returns(_probedMediaInfo);

            Subject.Execute(new RefreshMediaInfoCommand());

            _file.MediaInfo.Should().BeSameAs(_probedMediaInfo);
        }

        [Test]
        public void should_persist_large_libraries_in_batches()
        {
            var files = Enumerable.Range(1, 205).Select(id => new BookFile { Id = id, Path = $"/library/{id}.m4b" }).ToList();
            var batches = new List<List<BookFile>>();
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFiles()).Returns(files);
            Mocker.GetMock<IMediaInfoProbe>().Setup(probe => probe.Read(It.IsAny<string>())).Returns(_probedMediaInfo);
            Mocker.GetMock<IMediaFileService>().Setup(service => service.UpdateMediaInfo(It.IsAny<List<BookFile>>()))
                .Callback<List<BookFile>>(batch => batches.Add(batch));

            Subject.Execute(new RefreshMediaInfoCommand());

            batches.Select(batch => batch.Count).Should().Equal(100, 100, 5);
            batches.SelectMany(batch => batch).Should().Equal(files);
            files.Should().OnlyContain(file => file.MediaInfo == _probedMediaInfo);
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
            Mocker.GetMock<IMediaInfoProbe>().Verify(probe => probe.Read(It.IsAny<string>()), Times.Never());
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
