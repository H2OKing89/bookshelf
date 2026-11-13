using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class DeleteAuthorFilesFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private Author _author;
        private BookFile _owned;
        private string _root;
        private List<BookFile> _ownedFiles;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Test\Books".AsOsAgnostic();
            _author = new Author { Id = 32, AuthorMetadataId = 32, Path = Path.Combine(_root, "Brandon Varnell") };
            _owned = new BookFile { Id = 1, Path = Path.Combine(_author.Path, "owned.m4b") };
            _ownedFiles = new List<BookFile> { _owned };
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author>());
            Mocker.GetMock<IMediaFileService>().Setup(x => x.GetFilesByAuthorMetadataId(_author.AuthorMetadataId)).Returns(_ownedFiles);
            Mocker.GetMock<IMediaFileService>().Setup(x => x.GetFileWithPath(It.IsAny<List<string>>())).Returns(_ownedFiles);
            Mocker.GetMock<IRootFolderService>().Setup(x => x.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder { Path = _root });
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(_root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        }

        [TestCase("Brandon Varnell (1)")]
        [TestCase("Brandon Varnell/Nested Author")]
        [TestCase("Brandon Varnell")]
        public void should_delete_only_owned_files_and_preserve_shared_directory(string otherFolder)
        {
            var other = new Author { Id = 253, AuthorMetadataId = 253, Path = Path.Combine(_root, otherFolder) };
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { other });
            var message = new AuthorDeletedEvent(_author, true, false);
            Subject.Handle(message);
            Subject.HandleAsync(message);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(_owned.Path, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.Is<string>(path => path != _owned.Path), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_owned_file_outside_registered_author_directory()
        {
            _owned.Path = Path.Combine(_root, "Original Folder", "owned.m4b");
            Subject.Handle(new AuthorDeletedEvent(_author, true, false));
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(_owned.Path, "Original Folder"), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(_owned, DeleteMediaFileReason.Manual), Times.Once());
        }

        [Test]
        public void should_preserve_a_path_also_registered_to_another_file_record()
        {
            var shared = new BookFile { Id = 99, Path = _owned.Path };
            Mocker.GetMock<IMediaFileService>().Setup(x => x.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile> { _owned, shared });
            Subject.Handle(new AuthorDeletedEvent(_author, true, false));
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_preserve_files_when_another_profile_uses_the_same_metadata()
        {
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author>
            {
                new Author { Id = 253, AuthorMetadataId = _author.AuthorMetadataId, Path = Path.Combine(_root, "Other") }
            });
            Subject.Handle(new AuthorDeletedEvent(_author, true, false));
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_preserve_files_and_rows_when_root_is_unavailable()
        {
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(_root)).Returns(false);
            Subject.Handle(new AuthorDeletedEvent(_author, true, false));
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_leave_media_when_delete_files_is_false()
        {
            var message = new AuthorDeletedEvent(_author, false, false);
            Subject.Handle(message);
            Subject.HandleAsync(message);
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(x => x.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_traverse_an_author_directory_for_an_unmapped_file()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.DeleteEmptyFolders).Returns(true);
            var unmapped = new BookFile { Path = Path.Combine(_root, "unmapped.mp3"), EditionId = 0 };

            Assert.DoesNotThrow(() => Subject.Handle(new BookFileDeletedEvent(unmapped, DeleteMediaFileReason.Manual)));

            Mocker.GetMock<IDiskProvider>().Verify(x => x.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(x => x.RemoveEmptySubfolders(It.IsAny<string>()), Times.Never());
        }
    }
}
