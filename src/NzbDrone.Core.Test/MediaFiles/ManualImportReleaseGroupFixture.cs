using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ManualImportReleaseGroupFixture : FileSystemTest<ManualImportService>
    {
        private string _path;
        private Author _author;
        private Book _book;
        private Edition _edition;
        private CustomFormat _preferred;
        private List<LocalBook> _imported;

        [SetUp]
        public void Setup()
        {
            _path = @"C:\downloads\Author.Book.M4B-Detected.m4b".AsOsAgnostic();
            FileSystem.AddFile(_path, new MockFileData("audio"));
            _preferred = new CustomFormat("Preferred", new ReleaseGroupSpecification { Value = "^Preferred$" }) { Id = 1 };
            _author = new Author
            {
                Id = 1,
                Path = @"C:\library\Author".AsOsAgnostic(),
                QualityProfileId = 1,
                QualityProfile = new QualityProfile
                {
                    FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = _preferred, Score = 100 } }
                }
            };
            _book = new Book { Id = 2, Author = _author };
            _edition = new Edition { Id = 3, ForeignEditionId = "local-edition", Book = _book, Monitored = true };
            _book.Editions = new List<Edition> { _edition };
            _imported = new List<LocalBook>();
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(service => service.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((item, _) => item);
            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(service => service.Find(It.IsAny<string>(), It.IsAny<int>()))
                .Returns((string id, int clientId) =>
                {
                    var tracked = Mocker.GetMock<ITrackedDownloadService>().Object.Find(id);
                    return tracked?.DownloadClient == clientId ? tracked : null;
                });

            Mocker.GetMock<IMetadataTagService>().Setup(s => s.ReadTags(It.IsAny<IFileInfo>())).Returns(new ParsedTrackInfo());
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.FilterUnchangedFiles(It.IsAny<List<IFileInfo>>(), It.IsAny<FilterFilesType>()))
                .Returns((List<IFileInfo> files, FilterFilesType filter) => files);
            Mocker.GetMock<IAugmentingService>().Setup(s => s.Augment(It.IsAny<LocalBook>(), true))
                .Callback<LocalBook, bool>((local, other) =>
                {
                    local.Author = _author;
                    local.Book = _book;
                    local.Edition = _edition;
                    local.Quality = new QualityModel(Quality.M4B);
                    local.ReleaseGroup = "Detected";
                });
            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> files, IdentificationOverrides overrides, ImportDecisionMakerConfig config) =>
                    new List<LocalEdition> { new LocalEdition(files) { Edition = _edition } });
            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification<LocalBook>>>(Array.Empty<IImportDecisionEngineSpecification<LocalBook>>());
            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification<LocalEdition>>>(Array.Empty<IImportDecisionEngineSpecification<LocalEdition>>());
            Mocker.SetConstant<IMakeImportDecision>(Mocker.Resolve<ImportDecisionMaker>());
            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat> { _preferred });
            Mocker.SetConstant<ICustomFormatCalculationService>(Mocker.Resolve<CustomFormatCalculationService>());
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(_author.Id)).Returns(_author);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(_book.Id)).Returns(_book);
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(_edition.ForeignEditionId)).Returns(_edition);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(_author.Path)).Returns(new RootFolder());
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns((List<ImportDecision<LocalBook>> decisions, bool replace, DownloadClientItem client, ImportMode mode) =>
                {
                    _imported.AddRange(decisions.Select(d => d.Item));
                    return decisions.Select(d => new ImportResult(d)).ToList();
                });
        }

        [Test]
        public void should_show_detected_group_without_marking_it_as_an_override()
        {
            var item = Subject.GetMediaFiles(_path, null, _author, FilterFilesType.None, true).Single();

            item.ReleaseGroup.Should().Be("Detected");
            item.ReleaseGroupOverride.Should().BeNull();
        }

        [Test]
        public void should_preserve_scoped_pack_resolution_through_manual_preview_reprocessing_and_commit()
        {
            const string recordingId = "audible:us:B111111111";
            _book.ForeignBookId = recordingId;
            _edition.ForeignEditionId = recordingId;
            _edition.Asin = "B111111111";
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionByForeignEditionId(recordingId)).Returns(_edition);
            Mocker.GetMock<IMetadataTagService>().Setup(service => service.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo { Asin = _edition.Asin, ReleaseGroup = "Preferred" });

            var tracked = new TrackedDownload
            {
                DownloadClient = 7,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-pack", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 }, Title = "Mixed Pack M4B-UploaderCrew" },
                ImportItem = new DownloadClientItem { OutputPath = new OsPath(_path) }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("owned-pack", 7)).Returns(tracked);
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned-pack", 7))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        EventType = EntityHistoryEventType.Grabbed,
                        Data = new Dictionary<string, string>
                        {
                            ["DownloadClientId"] = "7",
                            ["ReleaseGroup"] = "UploaderCrew",
                            [ReleaseGroupPackContext.HistoryKey] = recordingId + "|audible:us:B222222222"
                        }
                    }
                });

            var preview = Subject.GetMediaFiles(_path, "owned-pack", _author, FilterFilesType.None, true, 7).Single();
            preview.ReleaseGroup.Should().Be("Preferred");
            preview.ReleaseGroupOverride.Should().BeNull();
            preview.ReleaseGroupEvidence.PackUploaderGroup.Should().Be("UploaderCrew");
            preview.CustomFormats.Should().ContainSingle(format => format.Id == _preferred.Id);

            var reviewed = Subject.UpdateItems(new List<ManualImportItem> { preview }).Single();
            reviewed.ReleaseGroup.Should().Be(preview.ReleaseGroup);
            reviewed.ReleaseGroupEvidence.Source.Should().Be("recordingFile");
            var command = Command(null);
            command.Files.Single().DownloadId = "owned-pack";
            command.Files.Single().DownloadClientId = 7;
            Subject.Execute(command);
            _imported.Single().ReleaseGroup.Should().Be(reviewed.ReleaseGroup);
            _imported.Single().ReleaseGroupEvidence.RecordingId.Should().Be(recordingId);
            _imported.Single().ReleaseGroupEvidence.PackUploaderGroup.Should().Be("UploaderCrew");
        }

        [TestCase(" Preferred ", "Preferred", 1)]
        [TestCase("", "", 0)]
        [TestCase("   ", "", 0)]
        [TestCase(null, "Detected", 0)]
        public void should_recalculate_preview_using_the_deliberate_choice(string choice, string expected, int formatCount)
        {
            var item = Subject.GetMediaFiles(_path, null, _author, FilterFilesType.None, true).Single();
            item.ReleaseGroupOverride = choice;
            var updated = Subject.UpdateItems(new List<ManualImportItem> { item }).Single();

            updated.ReleaseGroup.Should().Be(expected);
            updated.ReleaseGroupOverride.Should().Be(choice?.Trim());
            updated.CustomFormats.Should().HaveCount(formatCount);
            _author.QualityProfile.Value.CalculateCustomFormatScore(updated.CustomFormats).Should().Be(formatCount * 100);
        }

        [TestCase(null, "Preferred", 1)]
        [TestCase("", "", 0)]
        [TestCase("OtherCrew", "OtherCrew", 0)]
        public void should_preview_saved_provenance_unless_the_user_deliberately_changes_it(string choice, string expected, int formatCount)
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFileWithPath(_path))
                .Returns(new BookFile { Id = 42, Path = _path, ReleaseGroup = "Preferred" });

            var item = Subject.GetMediaFiles(_path, null, _author, FilterFilesType.None, true).Single();
            item.ReleaseGroup.Should().Be("Preferred");
            item.ReleaseGroupOverride.Should().BeNull();
            item.CustomFormats.Should().ContainSingle();
            item.ReleaseGroupOverride = choice;

            var result = Subject.UpdateItems(new List<ManualImportItem> { item }).Single();

            result.ReleaseGroup.Should().Be(expected);
            result.ReleaseGroupOverride.Should().Be(choice);
            result.CustomFormats.Should().HaveCount(formatCount);
        }

        [TestCase(" Preferred ", "Preferred")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        [TestCase(null, "Detected")]
        public void should_carry_the_final_command_choice_to_the_importer(string choice, string expected)
        {
            Subject.Execute(Command(choice));

            var imported = _imported.Should().ContainSingle().Subject;
            imported.ReleaseGroup.Should().Be(expected);
            imported.ReleaseGroupOverride.Should().Be(choice?.Trim());
            imported.Book.Should().BeSameAs(_book);
            imported.Edition.Should().BeSameAs(_edition);
        }

        [Test]
        public void should_keep_distinct_choices_for_multipart_files()
        {
            var command = Command("Preferred");
            var secondPath = @"C:\downloads\Author.Book.Part2.m4b".AsOsAgnostic();
            FileSystem.AddFile(secondPath, new MockFileData("audio part two"));
            command.Files.Add(new ManualImportFile
            {
                Path = secondPath,
                AuthorId = _author.Id,
                BookId = _book.Id,
                ForeignEditionId = _edition.ForeignEditionId,
                Quality = new QualityModel(Quality.M4B),
                ReleaseGroup = string.Empty
            });

            Subject.Execute(command);

            _imported.Select(f => f.ReleaseGroup).Should().Equal("Preferred", string.Empty);
            _imported.Should().OnlyContain(f => f.PartCount == 2 && f.Book == _book && f.Edition == _edition);
        }

        [TestCase(null, "DownloadCrew")]
        [TestCase("Preferred", "Preferred")]
        [TestCase("", "")]
        public void should_preserve_choices_for_tracked_downloads(string choice, string expected)
        {
            var tracked = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { DownloadId = "download-id", Title = "Author.Book.M4B-DownloadCrew" },
                RemoteBook = new RemoteBook { ParsedBookInfo = new ParsedBookInfo { ReleaseGroup = "DownloadCrew" } }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("download-id")).Returns(tracked);
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), tracked.DownloadItem, It.IsAny<ImportMode>()))
                .Returns((List<ImportDecision<LocalBook>> decisions, bool replace, DownloadClientItem client, ImportMode mode) =>
                {
                    _imported.AddRange(decisions.Select(d => d.Item));
                    return new List<ImportResult>();
                });
            var command = Command(choice);
            command.Files.Single().DownloadId = "download-id";

            Subject.Execute(command);

            var imported = _imported.Should().ContainSingle().Subject;
            imported.ReleaseGroup.Should().Be(expected);
            imported.ReleaseGroupOverride.Should().Be(choice);
            tracked.State.Should().NotBe(TrackedDownloadState.Imported);
        }

        [TestCase(ImportMode.Auto, false)]
        [TestCase(ImportMode.Move, false)]
        [TestCase(ImportMode.Copy, true)]
        public void should_delegate_tracked_completion_and_preserve_copy_choice_and_sources(ImportMode mode, bool keepSources)
        {
            var output = System.IO.Path.GetDirectoryName(_path);
            var tracked = new TrackedDownload
            {
                DownloadClient = 7,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-job", OutputPath = new OsPath(output), CanMoveFiles = true },
                ImportItem = new DownloadClientItem { OutputPath = new OsPath(output) },
                RemoteBook = new RemoteBook { Books = new List<Book> { _book, new Book { Id = 99 } } }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("owned-job")).Returns(tracked);
            Mocker.GetMock<IDownloadedBooksImportService>().Setup(service => service.ShouldDeleteFolder(It.IsAny<IDirectoryInfo>())).Returns(true);
            var command = Command(null);
            command.ImportMode = mode;
            command.Files.Single().DownloadId = "owned-job";
            var secondPath = System.IO.Path.Combine(output, "Book.Part2.m4b");
            FileSystem.AddFile(secondPath, new MockFileData("owned second part"));
            command.Files.Add(new ManualImportFile
            {
                Path = secondPath,
                AuthorId = _author.Id,
                BookId = _book.Id,
                ForeignEditionId = _edition.ForeignEditionId,
                Quality = new QualityModel(Quality.M4B),
                DownloadId = "owned-job"
            });

            Subject.Execute(command);

            Mocker.GetMock<ICompletedDownloadService>().Verify(
                service => service.VerifyImport(tracked, It.Is<List<ImportResult>>(items => items.Count == 2 && items.All(item => item.ImportDecision.Item.Book.Id == _book.Id))),
                Times.Once());
            tracked.KeepSourceFiles.Should().Be(keepSources);
            tracked.State.Should().NotBe(TrackedDownloadState.Imported);
            FileSystem.File.ReadAllText(_path).Should().Be("audio");
            FileSystem.File.ReadAllText(secondPath).Should().Be("owned second part");
            Mocker.GetMock<IDownloadedBooksImportService>().Verify(service => service.ShouldDeleteFolder(It.IsAny<IDirectoryInfo>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
        }

        [Test]
        public void should_keep_separate_download_provenance_when_the_same_book_is_selected_from_two_downloads()
        {
            var secondPath = @"C:\other-download\Author.Book.m4b".AsOsAgnostic();
            FileSystem.AddFile(secondPath, new MockFileData("another owned source"));
            var first = new TrackedDownload { DownloadItem = new DownloadClientItem { DownloadId = "first-job" } };
            var second = new TrackedDownload { DownloadItem = new DownloadClientItem { DownloadId = "second-job" } };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("first-job")).Returns(first);
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("second-job")).Returns(second);
            var command = Command(null);
            command.Files.Single().DownloadId = "first-job";
            command.Files.Add(new ManualImportFile
            {
                Path = secondPath,
                AuthorId = _author.Id,
                BookId = _book.Id,
                ForeignEditionId = _edition.ForeignEditionId,
                Quality = new QualityModel(Quality.M4B),
                DownloadId = "second-job"
            });

            Subject.Execute(command);

            Mocker.GetMock<IImportApprovedBooks>().Verify(
                service => service.Import(It.Is<List<ImportDecision<LocalBook>>>(items => items.Count == 1 && items[0].Item.Path == _path), false, first.DownloadItem, ImportMode.Copy),
                Times.Once());
            Mocker.GetMock<IImportApprovedBooks>().Verify(
                service => service.Import(It.Is<List<ImportDecision<LocalBook>>>(items => items.Count == 1 && items[0].Item.Path == secondPath), false, second.DownloadItem, ImportMode.Copy),
                Times.Once());
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.VerifyImport(first, It.IsAny<List<ImportResult>>()), Times.Once());
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.VerifyImport(second, It.IsAny<List<ImportResult>>()), Times.Once());
        }

        [Test]
        public void same_download_id_on_two_clients_keeps_book_groups_and_completion_separate()
        {
            var secondPath = @"C:\other-download\Author.Book.m4b".AsOsAgnostic();
            FileSystem.AddFile(secondPath, new MockFileData("another owned source"));
            var first = new TrackedDownload { DownloadClient = 1, DownloadItem = new DownloadClientItem { DownloadId = "same-hash" } };
            var second = new TrackedDownload { DownloadClient = 2, DownloadItem = new DownloadClientItem { DownloadId = "same-hash" } };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("same-hash", 1)).Returns(first);
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("same-hash", 2)).Returns(second);
            var command = Command(null);
            command.Files.Single().DownloadId = "same-hash";
            command.Files.Single().DownloadClientId = 1;
            command.Files.Add(new ManualImportFile
            {
                Path = secondPath, AuthorId = _author.Id, BookId = _book.Id,
                ForeignEditionId = _edition.ForeignEditionId, Quality = new QualityModel(Quality.M4B),
                DownloadId = "same-hash", DownloadClientId = 2
            });

            Subject.Execute(command);

            Mocker.GetMock<IImportApprovedBooks>().Verify(service => service.Import(
                It.Is<List<ImportDecision<LocalBook>>>(items => items.Count == 1 && items[0].Item.Path == _path), false, first.DownloadItem, ImportMode.Copy), Times.Once());
            Mocker.GetMock<IImportApprovedBooks>().Verify(service => service.Import(
                It.Is<List<ImportDecision<LocalBook>>>(items => items.Count == 1 && items[0].Item.Path == secondPath), false, second.DownloadItem, ImportMode.Copy), Times.Once());
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.VerifyImport(first, It.Is<List<ImportResult>>(items => items.Count == 1)), Times.Once());
            Mocker.GetMock<ICompletedDownloadService>().Verify(service => service.VerifyImport(second, It.Is<List<ImportResult>>(items => items.Count == 1)), Times.Once());
            Mocker.GetMock<ITrackedDownloadService>().Verify(service => service.Find("same-hash"), Times.Never());
        }

        [Test]
        public void an_ambiguous_later_selection_aborts_before_retention_or_importing_an_earlier_selection()
        {
            var command = Command(null);
            command.Files.Single().DownloadId = "known-job";
            command.Files.Single().DownloadClientId = 1;
            command.Files.Add(new ManualImportFile { DownloadId = "ambiguous-job" });
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("known-job", 1))
                .Returns(new TrackedDownload { DownloadClient = 1 });
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("ambiguous-job"))
                .Throws(new AmbiguousTrackedDownloadException("ambiguous-job"));

            Assert.Throws<AmbiguousTrackedDownloadException>(() => Subject.Execute(command));

            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordSourceRetention(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IImportApprovedBooks>().Verify(service => service.Import(
                It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
        }

        [Test]
        public void manual_preview_returns_the_requested_client_identity()
        {
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("same-hash", 2)).Returns(new TrackedDownload
            {
                DownloadClient = 2,
                DownloadItem = new DownloadClientItem { DownloadId = "same-hash", OutputPath = new OsPath(_path) }
            });

            var item = Subject.GetMediaFiles(null, "same-hash", _author, FilterFilesType.None, true, 2).Single();

            item.DownloadId.Should().Be("same-hash");
            item.DownloadClientId.Should().Be(2);
            Mocker.GetMock<ITrackedDownloadService>().Verify(service => service.Find("same-hash"), Times.Never());
        }

        [TestCase(0, "owned-job")]
        [TestCase(-1, "owned-job")]
        [TestCase(1, null)]
        [TestCase(1, "")]
        public void orphan_or_nonpositive_client_scope_is_rejected_before_preview_or_import(int clientId, string downloadId)
        {
            Assert.Throws<ArgumentException>(() => Subject.GetMediaFiles(_path, downloadId, _author, FilterFilesType.None, true, clientId));
            Assert.Throws<ArgumentException>(() => Subject.UpdateItems(new List<ManualImportItem>
            {
                new ManualImportItem { Path = _path, DownloadId = downloadId, DownloadClientId = clientId }
            }));
            var command = Command(null);
            command.Files.Single().DownloadId = downloadId;
            command.Files.Single().DownloadClientId = clientId;

            Assert.Throws<ArgumentException>(() => Subject.Execute(command));

            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordSourceRetention(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IImportApprovedBooks>().Verify(service => service.Import(
                It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
            Mocker.GetMock<IMakeImportDecision>().Verify(service => service.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Never());
        }

        [Test]
        public void a_subset_copy_should_record_its_mapped_retention_intent_before_importing_any_file()
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 7,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-job", OutputPath = new OsPath(@"Z:\remote\job") },
                ImportItem = new DownloadClientItem { OutputPath = new OsPath(@"C:\stale\old-job".AsOsAgnostic()) }
            };
            var previousImport = tracked.ImportItem;
            var mapped = new DownloadClientItem { OutputPath = new OsPath(System.IO.Path.GetDirectoryName(_path)) };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("owned-job")).Returns(tracked);
            Mocker.GetMock<IProvideImportItemService>().Setup(service => service.ProvideImportItem(tracked.DownloadItem, previousImport)).Returns(mapped);
            var recorded = false;
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.RecordSourceRetention(tracked))
                .Callback(() =>
                {
                    tracked.ImportItem.Should().BeSameAs(mapped);
                    _imported.Should().BeEmpty();
                    recorded = true;
                });
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(service => service.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), false, tracked.DownloadItem, ImportMode.Copy))
                .Returns((List<ImportDecision<LocalBook>> decisions, bool replace, DownloadClientItem client, ImportMode mode) =>
                {
                    recorded.Should().BeTrue();
                    return decisions.Select(decision => new ImportResult(decision)).ToList();
                });
            var command = Command(null);
            command.Files.Single().DownloadId = "owned-job";

            Subject.Execute(command);

            tracked.KeepSourceFiles.Should().BeTrue();
            tracked.State.Should().NotBe(TrackedDownloadState.Imported);
            Mocker.GetMock<IDownloadHistoryService>().Verify(service => service.RecordSourceRetention(tracked), Times.Once());
        }

        [Test]
        public void retention_persistence_failure_should_abort_a_manual_copy_before_any_import()
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 7,
                DownloadItem = new DownloadClientItem { DownloadId = "owned-job", OutputPath = new OsPath(System.IO.Path.GetDirectoryName(_path)) }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(service => service.Find("owned-job")).Returns(tracked);
            Mocker.GetMock<IDownloadHistoryService>().Setup(service => service.RecordSourceRetention(tracked)).Throws<System.IO.IOException>();
            var command = Command(null);
            command.Files.Single().DownloadId = "owned-job";

            Assert.Throws<System.IO.IOException>(() => Subject.Execute(command));

            Mocker.GetMock<IImportApprovedBooks>().Verify(service => service.Import(
                It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()), Times.Never());
        }

        [Test]
        public void a_custom_format_rejection_is_visible_before_an_explicit_manual_replacement()
        {
            _author.QualityProfile.Value.Items = new List<QualityProfileQualityItem>
            {
                new QualityProfileQualityItem { Quality = Quality.M4B, Allowed = true }
            };
            _book.BookFiles = new List<BookFile>
            {
                new BookFile
                {
                    Id = 42, EditionId = _edition.Id, Author = _author,
                    Path = @"C:\library\Author\Existing.m4b".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.M4B), ReleaseGroup = "Preferred"
                }
            };
            Mocker.GetMock<ILocalBookCustomFormatCalculationService>()
                .Setup(service => service.Calculate(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()))
                .Returns(new LocalBookCustomFormatScores
                {
                    OriginalFormats = new List<CustomFormat>(), IntendedFormats = new List<CustomFormat>()
                });
            var specifications = new[]
            {
                Mocker.Resolve<NzbDrone.Core.MediaFiles.BookImport.Specifications.UpgradeSpecification>()
            };
            Mocker.SetConstant<IMakeImportDecision>(new ImportDecisionMaker(
                specifications,
                Array.Empty<IImportDecisionEngineSpecification<LocalEdition>>(),
                Mocker.GetMock<IMediaFileService>().Object,
                Mocker.GetMock<IMetadataTagService>().Object,
                Mocker.GetMock<IAugmentingService>().Object,
                Mocker.GetMock<IIdentificationService>().Object,
                Mocker.GetMock<IRootFolderService>().Object,
                Mocker.GetMock<IQualityProfileService>().Object,
                Mocker.GetMock<IHistoryService>().Object,
                TestLogger));

            var preview = Subject.GetMediaFiles(_path, null, _author, FilterFilesType.None, true).Single();

            preview.Rejections.Should().ContainSingle().Which.Code.Should().Be(RejectionCode.CustomFormatDowngrade);
            var command = Command(null);
            command.ReplaceExistingFiles = true;
            Subject.Execute(command);

            Mocker.GetMock<IImportApprovedBooks>().Verify(service => service.Import(
                It.Is<List<ImportDecision<LocalBook>>>(items => items.All(item => item.Approved)), true, null, ImportMode.Copy), Times.Once());
        }

        private ManualImportCommand Command(string choice)
        {
            return new ManualImportCommand
            {
                ImportMode = ImportMode.Copy,
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = _path,
                        AuthorId = _author.Id,
                        BookId = _book.Id,
                        ForeignEditionId = _edition.ForeignEditionId,
                        Quality = new QualityModel(Quality.M4B),
                        ReleaseGroup = choice
                    }
                }
            };
        }
    }
}
