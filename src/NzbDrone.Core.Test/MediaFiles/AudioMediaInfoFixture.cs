using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class AudioMediaInfoFixture : CoreTest<AudioTagService>
    {
        private string FilePath => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "nin.mp3");

        [Test]
        public void should_enrich_local_files_without_replacing_descriptive_tags()
        {
            var original = new AudioTag(FilePath);
            var info = new MediaInfoModel
            {
                AudioFormat = "MPEG Audio",
                AudioProfile = "Layer 3",
                AudioBitrate = 192,
                DurationSeconds = 180.5,
                Source = "MediaInfo",
                SourceVersion = "26.10",
                AudioStreams = new List<AudioStreamInfo> { new AudioStreamInfo { AudioFormat = "MPEG Audio" } }
            };
            Mocker.GetMock<IMediaInfoProbe>().Setup(x => x.Read(FilePath)).Returns(info);

            var result = Subject.ReadTags(FilePath);
            result.Title.Should().Be(original.Title);
            result.MediaInfo.Should().BeSameAs(info);
            result.Duration.Should().Be(TimeSpan.FromSeconds(180.5));
        }

        [Test]
        public void should_retain_taglib_metadata_when_native_probe_is_unavailable()
        {
            Mocker.GetMock<IMediaInfoProbe>().Setup(x => x.Read(FilePath)).Returns((MediaInfoModel)null);
            var result = Subject.ReadTags(FilePath);
            result.MediaInfo.Source.Should().Be("TagLib");
            result.MediaInfo.AudioBitrate.Should().BeGreaterThan(0);
        }

        [Test]
        public void should_retain_known_technical_fields_when_single_stream_probe_is_partial()
        {
            var original = new AudioTag(FilePath);
            var info = new MediaInfoModel
            {
                AudioFormat = "MPEG Audio",
                Source = "MediaInfo",
                AudioStreams = new List<AudioStreamInfo> { new AudioStreamInfo { AudioFormat = "MPEG Audio" } }
            };
            Mocker.GetMock<IMediaInfoProbe>().Setup(x => x.Read(FilePath)).Returns(info);

            var result = Subject.ReadTags(FilePath);
            result.MediaInfo.AudioBitrate.Should().Be(original.MediaInfo.AudioBitrate);
            result.MediaInfo.AudioChannels.Should().Be(original.MediaInfo.AudioChannels);
            result.MediaInfo.AudioSampleRate.Should().Be(original.MediaInfo.AudioSampleRate);
            result.MediaInfo.AudioStreams[0].AudioBitrate.Should().Be(original.MediaInfo.AudioBitrate);
        }
    }
}
