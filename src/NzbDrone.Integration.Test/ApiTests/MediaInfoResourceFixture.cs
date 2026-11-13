using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using Readarr.Api.V1.BookFiles;
using Readarr.Api.V1.Indexers;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class MediaInfoResourceFixture
    {
        [TestCase("AAC", "LC", null, null, "AAC LC")]
        [TestCase("AAC", null, "LC", null, "AAC LC")]
        [TestCase("AAC", "LC", "SBR", null, "HE-AAC")]
        [TestCase("AAC", "LC", "SBR PS", null, "HE-AAC v2")]
        [TestCase("AAC", "HE-AACv2 / HE-AAC / LC", null, null, "HE-AAC v2")]
        [TestCase("USAC", null, null, null, "USAC")]
        [TestCase("USAC", null, "xHE-AAC", null, "xHE-AAC (USAC)")]
        [TestCase("AAC", null, null, "xHE-AAC", "xHE-AAC (USAC)")]
        [TestCase("E-AC-3", null, "JOC", null, "Dolby Atmos (E-AC-3)")]
        [TestCase("E-AC-3", null, null, "Dolby Digital Plus with Dolby Atmos", "Dolby Atmos (E-AC-3)")]
        [TestCase("E-AC-3", null, null, null, "E-AC-3")]
        [TestCase("MPEG Audio", "Layer 3", null, null, "MP3")]
        [TestCase("MLP FBA", null, null, "Dolby TrueHD", "Dolby TrueHD")]
        [TestCase("A future codec", null, null, null, "A future codec")]
        public void should_label_only_reported_codec_features(string format, string profile, string features, string commercialName, string expected)
        {
            var model = new MediaInfoModel
            {
                AudioFormat = format,
                AudioProfile = profile,
                AudioAdditionalFeatures = features,
                AudioCommercialName = commercialName,
                AudioChannels = 8
            };

            MediaInfoFormatter.FormatAudioCodec(model).Should().Be(expected);
        }

        [Test]
        public void should_preserve_the_existing_formatted_api_fields_and_expose_all_stream_details()
        {
            var alternate = new AudioStreamInfo
            {
                Index = 1,
                AudioFormat = "USAC",
                AudioAdditionalFeatures = "xHE-AAC",
                AudioCodecId = "mp4a-40-42",
                AudioBitrate = 64,
                AudioBitrateMode = "VBR",
                AudioChannels = 2,
                AudioChannelLayout = "L R",
                AudioSampleRate = 48000,
                DurationSeconds = 223438.5,
                Language = "eng",
                Title = "An alternate audio stream",
                IsDefault = false
            };
            var model = new MediaInfoModel
            {
                ContainerFormat = "MPEG-4",
                Source = "MediaInfo",
                SourceVersion = "26.10",
                AudioStreams = new List<AudioStreamInfo> { alternate }
            };
            model.SetPrimaryStream(alternate);

            var resource = Json.Deserialize<MediaInfoResource>(model.ToResource().ToJson());

            resource.AudioCodec.Should().Be("xHE-AAC (USAC)");
            resource.AudioBitRate.Should().Be("64 kbps");
            resource.AudioSampleRate.Should().Be("48kHz");
            resource.AudioChannels.Should().Be(2);
            resource.AudioBits.Should().BeEmpty();
            resource.Source.Should().Be("MediaInfo");
            resource.SourceVersion.Should().Be("26.10");
            resource.ContainerFormat.Should().Be("MPEG-4");
            resource.AudioAdditionalFeatures.Should().Be("xHE-AAC");
            resource.AudioStreams.Should().ContainSingle();
            var stream = resource.AudioStreams[0];
            stream.Index.Should().Be(1);
            stream.AudioCodecId.Should().Be("mp4a-40-42");
            stream.AudioBitrateMode.Should().Be("VBR");
            stream.AudioChannelLayout.Should().Be("L R");
            stream.Language.Should().Be("eng");
            stream.Title.Should().Be("An alternate audio stream");
            stream.DurationSeconds.Should().Be(223438.5);
            stream.IsDefault.Should().BeFalse();
        }

        [Test]
        public void should_allow_legacy_files_without_saved_media_info()
        {
            ((MediaInfoModel)null).ToResource().Should().BeNull();
            MediaInfoFormatter.FormatAudioCodec(null).Should().Be("Unknown");
            MediaInfoFormatter.FormatAudioBitrate(null).Should().BeEmpty();
            MediaInfoFormatter.FormatAudioSampleRate(null).Should().BeEmpty();
            MediaInfoFormatter.FormatAudioBitsPerSample(null).Should().BeEmpty();
            MediaInfoFormatter.FormatAudioChannels(null).Should().Be(0);
        }

        [Test]
        public void should_not_present_missing_measurements_as_real_zero_values()
        {
            var resource = new MediaInfoModel { AudioFormat = "AAC" }.ToResource();

            resource.AudioBitRate.Should().BeEmpty();
            resource.AudioBits.Should().BeEmpty();
            resource.AudioSampleRate.Should().BeEmpty();
            resource.AudioStreams.Should().BeEmpty();
            resource.DurationSeconds.Should().BeNull();
            resource.IsDefault.Should().BeNull();
        }

        [Test]
        public void should_expose_mam_reports_on_releases_without_accepting_client_media_info_as_verified_data()
        {
            var release = new ReleaseInfo
            {
                Title = "A reported release",
                PublishDate = DateTime.UtcNow,
                MediaInfo = new MediaInfoModel { Source = "MyAnonaMouse", AudioFormat = "AAC", AudioProfile = "LC" }
            };
            var decision = new DownloadDecision(new RemoteBook { Release = release, ParsedBookInfo = new ParsedBookInfo() });

            var resource = decision.ToResource();

            resource.MediaInfo.Source.Should().Be("MyAnonaMouse");
            resource.MediaInfo.AudioCodec.Should().Be("AAC LC");
            resource.ToModel().MediaInfo.Should().BeNull();
        }
    }
}
