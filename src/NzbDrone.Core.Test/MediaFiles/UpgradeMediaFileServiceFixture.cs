using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private string _root;
        private BookFile _incoming;
        private BookFile _old;
        private LocalBook _local;
        private HashSet<string> _files;
        private RootFolder _rootFolder;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Test\Books".AsOsAgnostic();
            var author = new Author { Id = 1, Path = Path.Combine(_root, "Author") };
            _old = new BookFile { Id = 1, Part = 1, Path = Path.Combine(author.Path, "book.mp3"), CalibreId = 7 };
            var book = new Book { Id = 1, Author = author, BookFiles = new List<BookFile> { _old } };
            _incoming = new BookFile { Part = 1, Path = Path.Combine(_root, "downloads", "book.mp3") };
            _local = new LocalBook { Author = author, Book = book, Path = _incoming.Path };
            _files = new HashSet<string> { _old.Path, _incoming.Path };
            _rootFolder = new RootFolder { Path = _root };
            Mocker.GetMock<IDiskProvider>().Setup(x => x.GetParentFolder(It.IsAny<string>())).Returns<string>(Path.GetDirectoryName);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FileExists(It.IsAny<string>())).Returns<string>(_files.Contains);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.DeleteFile(It.IsAny<string>())).Callback<string>(path => _files.Remove(path));
            Mocker.GetMock<IDiskProvider>().Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                .Callback<string, string, bool>((source, destination, _) =>
                {
                    if (!_files.Contains(source) || _files.Contains(destination))
                    {
                        throw new IOException("Source missing or destination exists");
                    }

                    _files.Remove(source);
                    _files.Add(destination);
                });
            Mocker.GetMock<IRecycleBinProvider>().Setup(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, _) => _files.Remove(path));
            Mocker.GetMock<IRootFolderService>().Setup(x => x.GetBestRootFolder(It.IsAny<string>())).Returns(_rootFolder);
            Mocker.GetMock<IMoveBookFiles>().Setup(x => x.StageBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Returns<BookFile, LocalBook>((file, local) =>
                {
                    var source = file.Path;
                    var stage = Path.Combine(local.Author.Path, ".stage-" + Guid.NewGuid().ToString("N"));
                    file.Path = Path.Combine(local.Author.Path, Path.GetFileName(source));
                    _files.Add(stage);
                    return new BookFileMoveResult { BookFile = file, SourcePath = source, StagingPath = stage };
                });
        }

        [Test]
        public void should_prepare_and_tag_without_removing_old_media_or_download()
        {
            var source = _incoming.Path;
            var result = Subject.UpgradeBookFile(_incoming, _local);
            _files.Should().Contain(new[] { source, _old.Path, result.StagingPath });
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMetadataTagService>().Verify(x => x.WriteTags(_incoming, true, false), Times.Once());
        }

        [Test]
        public void should_preserve_old_files_when_staging_fails()
        {
            Mocker.GetMock<IMoveBookFiles>().Setup(x => x.StageBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>())).Throws(new IOException("disk full"));
            Assert.Throws<IOException>(() => Subject.UpgradeBookFile(_incoming, _local));
            _files.Should().Contain(_old.Path);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_preserve_source_and_old_files_when_tagging_fails()
        {
            var source = _incoming.Path;
            Mocker.GetMock<IMetadataTagService>().Setup(x => x.WriteTags(It.IsAny<BookFile>(), true, false)).Throws(new IOException("tag write failed"));
            Assert.Throws<IOException>(() => Subject.UpgradeBookFile(_incoming, _local));
            _files.Should().BeEquivalentTo(source, _old.Path);
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_replace_same_destination_only_after_preparation_succeeds()
        {
            var source = _incoming.Path;
            var result = Subject.UpgradeBookFile(_incoming, _local);
            Subject.CommitUpgrade(new List<BookFileMoveResult> { result });
            Subject.FinalizeUpgrade(new List<BookFileMoveResult> { result });
            result.Committed.Should().BeTrue();
            _files.Should().BeEquivalentTo(_old.Path);
            result.OldFiles.Should().ContainSingle().Which.Should().Be(_old);
            Subject.AbortUpgrade(new List<BookFileMoveResult> { result });
            _files.Should().Contain(_old.Path);
        }

        [Test]
        public void should_keep_download_in_copy_mode()
        {
            var source = _incoming.Path;
            var result = Subject.UpgradeBookFile(_incoming, _local, true);
            Subject.CommitUpgrade(new List<BookFileMoveResult> { result });
            Subject.FinalizeUpgrade(new List<BookFileMoveResult> { result });
            _files.Should().Contain(source);
        }

        [Test]
        public void should_preserve_unrelated_destination_collision()
        {
            _local.Book.BookFiles = new List<BookFile>();
            Assert.Throws<DestinationAlreadyExistsException>(() => Subject.UpgradeBookFile(_incoming, _local));
            _files.Should().HaveCount(2).And.Contain(_old.Path);
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_restore_every_old_part_when_later_publication_fails()
        {
            var old2 = new BookFile { Id = 2, Part = 2, Path = Path.Combine(_local.Author.Path, "part2.mp3") };
            _local.Book.BookFiles.Value.Add(old2);
            _files.Add(old2.Path);
            var incoming2 = new BookFile { Part = 2, Path = Path.Combine(_root, "downloads", "part2.mp3") };
            _files.Add(incoming2.Path);
            var source1 = _incoming.Path;
            var source2 = incoming2.Path;
            var first = Subject.UpgradeBookFile(_incoming, _local);
            var second = Subject.UpgradeBookFile(incoming2, _local);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.MoveFile(second.StagingPath, old2.Path, false)).Throws(new IOException("disk full"));
            Assert.Throws<IOException>(() => Subject.CommitUpgrade(new List<BookFileMoveResult> { first, second }));
            _files.Should().BeEquivalentTo(source1, source2, _old.Path, old2.Path);
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_keep_unrepresented_old_parts_when_partial_replacement_arrives()
        {
            var old2 = new BookFile { Id = 2, Part = 2, Path = Path.Combine(_local.Author.Path, "part2.mp3") };
            _local.Book.BookFiles.Value.Add(old2);
            _files.Add(old2.Path);
            var result = Subject.UpgradeBookFile(_incoming, _local);
            Subject.CommitUpgrade(new List<BookFileMoveResult> { result });
            Subject.FinalizeUpgrade(new List<BookFileMoveResult> { result });
            _files.Should().Contain(old2.Path);
            result.OldFiles.Should().ContainSingle().Which.Should().Be(_old);
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(old2, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_allow_verified_complete_consolidation_of_old_parts()
        {
            _old.MediaInfo = new MediaInfoModel { DurationSeconds = 100 };
            var old2 = new BookFile { Id = 2, Part = 2, Path = Path.Combine(_local.Author.Path, "part2.mp3"), MediaInfo = new MediaInfoModel { DurationSeconds = 100 } };
            _local.Book.BookFiles.Value.Add(old2);
            _files.Add(old2.Path);
            _incoming.MediaInfo = new MediaInfoModel { DurationSeconds = 200 };
            var result = Subject.UpgradeBookFile(_incoming, _local);
            Subject.CommitUpgrade(new List<BookFileMoveResult> { result });
            Subject.FinalizeUpgrade(new List<BookFileMoveResult> { result });
            _files.Should().NotContain(old2.Path);
            result.OldFiles.Should().HaveCount(2);
        }

        [Test]
        public void should_preserve_old_calibre_formats_when_conversion_fails()
        {
            _rootFolder.IsCalibreLibrary = true;
            _rootFolder.CalibreSettings = new CalibreSettings();
            Mocker.GetMock<ICalibreProxy>().Setup(x => x.AddAndConvert(_incoming, _rootFolder.CalibreSettings))
                .Callback(() =>
                {
                    _incoming.CalibreId.Should().Be(0);
                    _incoming.CalibreId = 99;
                })
                .Throws(new IOException("conversion failed"));
            Assert.Throws<IOException>(() => Subject.UpgradeBookFile(_incoming, _local));
            Mocker.GetMock<ICalibreProxy>().Verify(x => x.DeleteBook(It.Is<BookFile>(file => file.CalibreId == 99), _rootFolder.CalibreSettings), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(x => x.DeleteBook(_old, It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(x => x.RemoveFormats(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CalibreSettings>()), Times.Never());
            _files.Should().HaveCount(2);
        }

        [Test]
        public void should_reject_missing_root_before_any_transfer()
        {
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(_root)).Returns(false);
            Assert.Throws<NzbDrone.Core.MediaFiles.BookImport.RootFolderNotFoundException>(() => Subject.UpgradeBookFile(_incoming, _local));
            Mocker.GetMock<IMoveBookFiles>().Verify(x => x.StageBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
        }

        [Test]
        public void should_restore_old_media_and_download_when_database_save_fails_after_publication()
        {
            var source = _incoming.Path;
            var result = Subject.UpgradeBookFile(_incoming, _local);
            Subject.CommitUpgrade(new List<BookFileMoveResult> { result });
            result.Published.Should().BeTrue();
            result.Committed.Should().BeFalse();
            _files.Should().Contain(result.BackupPath);

            Subject.AbortUpgrade(new List<BookFileMoveResult> { result });

            _files.Should().BeEquivalentTo(source, _old.Path);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_publish_over_a_source_in_the_same_batch()
        {
            var result = Subject.UpgradeBookFile(_incoming, _local);
            result.SourcePath = _old.Path;

            Assert.Throws<SameFilenameException>(() => Subject.CommitUpgrade(new List<BookFileMoveResult> { result }));

            _files.Should().Contain(_old.Path);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }
    }
}
