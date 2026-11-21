using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadGrabSpaceServiceFixture : CoreTest<DownloadGrabSpaceService>
    {
        private string _root;
        private string _authorPath;
        private RemoteBook _remote;
        private Mock<IMount> _mount;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(TempFolder, "library");
            _authorPath = Path.Combine(_root, "author");
            _remote = new RemoteBook { Author = new Author { Id = 3, Path = _authorPath }, Release = new ReleaseInfo { Size = 400.Megabytes() } };
            _mount = new Mock<IMount>();
            _mount.SetupGet(mount => mount.IsReady).Returns(true);
            _mount.SetupGet(mount => mount.RootDirectory).Returns(TempFolder);
            _mount.SetupGet(mount => mount.AvailableFreeSpace).Returns(500.Megabytes());
            Mocker.GetMock<IConfigService>().SetupGet(config => config.CheckFreeSpaceWhenGrabbing).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.MinimumFreeSpaceWhenImporting).Returns(100);
            Mocker.GetMock<IRootFolderService>().Setup(service => service.GetBestRootFolder(_authorPath)).Returns(new RootFolder { Path = _root });
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthor(3)).Returns(_remote.Author);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FolderExists(_root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetFileAttributes(_authorPath)).Returns(FileAttributes.Directory);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetMount(_authorPath)).Returns(_mount.Object);
        }

        [Test]
        public void disabled_check_should_preserve_unknown_sizes_and_perform_no_disk_or_author_io()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.CheckFreeSpaceWhenGrabbing).Returns(false);
            _remote.Release.Size = 0;

            Subject.Preview(_remote).Accepted.Should().BeTrue();
            Subject.Check(_remote, new DownloadSpaceBudget()).Decision.Accepted.Should().BeTrue();
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorService>().VerifyNoOtherCalls();
        }

        [Test]
        public void full_reported_size_and_reserve_can_fit_exactly_but_one_extra_byte_fails()
        {
            var assessment = Subject.Check(_remote, null);

            assessment.Decision.Accepted.Should().BeTrue();
            assessment.Bytes.Should().Be(_remote.Release.Size);
            _remote.Release.Size++;
            Subject.Preview(_remote).Code.Should().Be(RejectionCode.MinimumFreeSpace);
        }

        [Test]
        public void full_copy_budget_and_reserve_apply_even_when_import_checks_are_skipped_and_links_enabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.SkipFreeSpaceCheckWhenImporting).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.CopyUsingHardlinks).Returns(true);
            _remote.Release.Size = long.MaxValue;

            Subject.Preview(_remote).Code.Should().Be(RejectionCode.MinimumFreeSpace);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void unknown_release_size_does_not_get_treated_as_zero_bytes(long size)
        {
            _remote.Release.Size = size;

            Subject.Preview(_remote).Code.Should().Be(RejectionCode.GrabFreeSpaceUnverified);
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
        }

        [Test]
        public void uncreated_author_folder_uses_its_existing_configured_root()
        {
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetFileAttributes(_authorPath)).Throws(new DirectoryNotFoundException());
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetMount(_root)).Returns(_mount.Object);

            Subject.Preview(_remote).Accepted.Should().BeTrue();
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.GetMount(_root), Times.Once());
        }

        [Test]
        public void missing_library_root_never_falls_back_to_a_parent_filesystem()
        {
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FolderExists(_root)).Returns(false);

            Subject.Preview(_remote).Code.Should().Be(RejectionCode.GrabFreeSpaceUnverified);
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.GetMount(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void permission_failure_never_falls_back_to_a_parent_filesystem()
        {
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetFileAttributes(_authorPath)).Throws(new UnauthorizedAccessException());

            Subject.Preview(_remote).Code.Should().Be(RejectionCode.GrabFreeSpaceUnverified);
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.GetMount(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void space_probe_failure_is_unverified_not_a_zero_space_measurement()
        {
            _mount.SetupGet(mount => mount.AvailableFreeSpace).Throws(new IOException());

            Subject.Preview(_remote).Code.Should().Be(RejectionCode.GrabFreeSpaceUnverified);
        }

        [Test]
        public void submission_rechecks_live_space_and_current_author_path_after_preview()
        {
            Subject.Preview(_remote).Accepted.Should().BeTrue();
            _mount.SetupGet(mount => mount.AvailableFreeSpace).Returns(499.Megabytes());
            Subject.Check(_remote, null).Decision.Code.Should().Be(RejectionCode.MinimumFreeSpace);

            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthor(3)).Returns(new Author
            {
                Id = 3, Path = Path.Combine(TempFolder, "moved_to_missing_root", "author")
            });

            Subject.Check(_remote, null).Decision.Code.Should().Be(RejectionCode.GrabFreeSpaceUnverified);
        }

        [Test]
        public void budget_counts_prior_submissions_only_on_the_resolved_mount_and_does_not_leak_to_new_batches()
        {
            _remote.Release.Size = 300.Megabytes();
            var budget = new DownloadSpaceBudget();
            var first = Subject.Check(_remote, budget);
            first.Decision.Accepted.Should().BeTrue();
            budget.RecordSubmission(first);

            Subject.Check(_remote, budget).Decision.Code.Should().Be(RejectionCode.MinimumFreeSpace);
            Subject.Check(_remote, new DownloadSpaceBudget()).Decision.Accepted.Should().BeTrue();

            // The author destination can be on a nested mount, even if the configured root is shared.
            _mount.SetupGet(mount => mount.RootDirectory).Returns(_authorPath);

            Subject.Check(_remote, budget).Decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void known_local_output_on_library_mount_requires_space_for_download_and_staged_copy()
        {
            var output = Path.Combine(TempFolder, "client output");
            var client = new DownloadClientInfo { IsLocalhost = true };
            client.OutputRootFolders.Add(new OsPath(output));
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FolderExists(output)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetMount(output)).Returns(_mount.Object);

            Subject.Preview(_remote).Accepted.Should().BeTrue();
            Subject.Check(_remote, null, client).Decision.Code.Should().Be(RejectionCode.MinimumFreeSpace);

            _mount.SetupGet(mount => mount.AvailableFreeSpace).Returns(900.Megabytes());
            Subject.Check(_remote, null, client).Bytes.Should().Be(800.Megabytes());

            _remote.Release.Size = long.MaxValue;
            Subject.Check(_remote, null, client).Decision.Code.Should().Be(RejectionCode.MinimumFreeSpace);
        }

        [Test]
        public void remote_or_unverifiable_client_output_does_not_invalidate_the_library_only_measurement()
        {
            var output = Path.Combine(TempFolder, "client output");
            var client = new DownloadClientInfo();
            client.OutputRootFolders.Add(new OsPath(output));
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FolderExists(output)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.GetMount(output)).Throws(new IOException());

            Subject.Check(_remote, null, client).Bytes.Should().Be(400.Megabytes());
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.GetMount(output), Times.Never());

            client.IsLocalhost = true;
            Subject.Check(_remote, null, client).Bytes.Should().Be(400.Megabytes());
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.GetMount(output), Times.Once());
        }
    }
}
