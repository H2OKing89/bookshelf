using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaInfoJsonParserFixture
    {
        private static string Cli(params JObject[] streams)
        {
            return new JObject
            {
                ["creatingLibrary"] = new JObject { ["version"] = "26.10" },
                ["media"] = new JObject
                {
                    ["track"] = new JArray(new JObject { ["@type"] = "General", ["Format"] = "MPEG-4", ["Duration"] = "120.5" })
                }
            }.WithStreams(streams).ToString();
        }

        [Test]
        public void should_preserve_explicit_xhe_aac_profile_and_units()
        {
            var stream = new JObject
            {
                ["@type"] = "Audio", ["Format"] = "USAC", ["Format_AdditionalFeatures"] = "xHE-AAC",
                ["CodecID"] = "mp4a-40-42", ["BitRate"] = "64000", ["SamplingRate"] = "48000",
                ["Channels"] = "2", ["ChannelLayout"] = "L R", ["BitRate_Mode"] = "VBR", ["Duration"] = "120.25"
            };

            var result = MediaInfoJsonParser.ParseCli(Cli(stream));
            result.AudioFormat.Should().Be("USAC");
            result.AudioAdditionalFeatures.Should().Be("xHE-AAC");
            result.AudioCodecId.Should().Be("mp4a-40-42");
            result.AudioBitrate.Should().Be(64);
            result.AudioSampleRate.Should().Be(48000);
            result.DurationSeconds.Should().Be(120.25);
            result.Source.Should().Be("MediaInfo");
            result.SourceVersion.Should().Be("26.10");
            result.ContainerFormat.Should().Be("MPEG-4");
        }

        [Test]
        public void should_preserve_atmos_and_all_streams_using_the_default_for_summary()
        {
            var result = MediaInfoJsonParser.ParseCli(Cli(
                new JObject { ["@type"] = "Audio", ["Format"] = "AAC", ["Default"] = "No" },
                new JObject
                {
                    ["@type"] = "Audio", ["Format"] = "E-AC-3", ["Format_AdditionalFeatures"] = "JOC",
                    ["Format_Commercial_IfAny"] = "Dolby Digital Plus with Dolby Atmos", ["Default"] = "Yes",
                    ["Channels"] = "6", ["Language"] = "en", ["Title"] = "Immersive audio"
                }));

            result.AudioStreams.Should().HaveCount(2);
            result.AudioFormat.Should().Be("E-AC-3");
            result.AudioCommercialName.Should().Be("Dolby Digital Plus with Dolby Atmos");
            result.AudioAdditionalFeatures.Should().Be("JOC");
            result.Index.Should().Be(1);
            result.AudioChannels.Should().Be(6);
            result.DurationSeconds.Should().Be(120.5);
            result.Language.Should().Be("en");
        }

        [Test]
        public void should_not_invent_xhe_aac_or_atmos_from_base_format_and_channels()
        {
            var result = MediaInfoJsonParser.ParseCli(Cli(new JObject { ["@type"] = "Audio", ["Format"] = "USAC", ["Channels"] = 8 }));
            result.AudioAdditionalFeatures.Should().BeNull();
            result.AudioCommercialName.Should().BeNull();
        }

        [Test]
        public void should_not_select_an_empty_default_stream_over_a_known_stream()
        {
            var result = MediaInfoJsonParser.ParseCli(Cli(
                new JObject { ["@type"] = "Audio", ["Default"] = "Yes" },
                new JObject { ["@type"] = "Audio", ["Format"] = "AAC" }));
            result.AudioFormat.Should().Be("AAC");
            result.Index.Should().Be(1);
            result.AudioStreams.Should().HaveCount(2);
        }

        [Test]
        public void should_parse_mam_units_and_keep_numeric_stream_order()
        {
            var result = MediaInfoJsonParser.ParseMam(@"{""Audio10"":{""Format"":""MP3""},""Audio2"":{""Format"":""AAC"",""BitRate"":""126k"",""SamplingRate"":""44.1 kHz"",""Channels"":2,""BitRate_Mode"":""VBR""},""menu"":{""00:00:00"":""Chapter 1""}}");
            result.Source.Should().Be("MyAnonaMouse");
            result.AudioFormat.Should().Be("AAC");
            result.AudioBitrate.Should().Be(126);
            result.AudioSampleRate.Should().Be(44100);
            result.AudioStreams.Should().HaveCount(2);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{broken")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("{\"media\":1}")]
        [TestCase("{\"media\":{\"track\":[]}}")]
        [TestCase("{\"Audio1\":[]}")]
        public void should_tolerate_unavailable_or_malformed_optional_reports(string json)
        {
            MediaInfoJsonParser.ParseCli(json).Should().BeNull();
            MediaInfoJsonParser.ParseMam(json).Should().BeNull();
        }

        [Test]
        public void should_reject_oversized_reports()
        {
            var json = new string(' ', MediaInfoJsonParser.MaximumJsonLength + 1);
            MediaInfoJsonParser.ParseCli(json).Should().BeNull();
            MediaInfoJsonParser.ParseMam(json).Should().BeNull();
        }

        [Test]
        public void should_ignore_invalid_and_overflowing_numbers()
        {
            var result = MediaInfoJsonParser.ParseMam(@"{""Audio1"":{""Format"":""AAC"",""BitRate"":""-128k"",""SamplingRate"":""not known"",""Channels"":""99999999999999999999"",""Duration"":""999999999999999999999999999""}}");
            result.AudioBitrate.Should().Be(0);
            result.AudioSampleRate.Should().Be(0);
            result.AudioChannels.Should().Be(0);
            result.DurationSeconds.Should().BeNull();
        }
    }

    internal static class MediaInfoTestJsonExtensions
    {
        public static JObject WithStreams(this JObject root, JObject[] streams)
        {
            foreach (var stream in streams)
            {
                ((JArray)root["media"]["track"]).Add(stream);
            }

            return root;
        }
    }
}
