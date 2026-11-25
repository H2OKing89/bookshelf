using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Aggregation
{
    [TestFixture]
    public class ReleaseGroupResolutionFixture : CoreTest
    {
        private const string FirstId = "audible:us:B111111111";
        private const string SecondId = "audible:us:B222222222";
        private const string PackIds = FirstId + "|" + SecondId;

        private DownloadClientItem Download(int client = 7)
        {
            return new DownloadClientItem { DownloadId = "owned-pack", DownloadClientInfo = new DownloadClientItemClientInfo { Id = client } };
        }

        private ReleaseGroupPackContext Pack(string ids = PackIds, string uploader = "UploaderCrew")
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(history => history.FindByDownloadId("owned-pack", 7))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        EventType = EntityHistoryEventType.Grabbed,
                        Data = new Dictionary<string, string> { [ReleaseGroupPackContext.HistoryKey] = ids, ["ReleaseGroup"] = uploader, ["DownloadClientId"] = "7" }
                    }
                });
            return ReleaseGroupPackContext.Read(Mocker.GetMock<IHistoryService>().Object, Download());
        }

        private LocalBook File(string folder = "Mixed Pack", string filename = "Recording [B111111111].m4b", string group = null, string id = FirstId, string asin = "B111111111")
        {
            var book = new Book { Id = 101, ForeignBookId = id };
            return new LocalBook
            {
                Path = $@"C:\downloads\{folder}\{filename}".AsOsAgnostic(),
                DownloadClientBookInfo = new ParsedBookInfo { ReleaseGroup = "UploaderCrew" },
                FolderTrackInfo = new ParsedBookInfo { ReleaseGroup = "OuterFolderCrew" },
                FileTrackInfo = new ParsedTrackInfo { Asin = asin, ReleaseGroup = group },
                Book = book,
                Edition = new Edition { Id = 201, ForeignEditionId = id, Asin = asin, Book = book }
            };
        }

        [Test]
        public void should_only_capture_multiple_independent_qualified_recordings()
        {
            var remote = new RemoteBook { ParsedBookInfo = new ParsedBookInfo { Discography = true } };
            remote.Books.Add(new Book { Id = 1, ForeignBookId = FirstId });
            ReleaseGroupPackContext.Capture(remote).Should().BeNull("a publisher omnibus is one recording even when a title contains several works");
            remote.Books.Add(new Book { Id = 2, ForeignBookId = "audible:uk:B111111111" });
            ReleaseGroupPackContext.Capture(remote).Should().BeNull("the file's bare ASIN would be marketplace ambiguous");
            remote.Books[1].ForeignBookId = SecondId;
            ReleaseGroupPackContext.Capture(remote).Should().Be(PackIds);
            remote.Books.Add(new Book { Id = 3, ForeignBookId = "legacy-unqualified" });
            ReleaseGroupPackContext.Capture(remote).Should().BeNull();
        }

        [Test]
        public void should_require_scoped_and_current_grab_evidence()
        {
            Pack().Should().NotBeNull();
            ReleaseGroupPackContext.Read(Mocker.GetMock<IHistoryService>().Object, Download(8)).Should().BeNull();
            ReleaseGroupPackContext.Read(Mocker.GetMock<IHistoryService>().Object, Download(0)).Should().BeNull();
            Mocker.GetMock<IHistoryService>().Verify(history => history.FindByDownloadId(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IHistoryService>().Setup(history => history.FindByDownloadId("owned-pack", 7))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory { EventType = EntityHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddMinutes(-1), Data = new Dictionary<string, string> { [ReleaseGroupPackContext.HistoryKey] = PackIds } },
                    new EntityHistory { EventType = EntityHistoryEventType.Grabbed, Date = DateTime.UtcNow }
                });
            ReleaseGroupPackContext.Read(Mocker.GetMock<IHistoryService>().Object, Download()).Should().BeNull("an older acquisition cannot define the current one");
        }

        [Test]
        public void should_resolve_mixed_group_pack_by_exact_recording_file()
        {
            var first = File(group: "RecordingOneCrew");
            var second = File(filename: "Other [B222222222].m4b", group: "RecordingTwoCrew", id: SecondId, asin: "B222222222");
            var pack = Pack();
            ReleaseGroupResolution.Apply(first, pack);
            ReleaseGroupResolution.Apply(second, pack);
            first.ReleaseGroup.Should().Be("RecordingOneCrew");
            second.ReleaseGroup.Should().Be("RecordingTwoCrew");
            first.ReleaseGroupEvidence.Source.Should().Be("recordingFile");
            first.ReleaseGroupEvidence.PackUploaderGroup.Should().Be("UploaderCrew");
            first.ReleaseGroupEvidence.PackRecordingIds.Should().Equal(FirstId, SecondId);
            first.ReleaseGroupEvidence.RecordingId.Should().Be(FirstId);
            second.ReleaseGroupEvidence.RecordingId.Should().Be(SecondId);
        }

        [Test]
        public void should_keep_ordinary_multipart_behavior_without_independent_grab_evidence()
        {
            var partOne = File(group: "PartCrew");
            var partTwo = File(filename: "Part 2 [B111111111].m4b", group: "PartCrew");
            ReleaseGroupResolution.Apply(partOne);
            ReleaseGroupResolution.Apply(partTwo);
            partOne.ReleaseGroup.Should().Be("UploaderCrew");
            partTwo.ReleaseGroup.Should().Be("UploaderCrew");
            partOne.ReleaseGroupEvidence.PackRecordingIds.Should().BeNull();
        }

        [Test]
        public void should_use_only_an_identified_recording_folder_and_not_a_generic_outer_folder()
        {
            var part = File(folder: @"Mixed Pack\Recording [B111111111] M4B-RecordingCrew\CD 1", filename: "01.m4b");
            ReleaseGroupResolution.Apply(part, Pack());
            part.ReleaseGroup.Should().Be("RecordingCrew");
            part.ReleaseGroupEvidence.Source.Should().Be("recordingFolder");
            var unlabeled = File();
            ReleaseGroupResolution.Apply(unlabeled, Pack());
            unlabeled.ReleaseGroup.Should().Be("UploaderCrew");
            unlabeled.ReleaseGroupEvidence.Reason.Should().Be("noRecordingLabel");
        }

        [Test]
        public void should_explain_conflicting_recording_labels_without_guessing()
        {
            var file = File(folder: "Recording [B111111111] M4B-FolderCrew", group: "FileCrew");
            ReleaseGroupResolution.Apply(file, Pack());
            file.ReleaseGroup.Should().Be("UploaderCrew");
            file.ReleaseGroupEvidence.FileGroup.Should().Be("FileCrew");
            file.ReleaseGroupEvidence.RecordingFolderGroup.Should().Be("FolderCrew");
            file.ReleaseGroupEvidence.Reason.Should().Be("conflictingRecordingLabels");
        }

        [Test]
        public void should_not_promote_group_for_mismatched_or_conflicting_recording_identity()
        {
            var wrongEdition = File(group: "FileCrew", id: "audible:uk:B111111111");
            ReleaseGroupResolution.Apply(wrongEdition, Pack());
            wrongEdition.ReleaseGroup.Should().Be("UploaderCrew");
            var conflicting = File(folder: "Other [B222222222]", group: "FileCrew");
            ReleaseGroupResolution.Apply(conflicting, Pack());
            conflicting.ReleaseGroup.Should().Be("UploaderCrew");
        }

        [TestCase(" ManualCrew ")]
        [TestCase("")]
        public void should_preserve_manual_choice_and_raw_pack_evidence_in_the_committed_file(string group)
        {
            var file = File(group: "FileCrew");
            file.ReleaseGroupOverride = group;
            ReleaseGroupResolution.Apply(file, Pack());
            var saved = ImportBookFileMetadata.Create(file, null, Mocker.GetMock<IHistoryService>().Object);
            saved.ReleaseGroup.Should().Be(group.Trim());
            saved.ReleaseGroupEvidence.Source.Should().Be("manual");
            saved.ReleaseGroupEvidence.FileGroup.Should().Be("FileCrew");
            saved.ReleaseGroupEvidence.PackUploaderGroup.Should().Be("UploaderCrew");
        }
    }
}
