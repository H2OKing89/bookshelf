using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadImportDiagnosticsFixture : FileSystemTest<DownloadImportDiagnostics>
    {
        private string _output;
        private TrackedDownload _tracked;
        private Mock<IDownloadClient> _client;

        [SetUp]
        public void Setup()
        {
            _output = @"C:\downloads\owned-job".AsOsAgnostic();
            var item = new DownloadClientItem
            {
                DownloadId = "owned", OutputPath = new OsPath(_output),
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 }
            };
            _tracked = new TrackedDownload { DownloadClient = 7, DownloadItem = item, ImportItem = item };
            _client = new Mock<IDownloadClient>();
            _client.SetupGet(client => client.Definition).Returns(new DownloadClientDefinition { Id = 7 });
            _client.Setup(client => client.GetFiles(item)).Returns(Array.Empty<DownloadClientFile>());
            Mocker.GetMock<IProvideDownloadClient>().Setup(clients => clients.Get(7)).Returns(_client.Object);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.UserRejectedExtensions).Returns(string.Empty);
            Mocker.SetConstant<IRejectedFileExtensionService>(Mocker.Resolve<RejectedFileExtensionService>());
        }

        private void File(string name)
        {
            FileSystem.AddFile(Path.Combine(_output, name), new MockFileData("owned bytes"));
        }

        private IEnumerable<string> Codes(List<TrackedDownloadStatusMessage> warnings)
        {
            return warnings.SelectMany(warning => warning.Rejections).Select(rejection => rejection.Code);
        }

        [TestCase("payload.EXE", RejectionCode.ExecutableFile)]
        [TestCase("payload.PS1", RejectionCode.DangerousFile)]
        [TestCase("payload.ZIP", RejectionCode.ArchiveNotExtracted)]
        [TestCase("payload.xyz", RejectionCode.UnsupportedFileExtension)]
        public void local_nonmedia_inventory_should_explain_why_nothing_is_importable(string name, string code)
        {
            File(name);

            Codes(Subject.GetWarnings(_tracked)).Should().Contain(code);

            FileSystem.File.ReadAllText(Path.Combine(_output, name)).Should().Be("owned bytes");
        }

        [TestCase("book.M4B")]
        [TestCase("book.EPUB")]
        [TestCase("book.kepub")]
        [TestCase("book.PDF")]
        public void accessible_media_and_sidecars_should_not_query_client_inventory(string name)
        {
            File(name);
            File("tracks.CUE");
            File("cover.JPG");

            Subject.GetWarnings(_tracked).Should().BeEmpty();

            _client.Verify(client => client.GetFiles(It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void missing_mount_should_use_remote_names_only_for_diagnostics()
        {
            _client.Setup(client => client.GetFiles(_tracked.DownloadItem)).Returns(new[]
            {
                new DownloadClientFile { RelativePath = "unmounted/Book.M4B", Size = 10 }
            });

            Codes(Subject.GetWarnings(_tracked)).Should().Contain(RejectionCode.ImportPathUnavailable);

            FileSystem.AllFiles.Should().BeEmpty();
        }

        [Test]
        public void inventory_failure_should_keep_the_existing_local_rejection()
        {
            File("payload.exe");
            _client.Setup(client => client.GetFiles(_tracked.DownloadItem)).Throws(new IOException("owned failure"));

            Codes(Subject.GetWarnings(_tracked)).Should().Contain(RejectionCode.ExecutableFile)
                .And.Contain(RejectionCode.InventoryUnavailable);
        }

        [TestCase("../outside/book.m4b")]
        [TestCase("/outside/book.m4b")]
        [TestCase(@"C:\outside\book.m4b")]
        public void malformed_remote_paths_should_not_be_used_as_local_paths(string path)
        {
            _client.Setup(client => client.GetFiles(_tracked.DownloadItem)).Returns(new[] { new DownloadClientFile { RelativePath = path } });

            Codes(Subject.GetWarnings(_tracked)).Should().Contain(RejectionCode.InventoryInvalid);

            FileSystem.AllFiles.Should().BeEmpty();
        }

        [Test]
        public void excessive_inventory_should_be_bounded_and_leave_sources_unchanged()
        {
            _client.Setup(client => client.GetFiles(_tracked.DownloadItem)).Returns(Enumerable.Range(0, 10001)
                .Select(index => new DownloadClientFile { RelativePath = "owned-" + index + ".m4b" }));

            Codes(Subject.GetWarnings(_tracked)).Should().Contain(RejectionCode.InventoryTruncated);
        }

        [Test]
        public void a_user_rejected_media_extension_should_remain_diagnostic()
        {
            File("owned.m4b");
            Mocker.GetMock<IConfigService>().SetupGet(config => config.UserRejectedExtensions).Returns(".m4b");

            Codes(Subject.GetWarnings(_tracked)).Should().Contain(RejectionCode.UserRejectedExtension);

            FileSystem.File.ReadAllText(Path.Combine(_output, "owned.m4b")).Should().Be("owned bytes");
        }
    }
}
