using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RejectedImportRepositoryFixture : DbTest<RejectedImportRepository, DownloadHistory>
    {
        private IndexerRepository _indexers;
        private BlocklistRepository _blocklist;
        private IndexerDefinition _indexer;
        private DownloadHistory _grab;
        private DownloadFailedEvent _failure;

        [SetUp]
        public void Setup()
        {
            _indexers = Mocker.Resolve<IndexerRepository>();
            _blocklist = Mocker.Resolve<BlocklistRepository>();
            _indexer = _indexers.Insert(new IndexerDefinition
            {
                Name = "Owned indexer", Implementation = "Newznab", EnableRss = true,
                Settings = new NewznabSettings(), ExecutableFileAction = RejectedImportAction.BlocklistAndRetry
            });
            _grab = Storage.Insert(new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadGrabbed, AuthorId = 18, DownloadId = "owned",
                SourceTitle = "Owned release", Date = DateTime.UtcNow.AddMinutes(-1), Protocol = DownloadProtocol.Usenet,
                IndexerId = _indexer.Id, DownloadClientId = 4
            });
            _failure = new DownloadFailedEvent
            {
                AuthorId = 18, BookIds = new List<int> { 21 }, SourceTitle = "Owned release", Quality = new QualityModel(Quality.MP3),
                DownloadId = "owned", Message = "Owned rejection", ImportRejectionCode = RejectionCode.ExecutableFile,
                Data = new Dictionary<string, string> { ["publishedDate"] = "2026-11-20T10:00:00Z", ["size"] = "42", ["indexer"] = "Owned indexer", ["protocol"] = ((int)DownloadProtocol.Usenet).ToString() }
            };
        }

        private RejectedImportRecordResult Record() => Subject.TryRecord(_grab, _failure, RejectedImportAction.BlocklistAndRetry);

        [Test]
        public void policy_should_persist_with_explicit_review_and_new_definitions_default_to_review()
        {
            var saved = _indexers.Get(_indexer.Id);
            saved.ExecutableFileAction.Should().Be(RejectedImportAction.BlocklistAndRetry);
            saved.DangerousFileAction.Should().Be(RejectedImportAction.Review);
            saved.UserRejectedExtensionAction.Should().Be(RejectedImportAction.Review);
            saved.ExecutableFileAction = RejectedImportAction.Review;
            _indexers.Update(saved);
            _indexers.Get(saved.Id).ExecutableFileAction.Should().Be(RejectedImportAction.Review);
            Record().Should().Be(RejectedImportRecordResult.Changed);
            _blocklist.Count().Should().Be(0);
        }

        [Test]
        public void record_should_commit_failure_and_blocklist_once_and_keep_other_clients_separate()
        {
            Storage.Insert(new DownloadHistory
            {
                EventType = DownloadHistoryEventType.FileImported, AuthorId = 18, DownloadId = "owned", SourceTitle = "Owned release", Date = DateTime.UtcNow,
                DownloadClientId = 9
            });
            Record().Should().Be(RejectedImportRecordResult.Applied);
            Record().Should().Be(RejectedImportRecordResult.AlreadyFailed);
            var failures = Storage.All().Where(row => row.EventType == DownloadHistoryEventType.DownloadFailed).ToList();
            failures.Should().ContainSingle();
            failures[0].DownloadClientId.Should().Be(4);
            failures[0].Data["importRejectionCode"].Should().Be(RejectionCode.ExecutableFile);
            _blocklist.Single().BookIds.Should().Equal(21);
        }

        [Test]
        public void concurrent_policy_attempts_should_commit_only_once()
        {
            var subject = Subject;
            var results = new RejectedImportRecordResult[2];
            Parallel.Invoke(() => results[0] = subject.TryRecord(_grab, _failure, RejectedImportAction.BlocklistAndRetry),
                () => results[1] = subject.TryRecord(_grab, _failure, RejectedImportAction.BlocklistAndRetry));
            results.Should().BeEquivalentTo(new[] { RejectedImportRecordResult.Applied, RejectedImportRecordResult.AlreadyFailed });
            _blocklist.Count().Should().Be(1);
            Storage.All().Count(row => row.EventType == DownloadHistoryEventType.DownloadFailed).Should().Be(1);
        }

        [TestCase(DownloadHistoryEventType.FileImported)]
        [TestCase(DownloadHistoryEventType.DownloadImported)]
        [TestCase(DownloadHistoryEventType.DownloadGrabbed)]
        public void changed_grab_or_partial_import_should_be_rechecked_at_commit(DownloadHistoryEventType eventType)
        {
            Storage.Insert(new DownloadHistory
            {
                EventType = eventType, AuthorId = 18, DownloadId = "owned", SourceTitle = "Owned release", Date = DateTime.UtcNow,
                DownloadClientId = 4
            });
            Record().Should().Be(RejectedImportRecordResult.Changed);
            _blocklist.Count().Should().Be(0);
        }

        [Test]
        public void failure_marker_write_error_should_roll_back_the_blocklist()
        {
            var database = Mocker.Resolve<IMainDatabase>();
            using (var connection = database.OpenConnection())
            {
                if (database.DatabaseType == DatabaseType.PostgreSQL)
                {
                    connection.Execute("ALTER TABLE \"DownloadHistory\" ADD CONSTRAINT \"reject_owned_failure\" CHECK (\"EventType\" <> 3)");
                }
                else
                {
                    connection.Execute("CREATE TRIGGER reject_owned_failure BEFORE INSERT ON \"DownloadHistory\" WHEN NEW.\"EventType\" = 3 BEGIN SELECT RAISE(ABORT, 'owned fixture failure'); END");
                }
            }

            Assert.Catch<Exception>(() => Record());
            _blocklist.Count().Should().Be(0);
            Storage.All().Should().ContainSingle();
        }
    }
}
