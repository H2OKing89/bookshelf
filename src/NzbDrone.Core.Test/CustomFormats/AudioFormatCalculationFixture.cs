using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormats
{
    [TestFixture]
    public class AudioFormatCalculationFixture : CoreTest<CustomFormatCalculationService>
    {
        private readonly Author _author = new Author { Name = "Synthetic Author" };

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ICustomFormatService>().Setup(service => service.All()).Returns(new List<CustomFormat>
            {
                new("Atmos", new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos }),
                new("Lossless", new AudioCodecSpecification { Value = (int)AudioCodecKind.Lossless }),
                new("Preferred narrator", new NarratorSpecification { Value = "Synthetic Narrator" }),
                new("Unknown codec", new AudioCodecSpecification { Value = (int)AudioCodecKind.Unknown })
            });
        }

        [Test]
        public void release_queue_and_history_should_read_advertised_audio_evidence_without_title_inference()
        {
            var remote = new RemoteBook
            {
                Author = _author,
                Release = new ReleaseInfo
                {
                    Title = "Owned recording xHE-AAC and Atmos",
                    MediaInfo = new MediaInfoModel { Source = "MyAnonaMouse", AudioFormat = "E-AC-3", AudioAdditionalFeatures = "JOC" },
                    Narrators = new List<string> { "  Synthetic narrator " }
                }
            };
            var original = AudioFormatEvidence.ForRelease(remote);
            var snapshot = original.ToStored();
            var history = new EntityHistory { SourceTitle = remote.Release.Title };
            history.Data[AudioFormatEvidence.HistoryKey] = snapshot;

            Subject.ParseCustomFormat(remote, 1).Should().HaveCount(2).And.OnlyContain(format => format.Name == "Atmos" || format.Name == "Preferred narrator");
            Subject.ParseCustomFormat(history, _author).Should().HaveCount(2);
            Subject.ParseCustomFormat(new Blocklist { SourceTitle = history.SourceTitle, AudioFormatEvidence = snapshot }, _author).Should().HaveCount(2);
            original.Origin.Should().Be(AudioEvidenceOrigin.Release);

            remote.RecordedAudioFormatEvidence = AudioFormatEvidence.FromHistory(history.Data);
            remote.Release.MediaInfo = new MediaInfoModel { Source = "MyAnonaMouse", AudioFormat = "FLAC" };
            Subject.ParseCustomFormat(remote, 1).Should().HaveCount(2).And.NotContain(format => format.Name == "Lossless");

            remote.RecordedAudioFormatEvidence = null;
            remote.Release = new ReleaseInfo { Title = "Atmos with Synthetic Narrator" };
            Subject.ParseCustomFormat(remote, 1).Should().ContainSingle().Which.Name.Should().Be("Unknown codec");

            history.Data[AudioFormatEvidence.HistoryKey] = "not audio evidence";
            Subject.ParseCustomFormat(history, _author).Should().ContainSingle().Which.Name.Should().Be("Unknown codec");
        }

        [Test]
        public void replacement_and_rename_should_score_local_measurements_and_the_linked_narrator()
        {
            var local = new LocalBook
            {
                Path = System.IO.Path.Combine(TempFolder, "before.m4b"),
                Author = _author,
                Edition = new Edition { Id = 7, Narrators = new List<string> { "Synthetic Narrator" } },
                FileTrackInfo = new ParsedTrackInfo { MediaInfo = new MediaInfoModel { Source = "MediaInfo", AudioFormat = "FLAC" } }
            };
            var file = ImportBookFileMetadata.Create(local, null, Mocker.GetMock<IHistoryService>().Object);

            Subject.ParseCustomFormat(local).Should().HaveCount(2).And.OnlyContain(format => format.Name == "Lossless" || format.Name == "Preferred narrator");
            file.Path = System.IO.Path.Combine(TempFolder, "renamed Atmos.m4b");
            Subject.ParseCustomFormat(file, _author).Should().HaveCount(2).And.OnlyContain(format => format.Name == "Lossless" || format.Name == "Preferred narrator");

            file.MediaInfo = new MediaInfoModel { Source = "MyAnonaMouse", AudioFormat = "E-AC-3", AudioAdditionalFeatures = "JOC" };
            Subject.ParseCustomFormat(file, _author).Should().HaveCount(2).And.OnlyContain(format => format.Name == "Unknown codec" || format.Name == "Preferred narrator");
        }
    }
}
