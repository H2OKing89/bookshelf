using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Test.Common;
using Readarr.Api.V1.System.Backup;
using Readarr.Http.ErrorManagement;
using Readarr.Http.REST;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class BackupControllerRestoreFixture : TestBase
    {
        private const string Payload = "synthetic backup content";
        private Mock<IBackupService> _backupService;
        private Mock<IDiskProvider> _diskProvider;
        private BackupController _controller;
        private DefaultHttpContext _context;
        private TrackingStream _uploadStream;
        private List<string> _uploadPaths;

        [SetUp]
        public void Setup()
        {
            _uploadStream = null;
            _uploadPaths = new List<string>();
            _backupService = new Mock<IBackupService>();
            _diskProvider = new Mock<IDiskProvider>();
            _diskProvider.Setup(x => x.FolderExists(It.IsAny<string>()))
                .Returns<string>(Directory.Exists);
            _diskProvider.Setup(x => x.EnsureFolder(It.IsAny<string>()))
                .Callback<string>(path => Directory.CreateDirectory(path));
            _diskProvider.Setup(x => x.SaveStream(It.IsAny<Stream>(), It.IsAny<string>()))
                .Callback<Stream, string>((stream, path) =>
                {
                    _uploadPaths.Add(path);
                    _diskProvider.Verify(
                        x => x.SetPermissions(Path.GetDirectoryName(path), "700", null),
                        OsInfo.IsNotWindows ? Times.Once() : Times.Never());
                    using (var destination = File.Create(path))
                    {
                        stream.CopyTo(destination);
                    }
                });
            _diskProvider.Setup(x => x.DeleteFolder(It.IsAny<string>(), true))
                .Callback<string, bool>(Directory.Delete);
            var appFolders = new Mock<IAppFolderInfo>();
            appFolders.SetupGet(x => x.TempFolder).Returns(TempFolder);
            _context = new DefaultHttpContext();
            _context.Request.Method = "POST";
            _context.Request.Path = "/api/v1/system/backup/restore/upload";
            _context.Request.ContentType = "multipart/form-data; boundary=synthetic";
            _controller = new BackupController(_backupService.Object, appFolders.Object, _diskProvider.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = _context }
            };
        }

        [TearDown]
        public void Cleanup()
        {
            _uploadStream?.Dispose();
        }

        [TestCase(".zip")]
        [TestCase(".ZIP")]
        [TestCase(".DB")]
        [TestCase(".XML")]
        public void should_accept_supported_extensions_and_dispose_and_clean_up_the_upload(string extension)
        {
            WithUpload("backup" + extension);
            _backupService.Setup(x => x.Restore(It.IsAny<string>()))
                .Callback<string>(path =>
                {
                    File.ReadAllText(path).Should().Be(Payload);
                    Path.GetExtension(path).Should().Be(extension.ToLowerInvariant());
                    _uploadStream.Disposed.Should().BeTrue();
                });

            var result = _controller.UploadAndRestore();

            result.GetType().GetProperty("RestartRequired").GetValue(result).Should().Be(true);
            _uploadPaths.Should().ContainSingle();
            File.Exists(_uploadPaths[0]).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(_uploadPaths[0])).Should().BeFalse();
            _uploadStream.Disposed.Should().BeTrue();
            _backupService.Verify(x => x.Restore(_uploadPaths[0]), Times.Once());
            _diskProvider.Verify(x => x.DeleteFolder(Path.GetDirectoryName(_uploadPaths[0]), true), Times.Once());
        }

        [Test]
        public void should_use_independent_paths_for_uploads_with_the_same_filename()
        {
            WithUpload("backup.zip");
            _controller.UploadAndRestore();
            WithUpload("backup.zip");

            _controller.UploadAndRestore();

            _uploadPaths.Should().OnlyHaveUniqueItems();
            _uploadPaths.Should().HaveCount(2);
            foreach (var path in _uploadPaths)
            {
                Directory.GetParent(Path.GetDirectoryName(path)).FullName.Should().Be(TempFolder);
                File.Exists(path).Should().BeFalse();
                Directory.Exists(Path.GetDirectoryName(path)).Should().BeFalse();
            }
        }

        [Test]
        public async Task should_return_bad_request_and_clean_up_when_restore_validation_fails()
        {
            WithUpload("backup.db");
            _backupService.Setup(x => x.Restore(It.IsAny<string>()))
                .Throws(new RestoreBackupFailedException(HttpStatusCode.BadRequest, "Backup database is invalid"));
            Action restore = () => _controller.UploadAndRestore();

            var exception = restore.Should().Throw<RestoreBackupFailedException>().Which;
            _context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            {
                Error = exception,
                Path = _context.Request.Path
            });
            using var response = new MemoryStream();
            _context.Response.Body = response;
            await new ReadarrErrorPipeline(TestLogger).HandleException(_context);

            _context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            Encoding.UTF8.GetString(response.ToArray()).Should().Contain("Backup database is invalid");
            _uploadStream.Disposed.Should().BeTrue();
            File.Exists(_uploadPaths[0]).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(_uploadPaths[0])).Should().BeFalse();
            _diskProvider.Verify(x => x.DeleteFolder(Path.GetDirectoryName(_uploadPaths[0]), true), Times.Once());
        }

        [Test]
        public void should_dispose_the_input_and_remove_a_partial_upload_when_saving_fails()
        {
            WithUpload("backup.zip");
            string uploadPath = null;
            _diskProvider.Setup(x => x.SaveStream(It.IsAny<Stream>(), It.IsAny<string>()))
                .Callback<Stream, string>((stream, path) =>
                {
                    uploadPath = path;
                    File.WriteAllText(path, "partial content");
                    throw new IOException("Simulated upload failure");
                });
            Action restore = () => _controller.UploadAndRestore();

            restore.Should().Throw<IOException>().WithMessage("Simulated upload failure");

            _uploadStream.Disposed.Should().BeTrue();
            File.Exists(uploadPath).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(uploadPath)).Should().BeFalse();
            _backupService.Verify(x => x.Restore(It.IsAny<string>()), Times.Never());
            _diskProvider.Verify(x => x.DeleteFolder(Path.GetDirectoryName(uploadPath), true), Times.Once());
        }

        [Test]
        public void should_not_open_the_upload_if_private_permissions_cannot_be_applied()
        {
            PosixOnly();
            var file = WithUpload("backup.zip");
            _diskProvider.Setup(x => x.SetPermissions(It.IsAny<string>(), "700", null))
                .Throws(new UnauthorizedAccessException("Simulated permissions failure"));
            Action restore = () => _controller.UploadAndRestore();

            restore.Should().Throw<UnauthorizedAccessException>();

            Directory.GetDirectories(TempFolder).Should().BeEmpty();
            file.Verify(x => x.OpenReadStream(), Times.Never());
            _backupService.Verify(x => x.Restore(It.IsAny<string>()), Times.Never());
            _diskProvider.Verify(x => x.DeleteFolder(It.IsAny<string>(), true), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_folder_creation_errors_and_clean_up_only_existing_directories(bool directoryCreated)
        {
            var file = WithUpload("backup.zip");
            var failure = new IOException("Simulated directory creation failure");
            _diskProvider.Setup(x => x.EnsureFolder(It.IsAny<string>()))
                .Callback<string>(path =>
                {
                    if (directoryCreated)
                    {
                        Directory.CreateDirectory(path);
                    }

                    throw failure;
                });
            Action restore = () => _controller.UploadAndRestore();

            restore.Should().Throw<IOException>().Which.Should().BeSameAs(failure);

            Directory.GetDirectories(TempFolder).Should().BeEmpty();
            file.Verify(x => x.OpenReadStream(), Times.Never());
            _backupService.Verify(x => x.Restore(It.IsAny<string>()), Times.Never());
            _diskProvider.Verify(x => x.FolderExists(It.IsAny<string>()), Times.Once());
            _diskProvider.Verify(x => x.DeleteFolder(It.IsAny<string>(), true), directoryCreated ? Times.Once() : Times.Never());
        }

        [Test]
        public void should_remove_the_private_directory_when_opening_the_input_fails()
        {
            var file = WithUpload("backup.zip");
            file.Setup(x => x.OpenReadStream()).Throws(new IOException("Simulated input failure"));
            Action restore = () => _controller.UploadAndRestore();

            restore.Should().Throw<IOException>().WithMessage("Simulated input failure");

            Directory.GetDirectories(TempFolder).Should().BeEmpty();
            _backupService.Verify(x => x.Restore(It.IsAny<string>()), Times.Never());
            _diskProvider.Verify(x => x.DeleteFolder(It.IsAny<string>(), true), Times.Once());
        }

        [Test]
        public void should_reject_unsupported_extensions_before_opening_or_saving_any_file()
        {
            var file = WithUpload("backup.exe");
            Action restore = () => _controller.UploadAndRestore();

            restore.Should().Throw<UnsupportedMediaTypeException>();

            file.Verify(x => x.OpenReadStream(), Times.Never());
            _diskProvider.Verify(x => x.SaveStream(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never());
            _backupService.Verify(x => x.Restore(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_reject_missing_uploads_before_calling_restore()
        {
            _context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
            Action restore = () => _controller.UploadAndRestore();

            restore.Should().Throw<BadRequestException>();

            _diskProvider.Verify(x => x.SaveStream(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never());
            _backupService.Verify(x => x.Restore(It.IsAny<string>()), Times.Never());
        }

        private Mock<IFormFile> WithUpload(string filename)
        {
            _uploadStream = new TrackingStream(Encoding.UTF8.GetBytes(Payload));
            var file = new Mock<IFormFile>();
            file.SetupGet(x => x.FileName).Returns(filename);
            file.Setup(x => x.OpenReadStream()).Returns(_uploadStream);
            _context.Request.Form = new FormCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
                new FormFileCollection { file.Object });
            return file;
        }

        private sealed class TrackingStream : MemoryStream
        {
            public TrackingStream(byte[] bytes)
                : base(bytes)
            {
            }

            public bool Disposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
