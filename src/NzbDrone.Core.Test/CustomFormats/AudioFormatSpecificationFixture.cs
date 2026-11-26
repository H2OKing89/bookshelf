using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.CustomFormats
{
    [TestFixture]
    public class AudioFormatSpecificationFixture
    {
        [TestCase("AAC", "", "LC", "", AudioCodecKind.AacLc)]
        [TestCase("AAC", "LC", "SBR", "", AudioCodecKind.HeAac)]
        [TestCase("AAC", "LC", "LC SBR PS", "", AudioCodecKind.HeAacV2)]
        [TestCase("USAC", "", "xHE-AAC", "", AudioCodecKind.XheAac)]
        [TestCase("USAC", "", "", "", AudioCodecKind.Usac)]
        [TestCase("E-AC-3", "", "JOC", "", AudioCodecKind.EAc3Atmos)]
        [TestCase("E-AC-3", "", "", "", AudioCodecKind.EAc3)]
        [TestCase("MLP FBA", "", "16-ch", "", AudioCodecKind.TrueHd)]
        [TestCase("MLP FBA", "", "16-ch", "Dolby TrueHD with Dolby Atmos", AudioCodecKind.TrueHdAtmos)]
        [TestCase("Opus", "", "", "", AudioCodecKind.Opus)]
        [TestCase("FLAC", "", "", "", AudioCodecKind.Flac)]
        [TestCase("ALAC", "", "", "", AudioCodecKind.Alac)]
        [TestCase("Not a known codec", "", "", "", AudioCodecKind.Unknown)]
        public void structured_audio_fields_should_distinguish_profiles_and_carriers(string format, string profile, string features, string commercial, AudioCodecKind expected)
        {
            var evidence = AudioFormatEvidence.ForFile(
                new MediaInfoModel
                {
                    Source = "MediaInfo",
                    AudioFormat = format,
                    AudioProfile = profile,
                    AudioAdditionalFeatures = features,
                    AudioCommercialName = commercial,
                    AudioChannels = 8
                },
                null);

            evidence.Codec.Should().Be(expected);
            new AudioCodecSpecification { Value = (int)expected }.IsSatisfiedBy(new CustomFormatInput { Audio = evidence }).Should().BeTrue();
        }

        [Test]
        public void channel_bitrate_and_sample_rate_conditions_should_use_the_same_default_stream()
        {
            const string json = """
                {"Audio0":{"Format":"E-AC-3","Format_AdditionalFeatures":"JOC","Channels":6,"BitRate_Mode":"CBR","SamplingRate":48000},
                 "Audio1":{"Format":"AAC","Format_Profile":"LC","Channels":2,"BitRate_Mode":"VBR","SamplingRate":44100,"Default":"Yes"}}
                """;
            var release = new RemoteBook { Release = new ReleaseInfo { MediaInfo = MediaInfoJsonParser.ParseMam(json) } };
            var input = new CustomFormatInput { Audio = AudioFormatEvidence.ForRelease(release) };

            new AudioCodecSpecification { Value = (int)AudioCodecKind.AacLc }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos }.IsSatisfiedBy(input).Should().BeFalse();
            new AudioChannelsSpecification { Value = 1 }.IsSatisfiedBy(input).Should().BeFalse();
            new AudioChannelsSpecification { Value = 2 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioBitrateModeSpecification { Value = (int)AudioBitrateKind.Variable }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioSampleRateSpecification { Value = 44100 }.IsSatisfiedBy(input).Should().BeTrue();

            release.Release.MediaInfo.AudioStreams[1].IsDefault = false;
            input.Audio = AudioFormatEvidence.ForRelease(release);

            new AudioChannelsSpecification { Value = -1 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioChannelsSpecification { Value = 6 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioBitrateModeSpecification { Value = (int)AudioBitrateKind.Constant }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioSampleRateSpecification { Value = 44100 }.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void unknown_and_incomplete_evidence_should_not_award_a_negated_match()
        {
            var input = new CustomFormatInput();
            new AudioCodecSpecification { Value = 0 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioChannelsSpecification { Value = 0 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioBitrateModeSpecification { Value = 0 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioSampleRateSpecification { Value = 0 }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos, Negate = true }.IsSatisfiedBy(input).Should().BeFalse();
            new AudioChannelsSpecification { Value = -1, Negate = true }.IsSatisfiedBy(input).Should().BeFalse();
            new AudioBitrateModeSpecification { Value = 1, Negate = true }.IsSatisfiedBy(input).Should().BeFalse();
            new AudioSampleRateSpecification { Value = 44100, Negate = true }.IsSatisfiedBy(input).Should().BeFalse();
            new NarratorSpecification { Value = "Synthetic narrator", Negate = true }.IsSatisfiedBy(input).Should().BeFalse();

            input.Audio = new AudioFormatEvidence { Codec = AudioCodecKind.Aac };
            new AudioCodecSpecification { Value = (int)AudioCodecKind.AacLc, Negate = true }.IsSatisfiedBy(input).Should().BeFalse();
            input.Audio.Codec = AudioCodecKind.EAc3;
            new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos, Negate = true }.IsSatisfiedBy(input).Should().BeFalse();
            input.Audio.Codec = AudioCodecKind.Flac;
            new AudioCodecSpecification { Value = (int)AudioCodecKind.Atmos, Negate = true }.IsSatisfiedBy(input).Should().BeTrue();
            new AudioCodecSpecification { Value = (int)AudioCodecKind.Lossless }.IsSatisfiedBy(input).Should().BeTrue();
        }

        [Test]
        public void persisted_conditions_should_roundtrip_without_displacing_existing_ones()
        {
            var options = STJson.GetSerializerSettings();
            options.Converters.Add(new CustomFormatSpecificationListConverter());
            var specifications = new List<ICustomFormatSpecification>
            {
                new ReleaseTitleSpecification { Name = "Existing title", Value = "Synthetic" },
                new AudioCodecSpecification { Name = "Codec", Value = (int)AudioCodecKind.XheAac, Required = true },
                new AudioChannelsSpecification { Value = -1 },
                new AudioBitrateModeSpecification { Value = (int)AudioBitrateKind.Constant },
                new AudioSampleRateSpecification { Value = 48000, Negate = true },
                new NarratorSpecification { Value = "Synthetic narrator" }
            };

            var saved = JsonSerializer.Serialize(specifications, options);
            var restored = JsonSerializer.Deserialize<List<ICustomFormatSpecification>>(saved, options);

            JsonSerializer.Serialize(restored, options).Should().Be(saved);
        }
    }
}
