using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using WebhookNotification = NzbDrone.Core.Notifications.Webhook.Webhook;

namespace NzbDrone.Core.Test.NotificationTests
{
    public class VerifiedImportWebhookFixture : CoreTest<VerifiedImportWebhookService>
    {
        private readonly DateTime _commitDate = new DateTime(2026, 11, 20, 5, 0, 0, DateTimeKind.Utc);
        private List<DownloadHistory> _attempts;
        private List<EntityHistory> _fileHistory;
        private List<WebhookImportCompletePayload> _sent;
        private BookFile _file;
        private TrackedDownload _tracked;
        private DownloadHistory _saved;
        private DownloadImportReceipt _receipt;
        private DownloadCompletedEvent _completed;

        [SetUp]
        public void ArrangeVerifiedImport()
        {
            _attempts = new List<DownloadHistory>();
            _fileHistory = new List<EntityHistory>();
            _sent = new List<WebhookImportCompletePayload>();
            var author = new Author { Id = 4, Name = "Owned author", Tags = new HashSet<int> { 2 } };
            var book = new Book { Id = 6, AuthorId = author.Id, Title = "Owned work" };
            var edition = new Edition
            {
                Id = 7,
                BookId = book.Id,
                Book = book,
                Title = "Owned UK recording",
                ForeignEditionId = "audible:uk:B000000007",
                Asin = "B000000007",
                AudibleRegion = "uk"
            };
            _file = new BookFile
            {
                Id = 8,
                EditionId = edition.Id,
                Edition = edition,
                Author = author,
                Part = 1,
                Quality = new QualityModel(Quality.M4B),
                Path = Path.Combine(TempFolder, "private-library", "owned.m4b"),
                Size = 300,
                MediaInfo = new MediaInfoModel { Source = "MediaInfo", AudioFormat = "USAC", AudioProfile = "xHE-AAC", AudioChannels = 2 }
            };
            _receipt = new DownloadImportReceipt
            {
                AuthorId = author.Id,
                DownloadClientId = 23,
                DownloadId = "owned-id",
                BookIds = new List<int> { book.Id },
                Files = new List<DownloadImportFile>
                {
                    new DownloadImportFile
                    {
                        BookId = book.Id,
                        EditionId = edition.Id,
                        FileId = _file.Id,
                        Part = 1,
                        PartCount = 1,
                        SourcePath = Path.Combine(TempFolder, "private-download", "source.m4b"),
                        ImportedPath = _file.Path
                    }
                }
            };
            _tracked = new TrackedDownload
            {
                DownloadClient = 23,
                State = TrackedDownloadState.Imported,
                ImportReceipt = _receipt,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "owned-id",
                    Status = DownloadItemStatus.Completed,
                    DownloadClientInfo = new DownloadClientItemClientInfo { Id = 23, Name = "Owned client", Type = "Synthetic" }
                }
            };
            _saved = new DownloadHistory { Id = 1, Date = _commitDate, EventType = DownloadHistoryEventType.DownloadImported };
            _completed = new DownloadCompletedEvent(_tracked, author.Id);
            Mocker.GetMock<IDownloadHistoryService>().Setup(history => history.GetLatestDownloadHistoryItem("owned-id", It.IsAny<int>())).Returns(_saved);
            Mocker.GetMock<IDownloadImportCompletionService>().Setup(completion => completion.ReadReceipt(_tracked, _saved)).Returns(_receipt);
            Mocker.GetMock<IMediaFileService>().Setup(files => files.Get(_file.Id)).Returns(_file);
            Mocker.GetMock<IHistoryService>().Setup(history => history.Find("owned-id", EntityHistoryEventType.BookFileImported, It.IsAny<int>())).Returns(_fileHistory);
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.FindByDownloadId("owned-id")).Returns(_attempts);
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>())).Returns((DownloadHistory row) =>
            {
                row.Id = _attempts.Count + 100;
                _attempts.Add(row);
                return row;
            });
            Mocker.GetMock<IWebhookProxy>().Setup(proxy => proxy.SendWebhook(It.IsAny<WebhookImportCompletePayload>(), It.IsAny<WebhookSettings>()))
                .Callback<WebhookPayload, WebhookSettings>((payload, _) => _sent.Add((WebhookImportCompletePayload)payload));
            SetHooks(Hook(11, 2));
        }

        [Test]
        public void delivers_only_bound_completion_and_uses_the_committed_edition_and_filenames()
        {
            var grab = new DownloadHistory { Id = 10, Date = _commitDate.AddMinutes(-1), IndexerId = 9 };
            grab.Data["releaseSource"] = "Rss";
            grab.Data["indexer"] = "Owned indexer";
            grab.Data["customFormatScore"] = "17";
            Mocker.GetMock<IDownloadHistoryService>().Setup(history => history.GetLatestGrab("owned-id", 23)).Returns(grab);
            _fileHistory.Add(new EntityHistory
            {
                Date = _commitDate.AddSeconds(-1),
                AuthorId = 4,
                BookId = 6,
                Data = new Dictionary<string, string>
                {
                    ["fileId"] = "8",
                    ["editionId"] = "7",
                    ["droppedPath"] = _receipt.Files[0].SourcePath,
                    ["importedPath"] = _file.Path,
                    [CommittedImportScores.HistoryKey] = STJson.ToJson(new CommittedImportScores
                    {
                        ProfileId = 3,
                        Before = new List<CommittedFileScore> { new CommittedFileScore { Score = 3 } },
                        After = new CommittedFileScore { FileId = 8, EditionId = 7, Part = 1, Score = 11 }
                    })
                }
            });

            Subject.Handle(_completed);
            var payload = _sent.Should().ContainSingle().Which;
            payload.SchemaVersion.Should().Be(1);
            payload.EventType.Should().Be(WebhookEventType.ImportComplete);
            payload.EventId.Should().NotBeNullOrWhiteSpace();
            payload.DownloadClientId.Should().Be(23);
            payload.Indexer.Should().Be("Owned indexer");
            payload.ReleaseSource.Should().Be("Rss");
            payload.GrabCustomFormatScore.Should().Be(17);
            var recording = payload.Recordings.Should().ContainSingle().Which;
            recording.BookTitle.Should().Be("Owned work");
            recording.RecordingId.Should().Be("audible:uk:B000000007");
            recording.AudibleRegion.Should().Be("uk");
            var part = recording.Parts.Should().ContainSingle().Which;
            part.Media.AudioCodec.Should().Be("XheAac");
            part.SourceFileName.Should().Be("source.m4b");
            part.ImportedFileName.Should().Be("owned.m4b");
            part.CustomFormatScores.Before.Single().Score.Should().Be(3);
            part.CustomFormatScores.After.Score.Should().Be(11);
            payload.ToJson().Should().Contain("\"ImportComplete\"").And.NotContain("private-library").And.NotContain("private-download").And.NotContain("private-credential");
            _attempts.Should().ContainSingle(row => row.EventType == DownloadHistoryEventType.DownloadImportCompleteNotification && row.DownloadClientId == 23 && row.SourceTitle != null);
        }

        [TestCase(null)]
        [TestCase("legacy-relative.m4b")]
        public void missing_or_invalid_optional_history_paths_only_omit_the_scores(string recordedSource)
        {
            _fileHistory.Add(new EntityHistory
            {
                AuthorId = 4,
                BookId = 6,
                Date = _commitDate.AddSeconds(-1),
                Data = new Dictionary<string, string>
                {
                    ["fileId"] = "8",
                    ["editionId"] = "7",
                    ["droppedPath"] = recordedSource,
                    ["importedPath"] = _file.Path
                }
            });

            Subject.Handle(_completed);

            _sent.Should().ContainSingle().Which.Recordings.Single().Parts.Single().CustomFormatScores.Should().BeNull();
            _attempts.Should().ContainSingle();
        }

        [Test]
        public void ignores_uncommitted_or_unverifiable_completion()
        {
            _tracked.State = TrackedDownloadState.ImportBlocked;
            Subject.Handle(_completed);
            _tracked.State = TrackedDownloadState.Imported;
            Mocker.GetMock<IDownloadImportCompletionService>().Setup(completion => completion.ReadReceipt(_tracked, _saved)).Returns((DownloadImportReceipt)null);
            Subject.Handle(_completed);

            _sent.Should().BeEmpty();
            _attempts.Should().BeEmpty();
        }

        [Test]
        public void restart_skips_a_recorded_attempt_and_same_id_on_another_client_has_a_different_event()
        {
            Subject.Handle(_completed);
            new VerifiedImportWebhookService(
                Mocker.GetMock<INotificationFactory>().Object,
                Mocker.GetMock<INotificationStatusService>().Object,
                Mocker.GetMock<IDownloadImportCompletionService>().Object,
                Mocker.GetMock<IDownloadHistoryService>().Object,
                Mocker.GetMock<IDownloadHistoryRepository>().Object,
                Mocker.GetMock<IHistoryService>().Object,
                Mocker.GetMock<IMediaFileService>().Object,
                Mocker.GetMock<IConfigFileProvider>().Object,
                TestLogger).Handle(_completed);
            _sent.Should().ContainSingle();

            _tracked.DownloadClient = 29;
            _tracked.DownloadItem.DownloadClientInfo.Id = 29;
            _receipt.DownloadClientId = 29;
            Subject.Handle(_completed);

            _sent.Select(message => message.EventId).Distinct().Should().HaveCount(2);
            _attempts.Select(message => message.DownloadClientId).Should().Equal(23, 29);
            _sent.Last().Recordings.Single().Parts.Single().CustomFormatScores.Should().BeNull();
        }

        [Test]
        public void a_failed_claim_or_ineligible_tag_does_not_send()
        {
            SetHooks(Hook(11, 3));
            Subject.Handle(_completed);
            _attempts.Should().BeEmpty();
            SetHooks(Hook(11, 2));
            Mocker.GetMock<IDownloadHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<DownloadHistory>())).Returns((DownloadHistory)null);
            Subject.Handle(_completed);

            _sent.Should().BeEmpty();
        }

        [Test]
        public void provider_failure_does_not_expose_credentials_or_replay_other_connections()
        {
            var failed = Hook(12, 2);
            var successful = Hook(13, 2);
            var failingSettings = (WebhookSettings)failed.Definition.Settings;
            SetHooks(failed, successful);
            Mocker.GetMock<IWebhookProxy>().Setup(proxy => proxy.SendWebhook(It.IsAny<WebhookImportCompletePayload>(), failingSettings))
                .Throws(new InvalidOperationException("private-credential"));

            Subject.Handle(_completed);
            Subject.Handle(_completed);

            _sent.Should().ContainSingle();
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordFailure(12), Times.Once());
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(13), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void completion_setting_activates_a_completion_only_connection_and_preserves_disabled_default()
        {
            var definition = new NotificationDefinition { Settings = new WebhookSettings() };
            definition.Enable.Should().BeFalse();
            ((WebhookSettings)definition.Settings).OnImportComplete = true;
            definition.Enable.Should().BeTrue();
            definition.OnReleaseImport.Should().BeFalse();
        }

        [Test]
        public void import_history_snapshots_actual_replaced_and_imported_file_scores()
        {
            var author = _file.Author.Value;
            var preferred = new CustomFormat { Id = 98 };
            var profile = new QualityProfile
            {
                Id = 33,
                FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = preferred, Score = 28 } }
            };
            author.QualityProfile = profile;
            var edition = _file.Edition.Value;
            var replaced = new BookFile { Id = 2, EditionId = edition.Id, Part = _file.Part };
            var calculator = Mocker.GetMock<ICustomFormatCalculationService>();
            calculator.Setup(service => service.ParseCustomFormat(replaced, author)).Returns(new List<CustomFormat>());
            calculator.Setup(service => service.ParseCustomFormat(_file, author)).Returns(new List<CustomFormat> { preferred });
            var repository = Mocker.GetMock<IHistoryRepository>();
            EntityHistory saved = null;
            repository.Setup(history => history.Insert(It.IsAny<EntityHistory>())).Callback<EntityHistory>(row => saved = row);
            var historyService = new HistoryService(repository.Object, TestLogger, formatCalculator: calculator.Object);
            var local = new LocalBook
            {
                Author = author,
                Book = edition.Book.Value,
                Edition = edition,
                Quality = _file.Quality,
                Path = _receipt.Files[0].SourcePath,
                Modified = _commitDate.AddMinutes(-2)
            };
            historyService.Handle(new TrackImportedEvent(local, _file, new List<BookFile> { replaced }, true, _tracked.DownloadItem));

            profile.FormatItems[0].Score = 42;
            var snapshot = CommittedImportScores.Read(saved.Data[CommittedImportScores.HistoryKey], _file);
            snapshot.ProfileId.Should().Be(33);
            snapshot.Before.Single().Score.Should().Be(0);
            snapshot.After.Score.Should().Be(28);
        }

        private void SetHooks(params WebhookNotification[] hooks)
        {
            Mocker.GetMock<INotificationFactory>().Setup(factory => factory.OnImportCompleteEnabled()).Returns(hooks.ToList());
        }

        private WebhookNotification Hook(int id, int tag)
        {
            return new WebhookNotification(Mocker.GetMock<IWebhookProxy>().Object, Mocker.GetMock<IConfigFileProvider>().Object)
            {
                Definition = new NotificationDefinition
                {
                    Id = id,
                    Tags = new HashSet<int> { tag },
                    Settings = new WebhookSettings { OnImportComplete = true, Password = "private-credential" }
                }
            };
        }
    }
}
