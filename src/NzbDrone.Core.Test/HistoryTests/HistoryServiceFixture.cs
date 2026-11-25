using System;
using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;

namespace NzbDrone.Core.Test.HistoryTests
{
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private QualityProfile _profile;
        private QualityProfile _profileCustom;

        [SetUp]
        public void Setup()
        {
            _profile = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(),
            };

            _profileCustom = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(Quality.MP3),
            };
        }

        [Test]
        public void should_use_file_name_for_source_title_if_scene_name_is_null()
        {
            var author = Builder<Author>.CreateNew().Build();
            var trackFile = Builder<BookFile>.CreateNew()
                .With(f => f.SceneName = null)
                .With(f => f.Author = author)
                .Build();

            var localTrack = new LocalBook
            {
                Author = author,
                Book = new Book(),
                Path = @"C:\Test\Unsorted\Author.01.Hymn.mp3"
            };

            var downloadClientItem = new DownloadClientItem
            {
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = DownloadProtocol.Usenet,
                    Id = 1,
                    Name = "sab"
                },
                DownloadId = "abcd"
            };

            Subject.Handle(new TrackImportedEvent(localTrack, trackFile, new List<BookFile>(), true, downloadClientItem));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.SourceTitle == Path.GetFileNameWithoutExtension(localTrack.Path))));
        }

        [Test]
        public void committed_import_history_should_retain_recording_parts_client_and_source_timestamp()
        {
            var modified = new System.DateTime(2026, 11, 18, 1, 2, 3, System.DateTimeKind.Utc);
            var local = new LocalBook
            {
                Path = Path.Combine(TempFolder, "source.m4b"), Author = new Author { Id = 1 }, Book = new Book { Id = 2 },
                Part = 2, PartCount = 3, Size = 100, Modified = modified
            };
            var imported = new BookFile
            {
                Id = 4, EditionId = 5, Part = 2, Path = Path.Combine(TempFolder, "imported.m4b"),
                ReleaseGroup = "RecordingCrew",
                ReleaseGroupEvidence = new ReleaseGroupEvidence { FileGroup = "RecordingCrew", PackUploaderGroup = "UploaderCrew", ResolvedGroup = "RecordingCrew", Source = "recordingFile" },
                MediaInfo = new MediaInfoModel { Source = "MediaInfo", AudioFormat = "FLAC", AudioChannels = 1 }
            };
            EntityHistory saved = null;
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<EntityHistory>()))
                .Callback<EntityHistory>(history => saved = history);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<HistoryChangedEvent>()))
                .Callback(() => saved.Should().NotBeNull());
            var client = new DownloadClientItem { DownloadId = "owned-download", DownloadClientInfo = new DownloadClientItemClientInfo { Id = 6 } };

            Subject.Handle(new TrackImportedEvent(local, imported, new List<BookFile>(), true, client));

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<HistoryChangedEvent>()), Times.Once());
            saved.BookId.Should().Be(2);
            saved.Data["FileId"].Should().Be("4");
            saved.Data["EditionId"].Should().Be("5");
            saved.Data["Part"].Should().Be("2");
            saved.Data["PartCount"].Should().Be("3");
            saved.Data["DownloadClientId"].Should().Be("6");
            saved.Data["SourceModifiedUtc"].Should().Be(modified.ToString("O"));
            saved.Data["ReleaseGroup"].Should().Be("RecordingCrew");
            STJson.Deserialize<ReleaseGroupEvidence>(saved.Data["releaseGroupEvidence"]).PackUploaderGroup.Should().Be("UploaderCrew");
            var evidence = AudioFormatEvidence.FromHistory(saved.Data);
            evidence.Origin.Should().Be(AudioEvidenceOrigin.File);
            evidence.Codec.Should().Be(AudioCodecKind.Flac);
            evidence.Channels.Should().Be(1);
        }

        [Test]
        public void grab_history_should_save_language_and_advertised_audio_evidence()
        {
            var release = new RemoteBook
            {
                Release = new ReleaseInfo
                {
                    Languages = new List<Language> { Language.French, Language.English },
                    MediaInfo = new MediaInfoModel { Source = "MyAnonaMouse", AudioFormat = "USAC", AudioAdditionalFeatures = "xHE-AAC" }
                },
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel() },
                Books = new List<Book>
                {
                    new Book { Id = 2, Editions = new List<Edition> { new Edition { Language = "fra", Monitored = true } } }
                }
            };
            EntityHistory saved = null;
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<EntityHistory>()))
                .Callback<EntityHistory>(history => saved = history);

            Subject.Handle(new BookGrabbedEvent(release) { DownloadId = "owned", DownloadClientId = 4 });

            saved.Data[RecordingLanguageEvidence.LanguagesKey].Should().Be("2,1");
            saved.Data[RecordingLanguageEvidence.EditionLanguageKey].Should().Be("2");
            var evidence = AudioFormatEvidence.FromHistory(saved.Data);
            evidence.Origin.Should().Be(AudioEvidenceOrigin.Release);
            evidence.Codec.Should().Be(AudioCodecKind.XheAac);
        }

        [Test]
        public void grab_history_should_record_the_same_independent_recording_set_on_each_scoped_row()
        {
            var release = new RemoteBook
            {
                Release = new ReleaseInfo { Title = "Owned Mixed Pack" },
                ParsedBookInfo = new ParsedBookInfo { ReleaseGroup = "UploaderCrew", Quality = new QualityModel() },
                Books = new List<Book>
                {
                    new Book { Id = 1, ForeignBookId = "audible:us:B111111111", Editions = new List<Edition>() },
                    new Book { Id = 2, ForeignBookId = "audible:us:B222222222", Editions = new List<Edition>() }
                }
            };
            var saved = new List<EntityHistory>();
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.Insert(It.IsAny<EntityHistory>()))
                .Callback<EntityHistory>(history => saved.Add(history));

            Subject.Handle(new BookGrabbedEvent(release) { DownloadId = "owned-pack", DownloadClientId = 7 });

            saved.Should().HaveCount(2).And.OnlyContain(row => row.DownloadId == "owned-pack" &&
                row.Data["DownloadClientId"] == "7" && row.Data["ReleaseGroup"] == "UploaderCrew" &&
                row.Data[ReleaseGroupPackContext.HistoryKey] == "audible:us:B111111111|audible:us:B222222222");
        }

        [Test]
        public void should_publish_a_single_change_after_committed_bulk_update()
        {
            var items = new[] { new EntityHistory { Id = 1 }, new EntityHistory { Id = 2 } };
            var saved = false;
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.UpdateMany(items))
                .Callback(() => saved = true);
            Mocker.GetMock<IEventAggregator>().Setup(events => events.PublishEvent(It.IsAny<HistoryChangedEvent>()))
                .Callback(() => saved.Should().BeTrue());

            Subject.UpdateMany(items);
            Subject.UpdateMany(Array.Empty<EntityHistory>());

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<HistoryChangedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_publish_when_bulk_update_fails()
        {
            var items = new[] { new EntityHistory { Id = 1 } };
            Mocker.GetMock<IHistoryRepository>().Setup(repository => repository.UpdateMany(items))
                .Throws(new InvalidOperationException("Synthetic write failure"));

            Assert.Throws<InvalidOperationException>(() => Subject.UpdateMany(items));

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<HistoryChangedEvent>()), Times.Never());
        }
    }
}
