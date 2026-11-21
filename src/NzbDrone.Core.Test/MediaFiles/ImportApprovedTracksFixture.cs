using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedTracksFixture : CoreTest<ImportApprovedBooks>
    {
        private List<ImportDecision<LocalBook>> _rejectedDecisions;
        private List<ImportDecision<LocalBook>> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private DownloadClientItemClientInfo _clientInfo;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision<LocalBook>>();
            _approvedDecisions = new List<ImportDecision<LocalBook>>();

            var author = Builder<Author>.CreateNew()
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .With(s => s.Path = @"C:\Test\Music\Alien Ant Farm".AsOsAgnostic())
                                        .Build();

            var book = Builder<Book>.CreateNew()
                .With(e => e.Author = author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = book)
                .With(e => e.Monitored = true)
                .Build();

            book.Editions = new List<Edition> { edition };

            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.IsCalibreLibrary = false)
                .Build();

            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));

            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                                       new LocalBook
                                       {
                                           Author = author,
                                           Book = book,
                                           Edition = edition,
                                           Part = 1,
                                           Path = Path.Combine(author.Path, "Alien Ant Farm - 01 - Pilot.mp3"),
                                           Quality = new QualityModel(Quality.MP3),
                                           FileTrackInfo = new ParsedTrackInfo
                                           {
                                               ReleaseGroup = "DRONE"
                                           }
                                       }));

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Returns(new BookFileMoveResult());

            _clientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();
            _downloadClientItem = Builder<DownloadClientItem>.CreateNew().With(x => x.DownloadClientInfo = _clientInfo).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(rootFolder);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(edition))
                .Returns(new List<Edition> { edition });
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<BookFile>()), Times.Never());
        }

        [TestCase("B0BXV7C8PX", "B0HLLTHK38")]
        [TestCase("B0CCFTQW7R", "B0HLLTHK38")]
        [TestCase("B0F14RFHS6", "B0F14RPXHR")]
        public void should_reject_explicit_recording_mismatch_before_import_side_effects(string fileAsin, string editionAsin)
        {
            var decision = _approvedDecisions.Single();
            decision.Item.Path = Path.Combine(Path.GetTempPath(), $"Book [{fileAsin}].m4b");
            decision.Item.Edition.Asin = editionAsin;
            decision.Item.Edition.ForeignEditionId = $"audible:us:{editionAsin}";

            var result = Subject.Import(_approvedDecisions, false);

            result.Should().ContainSingle().Which.Result.Should().Be(ImportResultType.Rejected);
            decision.Rejections.Should().ContainSingle().Which.Reason.Should().Contain("ASIN");
            Mocker.GetMock<IEditionService>().Verify(x => x.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMetadataTagService>().Verify(x => x.WriteTags(It.IsAny<BookFile>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            Subject.Import(_approvedDecisions, false).Should().HaveCount(1);
        }

        [Test]
        public void should_only_import_approved()
        {
            var all = new List<ImportDecision<LocalBook>>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_track_once()
        {
            var all = new List<ImportDecision<LocalBook>>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision<LocalBook>(_approvedDecisions.First().Item));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_TrackImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_move_existing_files()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false),
                          Times.Never());
        }

        [Test]
        public void should_import_higher_quality_files_first()
        {
            var lqDecision = _approvedDecisions.First();
            lqDecision.Item.Quality = new QualityModel(Quality.MOBI);
            lqDecision.Item.Size = 10.Megabytes();

            var hqDecision = new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = lqDecision.Item.Author,
                    Book = lqDecision.Item.Book,
                    Edition = lqDecision.Item.Edition,
                    Part = 1,
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.AZW3),
                    Size = 1.Megabytes(),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        ReleaseGroup = "DRONE"
                    }
                });

            var all = new List<ImportDecision<LocalBook>>();
            all.Add(lqDecision);
            all.Add(hqDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == hqDecision.Item.Size);
        }

        [Test]
        public void should_import_larger_files_for_same_quality_first()
        {
            var fileDecision = _approvedDecisions.First();
            fileDecision.Item.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = fileDecision.Item.Author,
                    Book = fileDecision.Item.Book,
                    Edition = fileDecision.Item.Edition,
                    Part = 1,
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.MP3),
                    Size = 80.Megabytes()
                });

            var all = new List<ImportDecision<LocalBook>>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == fileDecision.Item.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo });

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo }, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false), Times.Once());
        }

        [TestCase(ImportMode.Auto, true)]
        [TestCase(ImportMode.Auto, false)]
        [TestCase(ImportMode.Move, true)]
        [TestCase(ImportMode.Move, false)]
        [TestCase(ImportMode.Copy, true)]
        [TestCase(ImportMode.Copy, false)]
        public void tracked_imports_should_keep_sources_until_download_completion(ImportMode mode, bool canMove)
        {
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId("owned-tracked-import", _clientInfo.Id))
                .Returns(new List<EntityHistory>());
            var client = new DownloadClientItem
            {
                DownloadId = "owned-tracked-import",
                Title = "Alien.Ant.Farm-Truant",
                CanMoveFiles = canMove,
                DownloadClientInfo = _clientInfo
            };

            Subject.Import(_approvedDecisions, true, client, mode);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(
                service => service.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, true),
                Times.Once());
        }

        [Test]
        public void successful_import_should_return_the_exact_committed_file_before_history_is_published()
        {
            BookFile committed = null;
            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.ImportFiles(It.IsAny<List<BookFile>>(), It.IsAny<List<BookFile>>(), It.IsAny<List<BookFile>>()))
                .Callback<List<BookFile>, List<BookFile>, List<BookFile>>((added, updated, replaced) =>
                {
                    committed = added.Single();
                    committed.Id = 901;
                });

            var results = Subject.Import(_approvedDecisions, true);

            results.Should().ContainSingle().Which.ImportedFile.Should().BeSameAs(committed);
            committed.Id.Should().Be(901);
            committed.EditionId.Should().Be(_approvedDecisions.Single().Item.Edition.Id);
            committed.Part.Should().Be(_approvedDecisions.Single().Item.Part);
        }

        private ImportDecision<LocalBook> AudioPart(string name, int disc, int part, long size = 100)
        {
            var original = _approvedDecisions.First().Item;
            return new ImportDecision<LocalBook>(new LocalBook
            {
                Author = original.Author,
                Book = original.Book,
                Edition = original.Edition,
                ExistingFile = true,
                Path = Path.Combine(original.Author.Path, name + ".mp3"),
                Part = part,
                Size = size,
                Quality = original.Quality,
                FileTrackInfo = new ParsedTrackInfo { DiscNumber = disc, TrackNumbers = new[] { part } }
            });
        }

        [Test]
        public void should_import_disc_track_resets_as_distinct_ordered_parts()
        {
            var decisions = new List<ImportDecision<LocalBook>>
            {
                AudioPart("Disc B track 2", 2, 2, 400),
                AudioPart("Disc A track 2", 1, 2, 300),
                AudioPart("Disc B track 1", 2, 1, 200),
                AudioPart("Disc A track 1", 1, 1, 100)
            };
            List<BookFile> imported = null;
            Mocker.GetMock<IMediaFileService>().Setup(service => service.AddMany(It.IsAny<List<BookFile>>()))
                .Callback<List<BookFile>>(files => imported = files);

            Subject.Import(decisions, false).Should().OnlyContain(result => result.Result == ImportResultType.Imported);

            imported.OrderBy(file => file.Part).Select(file => Path.GetFileName(file.Path)).Should().Equal(
                "Disc A track 1.mp3", "Disc A track 2.mp3", "Disc B track 1.mp3", "Disc B track 2.mp3");
            imported.Select(file => file.Part).Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
            imported.Should().OnlyContain(file => file.PartCount == 4);
        }

        [Test]
        public void should_keep_alternative_files_for_one_disc_track_in_quality_order()
        {
            var decisions = new List<ImportDecision<LocalBook>>
            {
                AudioPart("Disc 1 smaller", 1, 1, 100),
                AudioPart("Disc 2", 2, 1, 200),
                AudioPart("Disc 1 larger", 1, 1, 300)
            };

            var results = Subject.Import(decisions, false);

            results.Count(result => result.Result == ImportResultType.Imported).Should().Be(2);
            results.Where(result => result.Result == ImportResultType.Imported).Select(result => result.ImportDecision.Item.Path)
                .Should().BeEquivalentTo(new[] { decisions[1].Item.Path, decisions[2].Item.Path });
            decisions[0].Item.Part.Should().Be(decisions[2].Item.Part);
            decisions[1].Item.Part.Should().Be(2);
        }

        [Test]
        public void should_preserve_existing_single_disc_and_global_track_numbers()
        {
            var decisions = new List<ImportDecision<LocalBook>>
            {
                AudioPart("Disc 1 track 3", 1, 3),
                AudioPart("Disc 1 track 7", 1, 7),
                AudioPart("Disc 2 track 8", 2, 8)
            };

            Subject.Import(decisions, false).Should().OnlyContain(result => result.Result == ImportResultType.Imported);

            decisions.Select(decision => decision.Item.Part).Should().Equal(3, 7, 8);
        }

        [Test]
        public void should_number_untagged_parts_in_natural_filename_order()
        {
            var decisions = new List<ImportDecision<LocalBook>> { AudioPart("Track 10", 0, 0), AudioPart("Track 2", 0, 0) };

            Subject.Import(decisions, false).Should().OnlyContain(result => result.Result == ImportResultType.Imported);

            decisions.Select(decision => decision.Item.Part).Should().Equal(2, 1);
        }

        [Test]
        public void should_respect_disc_order_for_untagged_track_numbers()
        {
            var decisions = new List<ImportDecision<LocalBook>> { AudioPart("A second disc", 2, 0), AudioPart("Z first disc", 1, 0) };

            Subject.Import(decisions, false).Should().OnlyContain(result => result.Result == ImportResultType.Imported);

            decisions.Select(decision => decision.Item.Part).Should().Equal(2, 1);
        }

        [TestCase(null, "Original release group")]
        [TestCase("Preferred", "Preferred")]
        [TestCase("", "")]
        public void should_update_same_path_recording_link_without_replacing_file_identity_or_provenance(string groupOverride, string expectedGroup)
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.Size = 123456;
            track.Item.Modified = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
            track.Item.ReleaseGroup = "Replacement guess";
            track.Item.ReleaseGroupOverride = groupOverride;
            track.Item.FileTrackInfo.MediaInfo = new MediaInfoModel { AudioFormat = "AAC", AudioBitrate = 128 };
            var previous = new BookFile
            {
                Id = 42,
                Path = track.Item.Path,
                EditionId = 999,
                DateAdded = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                OriginalFilePath = "/downloads/original.mp3",
                SceneName = "Original scene name",
                ReleaseGroup = "Original release group",
                IndexerFlags = IndexerFlags.Freeleech,
                CalibreId = 77,
                LastRemoteSearchTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(track.Item.Path)).Returns(previous);
            List<BookFile> updated = null;
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Update(It.IsAny<List<BookFile>>()))
                .Callback<List<BookFile>>(files => updated = files);

            Subject.Import(_approvedDecisions, false).Should().ContainSingle(result => result.Result == ImportResultType.Imported);

            var result = updated.Should().ContainSingle().Which;
            result.Id.Should().Be(previous.Id);
            result.DateAdded.Should().Be(previous.DateAdded);
            result.OriginalFilePath.Should().Be(previous.OriginalFilePath);
            result.SceneName.Should().Be(previous.SceneName);
            result.ReleaseGroup.Should().Be(expectedGroup);
            result.IndexerFlags.Should().Be(previous.IndexerFlags);
            result.CalibreId.Should().Be(previous.CalibreId);
            result.LastRemoteSearchTime.Should().Be(previous.LastRemoteSearchTime);
            result.EditionId.Should().Be(track.Item.Edition.Id);
            result.Size.Should().Be(track.Item.Size);
            result.Modified.Should().Be(track.Item.Modified);
            result.MediaInfo.Should().BeSameAs(track.Item.FileTrackInfo.MediaInfo);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.IsAny<List<BookFile>>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.Is<TrackImportedEvent>(e => e.ImportedBook.Id == 42 && !e.NewDownload)), Times.Once());
        }

        [Test]
        public void should_retain_measured_streams_when_remapping_an_existing_path_with_a_partial_probe()
        {
            var track = _approvedDecisions.First().Item;
            track.ExistingFile = true;
            track.Size = 123456;
            track.Modified = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
            var target = new MediaAnalysisStamp { Size = track.Size, ModifiedUtc = track.Modified };
            track.FileTrackInfo.MediaInfo = new MediaInfoModel
            {
                Source = "TagLib",
                AudioBitrate = 64000,
                Analysis = target,
                LastAnalysisAttempt = new MediaAnalysisAttempt { Target = target, AttemptedAtUtc = track.Modified }
            };
            var previous = new BookFile
            {
                Id = 42,
                Path = track.Path,
                EditionId = 999,
                Size = track.Size,
                Modified = track.Modified,
                MediaInfo = new MediaInfoModel { Source = "MediaInfo", AudioFormat = "AAC", AudioBitrate = 128000 }
            };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(track.Path)).Returns(previous);
            List<BookFile> updated = null;
            Mocker.GetMock<IMediaFileService>().Setup(service => service.Update(It.IsAny<List<BookFile>>()))
                .Callback<List<BookFile>>(files => updated = files);

            Subject.Import(_approvedDecisions, false).Should().ContainSingle(result => result.Result == ImportResultType.Imported);

            var saved = updated.Should().ContainSingle().Which;
            saved.EditionId.Should().Be(track.Edition.Id);
            saved.MediaInfo.AudioBitrate.Should().Be(128000);
            saved.MediaInfo.LastAnalysisAttempt.Target.Should().BeSameAs(target);
        }

        [Test]
        public void should_leave_previous_file_row_intact_when_same_path_tag_update_fails()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            var previous = new BookFile { Id = 42, Path = track.Item.Path, EditionId = 999 };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(track.Item.Path)).Returns(previous);
            Mocker.GetMock<IMetadataTagService>().Setup(service => service.WriteTags(It.IsAny<BookFile>(), false, false))
                .Throws(new IOException("Simulated tag update failure"));

            Subject.Import(_approvedDecisions, false).Should().OnlyContain(result => result.Result != ImportResultType.Imported);

            previous.EditionId.Should().Be(999);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Update(It.IsAny<List<BookFile>>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.IsAny<List<BookFile>>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        private LocalBook NewDownloadPart(int part, string filename)
        {
            var template = _approvedDecisions.First().Item;
            return new LocalBook
            {
                Author = template.Author,
                Book = template.Book,
                Edition = template.Edition,
                Part = part,
                Path = Path.Combine(template.Author.Path, filename),
                Quality = template.Quality,
                FileTrackInfo = new ParsedTrackInfo(),
                ExistingFile = false
            };
        }

        [Test]
        public void should_abort_prepared_parts_when_another_part_fails()
        {
            var first = NewDownloadPart(1, "part1.mp3");
            var second = NewDownloadPart(2, "part2.mp3");
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(x => x.UpgradeBookFile(It.IsAny<BookFile>(), first, false))
                .Returns<BookFile, LocalBook, bool>((file, _, _) => new BookFileMoveResult { BookFile = file });
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(x => x.UpgradeBookFile(It.IsAny<BookFile>(), second, false))
                .Throws(new IOException("tag write failed"));

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { new (first), new (second) }, true);

            results.Should().OnlyContain(result => result.Result != ImportResultType.Imported);
            results.Should().OnlyContain(result => result.ImportedFile == null);
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.CommitUpgrade(It.IsAny<List<BookFileMoveResult>>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.AbortUpgrade(It.Is<List<BookFileMoveResult>>(items => items.Count == 1)), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.AddMany(It.IsAny<List<BookFile>>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(x => x.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_allow_alternative_candidate_after_first_candidate_fails()
        {
            var first = NewDownloadPart(1, "first.mp3");
            first.Size = 200;
            var alternative = NewDownloadPart(1, "alternative.mp3");
            alternative.Size = 100;
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(x => x.UpgradeBookFile(It.IsAny<BookFile>(), first, false))
                .Throws(new IOException("copy failed"));
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(x => x.UpgradeBookFile(It.IsAny<BookFile>(), alternative, false))
                .Returns<BookFile, LocalBook, bool>((file, _, _) => new BookFileMoveResult { BookFile = file });

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { new (first), new (alternative) }, true);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Imported);
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.CommitUpgrade(It.Is<List<BookFileMoveResult>>(items => items.Count == 1)), Times.Once());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.AbortUpgrade(It.IsAny<List<BookFileMoveResult>>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_persist_or_publish_import_when_replacement_commit_fails()
        {
            Mocker.GetMock<IUpgradeMediaFiles>().Setup(x => x.CommitUpgrade(It.IsAny<List<BookFileMoveResult>>()))
                .Throws(new IOException("destination appeared"));

            var results = Subject.Import(_approvedDecisions, true);

            results.Should().OnlyContain(result => result.Result != ImportResultType.Imported);
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.AbortUpgrade(It.IsAny<List<BookFileMoveResult>>()), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.AddMany(It.IsAny<List<BookFile>>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(x => x.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_abort_published_replacements_if_database_transaction_fails()
        {
            Mocker.GetMock<IMediaFileService>().Setup(x => x.ImportFiles(It.IsAny<List<BookFile>>(), It.IsAny<List<BookFile>>(), It.IsAny<List<BookFile>>()))
                .Throws(new IOException("database save failed"));

            Assert.Throws<IOException>(() => Subject.Import(_approvedDecisions, true));

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.AbortUpgrade(It.IsAny<List<BookFileMoveResult>>()), Times.Once());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(x => x.FinalizeUpgrade(It.IsAny<List<BookFileMoveResult>>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(x => x.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
        }
    }
}
