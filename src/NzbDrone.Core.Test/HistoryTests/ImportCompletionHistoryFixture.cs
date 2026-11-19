using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class ImportCompletionHistoryFixture : CoreTest<DownloadHistoryService>
    {
        private TrackedDownload _tracked;
        private DownloadImportReceipt _receipt;
        private List<DownloadHistory> _rows;

        [SetUp]
        public void Setup()
        {
            _tracked = new TrackedDownload
            {
                DownloadClient = 1,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-download", OutputPath = new OsPath(Path.Combine(TempFolder, "download")),
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1 }
                }
            };
            _receipt = new DownloadImportReceipt
            {
                DownloadId = "owned-download", DownloadClientId = 1, AuthorId = 2, OutputPath = _tracked.DownloadItem.OutputPath.FullPath,
                BookIds = new List<int> { 10 },
                Files = new List<DownloadImportFile>
                {
                    new DownloadImportFile
                    {
                        FileId = 20, BookId = 10, EditionId = 30, Part = 1, PartCount = 1, SourceSize = 100,
                        SourcePath = Path.Combine(TempFolder, "download", "source.m4b"),
                        ImportedPath = Path.Combine(TempFolder, "library", "imported.m4b"),
                        SourceModifiedUtc = new System.DateTime(2026, 11, 18, 1, 2, 3, System.DateTimeKind.Utc)
                    }
                }
            };
            _rows = new List<DownloadHistory>();
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.FindByDownloadId("owned-download")).Returns(() => _rows);
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>()))
                .Returns<DownloadHistory>(row =>
                {
                    row.Id = _rows.Count + 1;
                    _rows.Insert(0, row);
                    return row;
                });
        }

        [Test]
        public void verified_completion_should_be_written_synchronously_and_retries_should_not_duplicate_the_marker()
        {
            var first = Subject.RecordImportCompletion(_tracked, _receipt);
            var second = Subject.RecordImportCompletion(_tracked, _receipt);

            second.Id.Should().Be(first.Id);
            _rows.Should().ContainSingle();
            STJson.Deserialize<DownloadImportReceipt>(first.Data[DownloadHistoryService.ImportReceiptKey]).Should().BeEquivalentTo(_receipt);
        }

        [Test]
        public void simultaneous_local_completion_retries_should_not_duplicate_the_marker()
        {
            var subject = Subject;

            Parallel.Invoke(() => subject.RecordImportCompletion(_tracked, _receipt), () => subject.RecordImportCompletion(_tracked, _receipt));

            _rows.Should().ContainSingle();
        }

        [Test]
        public void failure_to_persist_receipt_should_reach_the_caller()
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>())).Throws<IOException>();

            Assert.Throws<IOException>(() => Subject.RecordImportCompletion(_tracked, _receipt));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void completion_notifications_should_never_create_an_unverified_or_duplicate_marker(bool receiptPresent)
        {
            _tracked.ImportReceipt = receiptPresent ? _receipt : null;

            Subject.Handle(new DownloadCompletedEvent(_tracked, 2));

            _rows.Should().BeEmpty();
            Mocker.GetMock<IDownloadHistoryRepository>().Verify(repository => repository.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }

        [Test]
        public void a_changed_copy_intent_should_be_persisted_as_a_new_authoritative_snapshot()
        {
            Subject.RecordImportCompletion(_tracked, _receipt);
            _receipt.KeepSourceFiles = true;

            Subject.RecordImportCompletion(_tracked, _receipt);

            _rows.Should().HaveCount(2);
            STJson.Deserialize<DownloadImportReceipt>(_rows[0].Data[DownloadHistoryService.ImportReceiptKey]).KeepSourceFiles.Should().BeTrue();
        }

        [Test]
        public void a_receipt_larger_than_the_reader_limit_should_fail_before_inserting_a_marker()
        {
            _receipt.Files[0].SourcePath = new string('x', DownloadHistoryService.MaximumReceiptLength);

            Assert.Throws<System.ArgumentException>(() => Subject.RecordImportCompletion(_tracked, _receipt));

            _rows.Should().BeEmpty();
        }

        [Test]
        public void latest_own_completion_should_remain_available_beneath_later_markers_and_other_clients()
        {
            var completion = Subject.RecordImportCompletion(_tracked, _receipt);
            _rows.Insert(0, new DownloadHistory { DownloadId = "owned-download", DownloadClientId = 1, EventType = DownloadHistoryEventType.DownloadImportIncomplete, Date = completion.Date.AddSeconds(1) });
            _rows.Insert(0, new DownloadHistory { DownloadId = "owned-download", DownloadClientId = 2, EventType = DownloadHistoryEventType.DownloadImported, Date = completion.Date.AddSeconds(2) });

            Subject.GetLatestImportCompletion("owned-download", 1).Should().BeSameAs(completion);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void receipt_collection_limits_should_match_the_reader_before_any_marker_is_written(bool tooManyFiles)
        {
            if (tooManyFiles)
            {
                _receipt.Files = Enumerable.Range(1, 10001).Select(id => new DownloadImportFile { FileId = id, BookId = 10, EditionId = 30 }).ToList();
            }
            else
            {
                _receipt.BookIds = Enumerable.Range(1, 4097).ToList();
            }

            Assert.Throws<System.ArgumentException>(() => Subject.RecordImportCompletion(_tracked, _receipt));

            _rows.Should().BeEmpty();
        }

        [Test]
        public void pending_copy_retention_should_be_durable_idempotent_and_not_replace_download_state()
        {
            var completion = Subject.RecordImportCompletion(_tracked, _receipt);
            var retained = Subject.RecordSourceRetention(_tracked);
            Subject.RecordSourceRetention(_tracked).Should().BeSameAs(retained);
            _rows.Insert(0, new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadImportIncomplete, DownloadId = "owned-download", DownloadClientId = 1,
                Date = retained.Date.AddSeconds(1)
            });
            var restarted = new TrackedDownload { DownloadClient = 1, DownloadItem = _tracked.DownloadItem };

            Subject.KeepSources(restarted).Should().BeTrue();
            Subject.GetLatestDownloadHistoryItem("owned-download").EventType.Should().Be(DownloadHistoryEventType.DownloadImportIncomplete);
            _rows.RemoveAt(0);
            Subject.GetLatestDownloadHistoryItem("owned-download").Should().BeSameAs(completion);
            _rows.Count(history => history.EventType == DownloadHistoryEventType.DownloadSourceRetained).Should().Be(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void retention_intent_should_not_be_borrowed_from_another_client_or_output(bool otherClient)
        {
            Subject.RecordSourceRetention(_tracked);
            var restarted = new TrackedDownload
            {
                DownloadClient = otherClient ? 2 : 1,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-download",
                    OutputPath = new OsPath(otherClient ? _tracked.DownloadItem.OutputPath.FullPath : Path.Combine(TempFolder, "another-job"))
                }
            };

            Subject.KeepSources(restarted).Should().BeFalse();
        }

        [TestCase("missing")]
        [TestCase("relative")]
        [TestCase("duplicate")]
        public void malformed_own_retention_path_cannot_be_used_to_authorize_deletion(string malformed)
        {
            var retained = Subject.RecordSourceRetention(_tracked);
            switch (malformed)
            {
                case "missing": retained.Data.Clear(); break;
                case "relative": retained.Data["OutputPath"] = "relative/path"; break;
                case "duplicate": retained.Data["outputPath"] = Path.Combine(TempFolder, "another-job"); break;
            }

            Subject.KeepSources(_tracked).Should().BeTrue();
        }

        [Test]
        public void retention_should_bind_to_the_mapped_local_output_path()
        {
            _tracked.DownloadItem.OutputPath = new OsPath(@"Z:\remote\owned-download");
            _tracked.ImportItem = new DownloadClientItem { OutputPath = new OsPath(_receipt.OutputPath) };

            var retained = Subject.RecordSourceRetention(_tracked);

            retained.Data["OutputPath"].Should().Be(_receipt.OutputPath);
            Subject.KeepSources(_tracked).Should().BeTrue();
        }

        [Test]
        public void retention_write_failure_should_reach_the_caller_before_import_can_start()
        {
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>())).Throws<IOException>();

            Assert.Throws<IOException>(() => Subject.RecordSourceRetention(_tracked));
        }
    }
}
