using System;
using System.Collections.Generic;
using System.IO;
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
using NzbDrone.Test.Common.AutoMoq;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    [Platform("Linux")]
    public class DownloadImportLibraryAliasFixture : CoreTest<DownloadImportSourceService>
    {
        private IDiskProvider _disk;
        private string _physicalLibrary;
        private string _configuredLibrary;
        private string _alias;
        private string _source;
        private string _destination;
        private TrackedDownload _tracked;
        private DownloadImportReceipt _receipt;
        private BookFile _file;

        [SetUp]
        public void Setup()
        {
            _disk = Mocker.Resolve<IDiskProvider>(FileSystemType.Actual);
            _physicalLibrary = Path.Combine(TempFolder, "disk", "audiobooks");
            _alias = Path.Combine(TempFolder, "library-link");
            _configuredLibrary = _alias;
            var download = Path.Combine(TempFolder, "download");
            _source = Path.Combine(download, "book.m4b");
            Directory.CreateDirectory(download);
            Directory.CreateDirectory(Path.Combine(_physicalLibrary, "Author"));
            File.WriteAllText(_source, "owned source");
            File.WriteAllText(Path.Combine(_physicalLibrary, "Author", "book.m4b"), "owned committed file");
            var modified = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(_source, modified);
            _tracked = new TrackedDownload
            {
                DownloadClient = 7,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-alias-job", OutputPath = new OsPath(download), CanMoveFiles = true }
            };
            _file = new BookFile { Id = 42, EditionId = 8, Part = 1 };
            _receipt = new DownloadImportReceipt
            {
                DownloadId = "owned-alias-job",
                DownloadClientId = 7,
                AuthorId = 2,
                OutputPath = download,
                BookIds = new List<int> { 5 },
                Files = new List<DownloadImportFile>
                {
                    new DownloadImportFile
                    {
                        BookId = 5,
                        EditionId = 8,
                        FileId = 42,
                        Part = 1,
                        PartCount = 1,
                        SourcePath = _source,
                        SourceSize = _disk.GetFileSize(_source),
                        SourceModifiedUtc = modified
                    }
                }
            };
            Mocker.GetMock<IRootFolderService>().Setup(service => service.All())
                .Returns(() => new List<RootFolder> { new RootFolder { Path = _configuredLibrary } });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Get(42)).Returns(_file);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEdition(8)).Returns(new Edition { Id = 8, BookId = 5 });
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((item, previous) => previous ?? item);
        }

        [TearDown]
        public void RemoveOwnedLinks()
        {
            // All targets are inside this fixture's TempFolder; unlink before
            // the base fixture recursively cleans its owned directory.
            foreach (var path in new[] { Path.Combine(_alias, "Author", "child-link"), _alias })
            {
                if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(path);
                }
                else if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    File.Delete(path);
                }
            }
        }

        private void ConfigureAlias(bool ancestor)
        {
            if (ancestor)
            {
                Directory.CreateSymbolicLink(_alias, Path.Combine(TempFolder, "disk"));
                _configuredLibrary = Path.Combine(_alias, "audiobooks");
            }
            else
            {
                Directory.CreateSymbolicLink(_alias, _physicalLibrary);
            }

            _destination = Path.Combine(_configuredLibrary, "Author", "book.m4b");
            _file.Path = _destination;
            _receipt.Files[0].ImportedPath = _destination;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void explicitly_configured_library_aliases_should_allow_proven_destinations(bool ancestor)
        {
            ConfigureAlias(ancestor);

            Subject.Scan(_tracked).Should().ContainSingle();
            Subject.Validate(_tracked, _receipt).Should().BeTrue();
            File.ReadAllText(_source).Should().Be("owned source");
            Subject.Retire(_tracked, _receipt).Should().BeTrue();

            File.Exists(_source).Should().BeFalse();
            File.ReadAllText(_destination).Should().Be("owned committed file");
            File.ReadAllText(Path.Combine(_physicalLibrary, "Author", "book.m4b")).Should().Be("owned committed file");
        }

        [Test]
        public void the_resolved_library_target_should_never_be_accepted_as_download_source()
        {
            ConfigureAlias(false);
            _tracked.DownloadItem.OutputPath = new OsPath(_physicalLibrary);
            _receipt.OutputPath = _physicalLibrary;

            Assert.Throws<InvalidOperationException>(() => Subject.Scan(_tracked));
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            File.ReadAllText(_source).Should().Be("owned source");
            File.ReadAllText(_destination).Should().Be("owned committed file");
        }

        [Test]
        public void an_unavailable_configured_library_root_should_refuse_cleanup()
        {
            ConfigureAlias(false);
            _configuredLibrary = Path.Combine(TempFolder, "unavailable-library");

            Subject.Validate(_tracked, _receipt).Should().BeFalse();
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            File.ReadAllText(_source).Should().Be("owned source");
            File.ReadAllText(_destination).Should().Be("owned committed file");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void child_links_below_a_configured_library_root_should_not_authorize_source_cleanup(bool fileLink)
        {
            ConfigureAlias(false);
            var outside = Path.Combine(TempFolder, "outside-library");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "other.m4b"), "owned unrelated file");
            var child = Path.Combine(_alias, "Author", "child-link");
            if (fileLink)
            {
                File.CreateSymbolicLink(child, Path.Combine(outside, "other.m4b"));
                _destination = child;
            }
            else
            {
                Directory.CreateSymbolicLink(child, outside);
                _destination = Path.Combine(child, "other.m4b");
            }

            _file.Path = _destination;
            _receipt.Files[0].ImportedPath = _destination;

            Subject.Validate(_tracked, _receipt).Should().BeFalse();
            Subject.Retire(_tracked, _receipt).Should().BeFalse();

            File.ReadAllText(_source).Should().Be("owned source");
            File.ReadAllText(Path.Combine(outside, "other.m4b")).Should().Be("owned unrelated file");
        }
    }
}
