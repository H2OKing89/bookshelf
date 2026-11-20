using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.Pneumatic;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentFileInventoryFixture : DownloadClientFixtureBase<QBittorrent>
    {
        private DownloadClientItem _item;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new DownloadClientDefinition
            {
                Id = 11,
                Name = "Owned inventory client",
                Settings = new QBittorrentSettings { Host = "127.0.0.1", Port = 2222 }
            };
            _item = new DownloadClientItem
            {
                DownloadId = "ABCD1234", Title = "Owned inventory download",
                OutputPath = new OsPath("/unavailable/remote/library"),
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 11 },
                CanBeRemoved = true, CanMoveFiles = true, Status = DownloadItemStatus.Completed
            };
            Mocker.GetMock<IQBittorrentProxySelector>()
                .Setup(selector => selector.GetProxy(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                .Returns(Mocker.GetMock<IQBittorrentProxy>().Object);
            GivenFiles(new List<QBittorrentTorrentFile>());
        }

        private void GivenFiles(List<QBittorrentTorrentFile> files)
        {
            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(proxy => proxy.GetTorrentFiles("abcd1234", It.IsAny<QBittorrentSettings>())).Returns(files);
        }

        [Test]
        public void proxy_payload_should_preserve_relative_paths_and_optional_sizes_without_local_authority()
        {
            GivenFiles(Json.Deserialize<List<QBittorrentTorrentFile>>(
                "[{\"name\":\"Author/Book.M4B\",\"size\":4294967296},{\"name\":\"Author/cover.jpg\",\"size\":0},{\"name\":\"Author/notes.nfo\"}]"));

            var files = Subject.GetFiles(_item).ToList();

            files.Select(file => file.RelativePath).Should().Equal("Author/Book.M4B", "Author/cover.jpg", "Author/notes.nfo");
            files.Select(file => file.Size).Should().Equal(4294967296L, 0L, null);
            _item.OutputPath.FullPath.Should().Be("/unavailable/remote/library");
            _item.Status.Should().Be(DownloadItemStatus.Completed);
            _item.CanMoveFiles.Should().BeTrue();
            _item.CanBeRemoved.Should().BeTrue();
            _item.Removed.Should().BeFalse();
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IRemotePathMappingService>().Verify(service => service.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()), Times.Never());
        }

        [TestCase("Author\\Book\\Part 1.m4b", "Author/Book/Part 1.m4b")]
        [TestCase("Author/Book: Subtitle/Part 1.m4b", "Author/Book: Subtitle/Part 1.m4b")]
        [TestCase("作者/本.m4b", "作者/本.m4b")]
        [TestCase("書: Subtitle.m4b", "書: Subtitle.m4b")]
        [TestCase(".hidden/recording.m4b", ".hidden/recording.m4b")]
        public void valid_remote_names_should_be_normalized_without_changing_filename_meaning(string path, string expected)
        {
            GivenFiles(new List<QBittorrentTorrentFile> { new QBittorrentTorrentFile { Name = path, Size = 1 } });

            Subject.GetFiles(_item).Single().RelativePath.Should().Be(expected);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("/root/Book.m4b")]
        [TestCase("\\root\\Book.m4b")]
        [TestCase("C:\\Books\\Book.m4b")]
        [TestCase("C:Book.m4b")]
        [TestCase("\\\\server\\share\\Book.m4b")]
        [TestCase("../Book.m4b")]
        [TestCase("Books/../Book.m4b")]
        [TestCase("Books\\..\\Book.m4b")]
        [TestCase("./Book.m4b")]
        [TestCase("Books/./Book.m4b")]
        [TestCase("Books//Book.m4b")]
        [TestCase("Books/Book.m4b/")]
        [TestCase("Books/Book\nm4b")]
        [TestCase("Books/Book\0.m4b")]
        [TestCase("Books/Book\u2028.m4b")]
        public void unsafe_or_ambiguous_remote_paths_should_fail_the_inventory_as_a_whole(string path)
        {
            GivenFiles(new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile { Name = "Valid.m4b", Size = 1 },
                new QBittorrentTorrentFile { Name = path, Size = 1 }
            });

            Assert.Throws<DownloadClientException>(() => Subject.GetFiles(_item).ToList());

            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
            _item.Removed.Should().BeFalse();
        }

        [TestCase("null-response")]
        [TestCase("null-entry")]
        [TestCase("negative-size")]
        [TestCase("duplicate-path")]
        [TestCase("too-many-files")]
        [TestCase("oversized-path")]
        [TestCase("oversized-inventory")]
        public void malformed_or_excessive_inventory_should_not_return_partial_metadata(string problem)
        {
            var files = new List<QBittorrentTorrentFile>();
            switch (problem)
            {
                case "null-response":
                    files = null;
                    break;
                case "null-entry":
                    files.Add(null);
                    break;
                case "negative-size":
                    files.Add(new QBittorrentTorrentFile { Name = "Book.m4b", Size = -1 });
                    break;
                case "duplicate-path":
                    files.Add(new QBittorrentTorrentFile { Name = "Books/Book.m4b" });
                    files.Add(new QBittorrentTorrentFile { Name = "Books\\Book.m4b" });
                    break;
                case "too-many-files":
                    files.AddRange(Enumerable.Range(0, DownloadClientFile.MaximumFiles + 1)
                        .Select(index => new QBittorrentTorrentFile { Name = index + ".m4b" }));
                    break;
                case "oversized-path":
                    files.Add(new QBittorrentTorrentFile { Name = new string('a', DownloadClientFile.MaximumRelativePathLength + 1) });
                    break;
                case "oversized-inventory":
                    files.AddRange(Enumerable.Range(0, 300)
                        .Select(index => new QBittorrentTorrentFile { Name = new string('a', 4000) + index + ".m4b" }));
                    break;
            }

            GivenFiles(files);

            Assert.Throws<DownloadClientException>(() => Subject.GetFiles(_item).ToList());
        }

        [Test]
        public void empty_inventory_should_remain_supported_without_claiming_completion()
        {
            Subject.GetFiles(_item).Should().BeEmpty();
            _item.Removed.Should().BeFalse();
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
        }

        [Test]
        public void inventory_from_a_different_client_should_never_be_requested()
        {
            _item.DownloadClientInfo.Id = 12;

            Assert.Throws<DownloadClientException>(() => Subject.GetFiles(_item).ToList());

            Mocker.GetMock<IQBittorrentProxy>().Verify(proxy => proxy.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void a_download_id_is_required_before_requesting_inventory(string id)
        {
            _item.DownloadId = id;

            Assert.Throws<DownloadClientException>(() => Subject.GetFiles(_item).ToList());

            Mocker.GetMock<IQBittorrentProxy>().Verify(proxy => proxy.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void proxy_failure_should_reach_the_diagnostic_boundary_without_mutating_item_state()
        {
            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(proxy => proxy.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>())).Throws<InvalidOperationException>();

            Assert.Throws<InvalidOperationException>(() => Subject.GetFiles(_item).ToList());

            _item.Status.Should().Be(DownloadItemStatus.Completed);
            _item.Removed.Should().BeFalse();
        }

        [Test]
        public void unsupported_clients_should_return_empty_without_disk_or_network_access()
        {
            var unsupported = Mocker.Resolve<Pneumatic>();

            unsupported.GetFiles(_item).Should().BeEmpty();
            unsupported.GetFiles(null).Should().BeEmpty();
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
            Mocker.GetMock<IQBittorrentProxy>().Verify(proxy => proxy.GetTorrentFiles(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }
    }
}
