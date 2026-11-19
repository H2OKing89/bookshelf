using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadImportSourceServiceFixture : FileSystemTest<DownloadImportSourceService>
    {
        private string _output;
        private string _library;
        private DateTime _modified;
        private TrackedDownload _tracked;
        private DownloadImportReceipt _receipt;
        private Dictionary<int, BookFile> _files;

        [SetUp]
        public void Setup()
        {
            _output = @"C:\downloads\owned-job".AsOsAgnostic();
            _library = @"C:\library".AsOsAgnostic();
            _modified = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
            _tracked = new TrackedDownload
            {
                DownloadClient = 7,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-job", OutputPath = new OsPath(_output), CanMoveFiles = true }
            };
            _receipt = new DownloadImportReceipt
            {
                DownloadId = "owned-job",
                DownloadClientId = 7,
                OutputPath = _output,
                BookIds = new List<int> { 5 }
            };
            _files = new Dictionary<int, BookFile>();
            Mocker.GetMock<IRootFolderService>().Setup(service => service.All())
                .Returns(new List<RootFolder> { new RootFolder { Path = _library } });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Get(It.IsAny<int>()))
                .Returns<int>(id => _files.GetValueOrDefault(id));
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEdition(8))
                .Returns(new Edition { Id = 8, BookId = 5 });
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((item, previous) => previous ?? item);
            AddPart(1, "01.mp3");
            AddPart(2, "02.m4b");
        }

        private void AddPart(int part, string filename)
        {
            var source = Path.Combine(_output, filename);
            var destination = Path.Combine(_library, "Author", filename);
            FileSystem.AddFile(source, new MockFileData("owned source " + part));
            FileSystem.AddFile(destination, new MockFileData("owned committed " + part));
            FileSystem.File.SetLastWriteTimeUtc(source, _modified);
            var file = new BookFile { Id = 100 + part, EditionId = 8, Part = part, Path = destination };
            _files.Add(file.Id, file);
            _receipt.Files.Add(new DownloadImportFile
            {
                BookId = 5,
                EditionId = 8,
                FileId = file.Id,
                Part = part,
                PartCount = 2,
                SourcePath = source,
                ImportedPath = destination,
                SourceSize = DiskProvider.GetFileSize(source),
                SourceModifiedUtc = _modified
            });
        }

        [Test]
        public void inventory_should_include_every_supported_audio_and_ebook_file_but_not_sidecars()
        {
            var epub = Path.Combine(_output, "subfolder", "book.EPUB");
            FileSystem.AddFile(epub, new MockFileData("owned ebook"));
            FileSystem.AddFile(Path.Combine(_output, "cover.jpg"), new MockFileData("cover"));
            FileSystem.AddFile(Path.Combine(_output, "archive.zip"), new MockFileData("archive"));

            var inventory = Subject.Scan(_tracked);

            inventory.Select(file => file.Path).Should().BeEquivalentTo(_receipt.Files.Select(file => file.SourcePath).Append(epub));
            inventory.Should().OnlyContain(file => file.Size > 0 && file.ModifiedUtc.Kind == DateTimeKind.Utc);
        }

        [Test]
        public void mapped_import_path_should_take_precedence_over_the_remote_output()
        {
            _tracked.DownloadItem.OutputPath = new OsPath(@"C:\remote\other-job".AsOsAgnostic());
            _tracked.ImportItem = new DownloadClientItem { OutputPath = new OsPath(_output) };

            Subject.Scan(_tracked).Should().HaveCount(2);
            Subject.Retire(_tracked, _receipt).Should().BeTrue();
        }

        [Test]
        public void restart_should_initialize_the_mapped_import_item_before_scanning_or_retiring()
        {
            _tracked.DownloadItem.OutputPath = new OsPath(@"Z:\remote\owned-job");
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(_tracked.DownloadItem, null))
                .Returns(new DownloadClientItem { OutputPath = new OsPath(_output) });

            Subject.Scan(_tracked).Should().HaveCount(2);
            Subject.Retire(_tracked, _receipt).Should().BeTrue();

            _tracked.ImportItem.OutputPath.FullPath.Should().Be(_output);
        }

        [Test]
        public void changed_remote_mapping_should_refresh_cached_import_authority_before_scan_or_cleanup()
        {
            var remappedRoot = @"C:\downloads\remapped-job".AsOsAgnostic();
            var remappedSource = Path.Combine(remappedRoot, "new-recording.m4b");
            FileSystem.AddFile(remappedSource, new MockFileData("owned remapped source"));
            _tracked.DownloadItem.OutputPath = new OsPath(@"Z:\remote\owned-job");
            _tracked.ImportItem = new DownloadClientItem { OutputPath = new OsPath(_output) };
            var refreshed = new DownloadClientItem { OutputPath = new OsPath(remappedRoot) };
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(_tracked.DownloadItem, It.IsAny<DownloadClientItem>()))
                .Returns(refreshed);

            Subject.Scan(_tracked).Should().ContainSingle().Which.Path.Should().Be(remappedSource);
            Subject.Validate(_tracked, _receipt).Should().BeFalse();
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            _tracked.ImportItem.Should().BeSameAs(refreshed);
            AssertSourcesRemain();
            FileSystem.File.ReadAllText(remappedSource).Should().Be("owned remapped source");
        }

        [Test]
        public void unavailable_current_mapping_should_not_fall_back_to_cached_authority()
        {
            _tracked.ImportItem = new DownloadClientItem { OutputPath = new OsPath(_output) };
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(_tracked.DownloadItem, It.IsAny<DownloadClientItem>()))
                .Returns((DownloadClientItem)null);

            Assert.Throws<InvalidOperationException>(() => Subject.Scan(_tracked));
            Subject.Validate(_tracked, _receipt).Should().BeFalse();
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            AssertSourcesRemain();
        }

        [Test]
        public void a_file_output_should_be_scanned_and_retired_without_treating_its_parent_as_owned()
        {
            var selected = _receipt.Files.First();
            selected.PartCount = 1;
            _tracked.DownloadItem.OutputPath = new OsPath(selected.SourcePath);
            _receipt.OutputPath = selected.SourcePath;
            _receipt.Files = new List<DownloadImportFile> { selected };

            Subject.Scan(_tracked).Should().ContainSingle().Which.Path.Should().Be(selected.SourcePath);
            Subject.Retire(_tracked, _receipt).Should().BeTrue();

            FileSystem.File.Exists(selected.SourcePath).Should().BeFalse();
            FileSystem.File.Exists(Path.Combine(_output, "02.m4b")).Should().BeTrue();
        }

        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        public void copy_and_busy_downloads_should_keep_sources(bool trackedCopy, bool receiptedCopy, bool canMove)
        {
            _tracked.KeepSourceFiles = trackedCopy;
            _receipt.KeepSourceFiles = receiptedCopy;
            _tracked.DownloadItem.CanMoveFiles = canMove;

            Subject.Retire(_tracked, _receipt).Should().BeTrue();

            _receipt.Files.Should().OnlyContain(file => FileSystem.File.Exists(file.SourcePath));
        }

        [Test]
        public void a_proven_download_should_retire_only_its_sources_and_keep_destinations_and_sidecars()
        {
            var cover = Path.Combine(_output, "cover.jpg");
            var archive = Path.Combine(_output, "archive.zip");
            FileSystem.AddFile(cover, new MockFileData("owned cover"));
            FileSystem.AddFile(archive, new MockFileData("owned archive"));

            Subject.Retire(_tracked, _receipt).Should().BeTrue();

            _receipt.Files.Should().OnlyContain(file => !FileSystem.File.Exists(file.SourcePath) && FileSystem.File.Exists(file.ImportedPath));
            FileSystem.File.Exists(cover).Should().BeTrue();
            FileSystem.File.Exists(archive).Should().BeTrue();
            FileSystem.Directory.Exists(_output).Should().BeTrue();
            Subject.Scan(_tracked).Should().BeEmpty();
            Subject.Retire(_tracked, _receipt).Should().BeTrue();
        }

        [Test]
        public void validating_receipt_authority_should_never_delete_sources()
        {
            Subject.Validate(_tracked, _receipt).Should().BeTrue();

            AssertSourcesRemain();
            _receipt.Files.Should().OnlyContain(file => FileSystem.File.Exists(file.ImportedPath));
        }

        [Test]
        public void new_eligible_files_should_block_all_retirement_before_any_source_is_deleted()
        {
            FileSystem.AddFile(Path.Combine(_output, "unrecognized.pdf"), new MockFileData("new file"));

            Subject.Validate(_tracked, _receipt).Should().BeFalse();
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            AssertSourcesRemain();
        }

        [TestCase("source-size")]
        [TestCase("source-time")]
        [TestCase("missing-destination")]
        [TestCase("live-file-id")]
        [TestCase("live-edition")]
        [TestCase("live-part")]
        [TestCase("live-path")]
        [TestCase("edition-book")]
        public void changed_second_part_should_block_the_entire_batch_before_any_deletion(string change)
        {
            var selected = _receipt.Files.Last();
            switch (change)
            {
                case "source-size":
                    FileSystem.File.AppendAllText(selected.SourcePath, "changed");
                    break;
                case "source-time":
                    FileSystem.File.SetLastWriteTimeUtc(selected.SourcePath, _modified.AddSeconds(1));
                    break;
                case "missing-destination":
                    FileSystem.File.Delete(selected.ImportedPath);
                    break;
                case "live-file-id":
                    _files.Remove(selected.FileId);
                    break;
                case "live-edition":
                    _files[selected.FileId].EditionId = 99;
                    break;
                case "live-part":
                    _files[selected.FileId].Part = 99;
                    break;
                case "live-path":
                    _files[selected.FileId].Path = Path.Combine(_library, "other.m4b");
                    break;
                case "edition-book":
                    Mocker.GetMock<IEditionService>().Setup(service => service.GetEdition(8)).Returns(new Edition { Id = 8, BookId = 99 });
                    break;
            }

            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            AssertSourcesRemain();
        }

        [TestCase("download-id")]
        [TestCase("client-id")]
        [TestCase("output-root")]
        [TestCase("outside-source")]
        [TestCase("source-is-destination")]
        [TestCase("destination-in-output")]
        [TestCase("duplicate-source")]
        [TestCase("wrong-book-set")]
        public void mismatched_receipt_authority_should_never_delete_sources(string change)
        {
            switch (change)
            {
                case "download-id":
                    _receipt.DownloadId = "other-job";
                    break;
                case "client-id":
                    _receipt.DownloadClientId++;
                    break;
                case "output-root":
                    _receipt.OutputPath = Path.Combine(_output, "other-job");
                    break;
                case "outside-source":
                    _receipt.Files.Last().SourcePath = @"C:\outside\02.m4b".AsOsAgnostic();
                    break;
                case "source-is-destination":
                    _receipt.Files.Last().SourcePath = _receipt.Files.Last().ImportedPath;
                    break;
                case "destination-in-output":
                    _receipt.Files.Last().ImportedPath = Path.Combine(_output, "target.m4b");
                    break;
                case "duplicate-source":
                    _receipt.Files.Last().SourcePath = _receipt.Files.First().SourcePath;
                    break;
                case "wrong-book-set":
                    _receipt.BookIds = new List<int> { 99 };
                    break;
            }

            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            FileSystem.File.Exists(_receipt.Files.First().SourcePath).Should().BeTrue();
            FileSystem.File.Exists(Path.Combine(_output, "02.m4b")).Should().BeTrue();
        }

        [TestCase("source-file")]
        [TestCase("source-directory")]
        [TestCase("source-ancestor")]
        [TestCase("destination-file")]
        public void symbolic_links_should_not_authorize_retirement(string link)
        {
            var selected = _receipt.Files.Last();
            var path = link switch
            {
                "source-file" => selected.SourcePath,
                "source-directory" => _output,
                "source-ancestor" => Path.GetDirectoryName(_output),
                _ => selected.ImportedPath
            };
            FileSystem.File.SetAttributes(path, FileSystem.File.GetAttributes(path) | FileAttributes.ReparsePoint);

            Subject.Validate(_tracked, _receipt).Should().BeFalse();
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            AssertSourcesRemain();
        }

        [Test]
        public void library_roots_and_their_parents_should_never_be_used_as_download_sources()
        {
            _tracked.DownloadItem.OutputPath = new OsPath(_library);
            Assert.Throws<InvalidOperationException>(() => Subject.Scan(_tracked));
            _tracked.DownloadItem.OutputPath = new OsPath(Path.GetDirectoryName(_library));
            Assert.Throws<InvalidOperationException>(() => Subject.Scan(_tracked));
            AssertSourcesRemain();
        }

        [Test]
        public void missing_valid_output_should_be_empty_but_empty_or_filesystem_root_paths_are_invalid()
        {
            _tracked.DownloadItem.OutputPath = new OsPath(Path.Combine(_output, "missing"));
            Subject.Scan(_tracked).Should().BeEmpty();
            _tracked.DownloadItem.OutputPath = new OsPath(string.Empty);
            Assert.Throws<InvalidOperationException>(() => Subject.Scan(_tracked));
            _tracked.DownloadItem.OutputPath = new OsPath(Path.GetPathRoot(_output));
            Assert.Throws<InvalidOperationException>(() => Subject.Scan(_tracked));
        }

        [Test]
        public void deletion_failure_should_keep_the_download_pending_and_preserve_remaining_sources()
        {
            var failing = new Mock<IDiskProvider>();
            failing.Setup(provider => provider.FileExists(It.IsAny<string>())).Returns<string>(DiskProvider.FileExists);
            failing.Setup(provider => provider.FolderExists(It.IsAny<string>())).Returns<string>(DiskProvider.FolderExists);
            failing.Setup(provider => provider.GetFileAttributes(It.IsAny<string>())).Returns<string>(DiskProvider.GetFileAttributes);
            failing.Setup(provider => provider.GetFileSize(It.IsAny<string>())).Returns<string>(DiskProvider.GetFileSize);
            failing.Setup(provider => provider.FileGetLastWrite(It.IsAny<string>())).Returns<string>(DiskProvider.FileGetLastWrite);
            failing.Setup(provider => provider.GetFiles(It.IsAny<string>(), false)).Returns<string, bool>((path, _) => DiskProvider.GetFiles(path, false));
            failing.Setup(provider => provider.GetDirectories(It.IsAny<string>())).Returns<string>(DiskProvider.GetDirectories);
            failing.Setup(provider => provider.DeleteFile(It.IsAny<string>())).Throws(new IOException("owned deletion failure"));
            var service = new DownloadImportSourceService(
                failing.Object,
                Mocker.GetMock<IRootFolderService>().Object,
                Mocker.GetMock<IMediaFileService>().Object,
                Mocker.GetMock<IEditionService>().Object,
                Mocker.GetMock<IProvideImportItemService>().Object,
                TestLogger);

            service.Retire(_tracked, _receipt).Should().BeFalse();

            AssertSourcesRemain();
        }

        private void AssertSourcesRemain()
        {
            FileSystem.File.Exists(Path.Combine(_output, "01.mp3")).Should().BeTrue();
            FileSystem.File.Exists(Path.Combine(_output, "02.m4b")).Should().BeTrue();
        }
    }
}
