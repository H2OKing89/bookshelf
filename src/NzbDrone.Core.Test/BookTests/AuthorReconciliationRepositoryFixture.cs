using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class AuthorReconciliationRepositoryFixture : DbTest<AuthorReconciliationRepository, Author>
    {
        private const string CanonicalAsin = "B099999902";
        private const string RecordingAsin = "B099999912";
        private const string AlternateName = "Synthetic Author Variant";

        private Author _source;
        private Author _target;
        private Book _book;
        private Edition _edition;
        private BookFile _file;
        private AuthorReconciliationConfirmation _confirmation;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.GetTrackedDownloads()).Returns(new List<TrackedDownload>());
            var path = Path.Combine(TempFolder, "Shared Author Folder");
            _source = AddAuthor(AudiobookId.ForName("us", AlternateName), AlternateName, "source-author", path, false);
            _target = AddAuthor(AudiobookId.ForAsin("us", CanonicalAsin), "Synthetic Author", "canonical-author", path, true, AlternateName);
            _book = Db.Insert(new Book
            {
                AuthorMetadataId = _source.AuthorMetadataId,
                ForeignBookId = AudiobookId.ForAsin("us", RecordingAsin),
                Title = "Synthetic Recording",
                TitleSlug = "synthetic-recording",
                CleanTitle = "syntheticrecording",
                Monitored = false
            });
            _edition = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = _book.ForeignBookId,
                Title = _book.Title,
                TitleSlug = _book.TitleSlug,
                Asin = RecordingAsin,
                AudibleRegion = "us",
                Monitored = true
            });
            _file = Db.Insert(new BookFile
            {
                EditionId = _edition.Id,
                Path = Path.Combine(path, "Synthetic.m4b"),
                Part = 1,
                Quality = new QualityModel(Quality.M4B),
                DateAdded = DateTime.UtcNow,
                Modified = DateTime.UtcNow
            });
            Db.Insert(new EntityHistory
            {
                AuthorId = _source.Id,
                BookId = _book.Id,
                DownloadId = "synthetic-download",
                SourceTitle = _book.Title,
                Date = DateTime.UtcNow,
                Quality = _file.Quality,
                EventType = EntityHistoryEventType.BookFileImported
            });
            _confirmation = new AuthorReconciliationConfirmation
            {
                SourceAuthorId = _source.Id,
                TargetAuthorId = _target.Id,
                EvidenceUrl = "https://synthetic.invalid/author-credit",
                Reason = "The listed credits point to the same author."
            };
        }

        [Test]
        public void should_move_only_author_references_and_preserve_the_original_receipt_identity()
        {
            var receipt = new DownloadImportReceipt
            {
                AuthorId = _source.Id,
                DownloadClientId = 4,
                DownloadId = "synthetic-download",
                OutputPath = Path.Combine(TempFolder, "downloads"),
                KeepSourceFiles = true,
                BookIds = new List<int> { _book.Id }
            };
            Db.Insert(new DownloadHistory
            {
                AuthorId = _source.Id,
                DownloadId = receipt.DownloadId,
                DownloadClientId = receipt.DownloadClientId,
                SourceTitle = _book.Title,
                Date = DateTime.UtcNow,
                EventType = DownloadHistoryEventType.DownloadImported,
                Data = new Dictionary<string, string> { [DownloadHistoryService.ImportReceiptKey] = STJson.ToJson(receipt) }
            });
            Db.Insert(new DownloadHistory
            {
                AuthorId = _source.Id,
                DownloadId = receipt.DownloadId,
                DownloadClientId = receipt.DownloadClientId,
                SourceTitle = _book.Title,
                Date = DateTime.UtcNow,
                EventType = DownloadHistoryEventType.DownloadImportCompleteNotification,
                Data = new Dictionary<string, string> { [VerifiedImportWebhookService.ContextKey] = "owned-context" }
            });

            Subject.Candidates(_source.Id).Select(item => item.Id).Should().Equal(_target.Id);
            var preview = Subject.Preview(_confirmation);
            preview.CanApply.Should().BeTrue();
            preview.SettingsDifferences.Should().Equal("monitoring");
            preview.Recordings.Single().ForeignEditionId.Should().Be(_edition.ForeignEditionId);
            preview.Files.Single().Id.Should().Be(_file.Id);
            preview.References.Single(row => row.Table == "History").Count.Should().Be(1);

            _confirmation.PreviewToken = preview.PreviewToken;
            Assert.That(() => Subject.Apply(_confirmation), Throws.TypeOf<NzbDroneClientException>());
            _confirmation.KeepCanonicalSettings = true;
            Subject.Apply(_confirmation);

            Db.Single<Author>().Id.Should().Be(_target.Id);
            Db.Single<Author>().Monitored.Should().BeTrue();
            Db.Single<Book>().Id.Should().Be(_book.Id);
            Db.Single<Book>().AuthorMetadataId.Should().Be(_target.AuthorMetadataId);
            Db.Single<Book>().Monitored.Should().BeFalse();
            Db.Single<Edition>().ForeignEditionId.Should().Be(_edition.ForeignEditionId);
            Db.Single<BookFile>().Id.Should().Be(_file.Id);
            Db.Single<BookFile>().Path.Should().Be(_file.Path);
            Db.Single<EntityHistory>().AuthorId.Should().Be(_target.Id);
            var histories = Db.All<DownloadHistory>();
            histories.Should().OnlyContain(row => row.AuthorId == _target.Id);
            var imported = histories.Single(row => row.EventType == DownloadHistoryEventType.DownloadImported);
            var stored = STJson.Deserialize<DownloadImportReceipt>(DownloadImportCompletionService.DataValue(imported.Data, DownloadHistoryService.ImportReceiptKey));
            stored.AuthorId.Should().Be(_target.Id);
            stored.OriginalAuthorId.Should().Be(_source.Id);
            stored.OutputPath.Should().Be(receipt.OutputPath);
            stored.KeepSourceFiles.Should().BeTrue();
            stored.BookIds.Should().Equal(_book.Id);
            var claim = histories.Single(row => row.EventType == DownloadHistoryEventType.DownloadImportCompleteNotification);
            DownloadImportCompletionService.DataValue(claim.Data, VerifiedImportWebhookService.ContextKey).Should().Be("owned-context");
            var correction = Db.All<AudiobookMetadataOverride>().Single(row => row.Asin == CanonicalAsin);
            correction.Aliases.Should().Contain(AlternateName);
            correction.SourceUrl.Should().Be(_confirmation.EvidenceUrl);
        }

        [Test]
        public void should_reject_a_stale_preview_after_a_recording_changed()
        {
            _confirmation.KeepCanonicalSettings = true;
            _confirmation.PreviewToken = Subject.Preview(_confirmation).PreviewToken;
            _book.Monitored = true;
            Db.Update(_book);

            Assert.That(() => Subject.Apply(_confirmation), Throws.TypeOf<NzbDroneClientException>().With.Message.Contains("preview changed"));
            Db.All<Author>().Should().HaveCount(2);
            Db.Single<Book>().AuthorMetadataId.Should().Be(_source.AuthorMetadataId);
        }

        [Test]
        public void should_roll_back_catalog_and_mapping_after_a_database_error()
        {
            _confirmation.KeepCanonicalSettings = true;
            _confirmation.PreviewToken = Subject.Preview(_confirmation).PreviewToken;
            using (var connection = Db.OpenConnection())
            {
                if (Db.DatabaseType == DatabaseType.SQLite)
                {
                    connection.Execute("""
                        CREATE TRIGGER "OwnedReconciliationFailure" BEFORE DELETE ON "Authors"
                        BEGIN SELECT RAISE(ABORT, 'Owned reconciliation rollback'); END
                        """);
                }
                else
                {
                    connection.Execute("""
                        CREATE FUNCTION "owned_reconciliation_failure"() RETURNS trigger AS $$
                        BEGIN RAISE EXCEPTION 'Owned reconciliation rollback'; END;
                        $$ LANGUAGE plpgsql;
                        CREATE TRIGGER "OwnedReconciliationFailure" BEFORE DELETE ON "Authors"
                        FOR EACH ROW EXECUTE FUNCTION "owned_reconciliation_failure"()
                        """);
                }
            }

            Assert.That(() => Subject.Apply(_confirmation), Throws.InstanceOf<DbException>().With.Message.Contains("Owned reconciliation rollback"));
            Db.All<Author>().Should().HaveCount(2);
            Db.Single<Book>().AuthorMetadataId.Should().Be(_source.AuthorMetadataId);
            Db.Single<EntityHistory>().AuthorId.Should().Be(_source.Id);
            Db.All<AudiobookMetadataOverride>().Should().NotContain(row => row.Asin == CanonicalAsin);
        }

        [Test]
        public void should_block_a_name_shared_with_another_explicit_identity()
        {
            AddAuthor(AudiobookId.ForAsin("us", "B099999903"), AlternateName, "other-canonical-author", _target.Path, false);
            var preview = Subject.Preview(_confirmation);

            preview.CanApply.Should().BeFalse();
            preview.Blockers.Should().Contain(message => message.Contains("does not resolve uniquely"));
        }

        [Test]
        public void should_block_different_folders_and_uncertain_download_submissions()
        {
            _source.Path = Path.Combine(TempFolder, "Other Folder");
            Db.Update(_source);
            Db.Insert(new DownloadHistory
            {
                AuthorId = _target.Id,
                DownloadId = "submission:owned",
                SourceTitle = _book.Title,
                Date = DateTime.UtcNow,
                EventType = DownloadHistoryEventType.DownloadSubmissionPending
            });

            var preview = Subject.Preview(_confirmation);
            preview.CanApply.Should().BeFalse();
            preview.Blockers.Should().Contain(message => message.Contains("author folders differ"));
            preview.Blockers.Should().Contain(message => message.Contains("pending download submissions"));
        }

        private Author AddAuthor(string foreignId, string name, string slug, string path, bool monitored, params string[] aliases)
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                Name = name,
                SortName = name.ToLowerInvariant(),
                NameLastFirst = name,
                SortNameLastFirst = name.ToLowerInvariant(),
                ForeignAuthorId = foreignId,
                TitleSlug = slug,
                Aliases = aliases.ToList()
            });
            return Db.Insert(new Author
            {
                AuthorMetadataId = metadata.Id,
                CleanName = name.ToLowerInvariant().Replace(" ", string.Empty),
                Path = path,
                Monitored = monitored,
                QualityProfileId = 1,
                MetadataProfileId = 1
            });
        }
    }
}
