using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public static class MediaInfoJsonParser
    {
        public const int MaximumJsonLength = 1024 * 1024;
        private static readonly Regex AudioKey = new Regex(@"^Audio(?<index>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Number = new Regex(@"^\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>[kmg]?)\s*(?:b(?:it)?s?(?:/s|ps)?|hz|channels?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static MediaInfoModel ParseCli(string json)
        {
            var root = ParseObject(json);
            var tracks = (root?["media"] as JObject)?["track"] as JArray;
            if (tracks == null)
            {
                return null;
            }

            var general = tracks.OfType<JObject>().FirstOrDefault(x => Text(x, "@type") == "General");
            var streams = tracks.OfType<JObject>().Where(x => Text(x, "@type") == "Audio")
                .Take(64).Select((track, index) => ReadStream(track, index)).ToList();
            var model = FromStreams(streams, general, "MediaInfo");
            if (model != null)
            {
                model.SourceVersion = Text(root?["creatingLibrary"] as JObject, "version");
            }

            return model;
        }

        public static MediaInfoModel ParseMam(string json)
        {
            var root = ParseObject(json);
            if (root == null)
            {
                return null;
            }

            var streams = root.Properties().Where(x => AudioKey.IsMatch(x.Name) && x.Value is JObject)
                .OrderBy(x => int.TryParse(AudioKey.Match(x.Name).Groups["index"].Value, out var index) ? index : int.MaxValue)
                .Take(64).Select((property, index) => ReadStream((JObject)property.Value, index)).ToList();
            return FromStreams(streams, root["General"] as JObject ?? root["General1"] as JObject, "MyAnonaMouse");
        }

        private static JObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonLength)
            {
                return null;
            }

            try
            {
                var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
                var token = JToken.Parse(json, settings);
                if (token.Type == JTokenType.String)
                {
                    token = JToken.Parse(token.Value<string>(), settings);
                }

                return token as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static MediaInfoModel FromStreams(List<AudioStreamInfo> streams, JObject general, string source)
        {
            if (streams.Count == 0 || !streams.Any(x => !string.IsNullOrWhiteSpace(x.AudioFormat)))
            {
                return null;
            }

            var model = new MediaInfoModel
            {
                Source = source,
                ContainerFormat = Text(general, "Format"),
                MediaFileSizeBytes = ReadFileSize(general),
                ContainerDurationSeconds = Duration(general),
                AudioStreams = streams
            };
            var meaningfulStreams = streams.Where(x => !string.IsNullOrWhiteSpace(x.AudioFormat)).ToList();
            model.SetPrimaryStream(meaningfulStreams.FirstOrDefault(x => x.IsDefault == true) ?? meaningfulStreams[0]);
            model.DurationSeconds ??= model.ContainerDurationSeconds;
            return model;
        }

        private static long? ReadFileSize(JObject general)
        {
            return long.TryParse(Text(general, "FileSize"), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes > 0
                ? bytes
                : null;
        }

        private static AudioStreamInfo ReadStream(JObject track, int index)
        {
            var bitrate = PositiveNumber(track, "BitRate") ?? PositiveNumber(track, "BitRate_Nominal");
            var isDefault = Text(track, "Default");
            return new AudioStreamInfo
            {
                Index = index,
                AudioFormat = Text(track, "Format"),
                AudioProfile = Text(track, "Format_Profile"),
                AudioAdditionalFeatures = Text(track, "Format_AdditionalFeatures"),
                AudioCodecId = Text(track, "CodecID"),
                AudioCommercialName = Text(track, "Format_Commercial_IfAny") ?? Text(track, "Format_Commercial") ?? Text(track, "CommercialName"),
                AudioBitrate = Integer(bitrate / 1000),
                AudioBitrateMode = Text(track, "BitRate_Mode"),
                AudioChannels = Integer(PositiveNumber(track, "Channels")),
                AudioChannelLayout = Text(track, "ChannelLayout"),
                AudioBits = Integer(PositiveNumber(track, "BitDepth")),
                AudioSampleRate = Integer(PositiveNumber(track, "SamplingRate")),
                DurationSeconds = Duration(track),
                Language = Text(track, "Language"),
                Title = Text(track, "Title"),
                IsDefault = isDefault == null ? null : isDefault.Equals("Yes", StringComparison.OrdinalIgnoreCase) || isDefault.Equals("true", StringComparison.OrdinalIgnoreCase) || isDefault == "1"
            };
        }

        private static int Integer(double? value)
        {
            return value.HasValue && value.Value > 0 && value.Value <= int.MaxValue ? (int)Math.Round(value.Value) : 0;
        }

        private static double? Duration(JObject source)
        {
            var value = PositiveNumber(source, "Duration");
            return value.HasValue && value.Value < TimeSpan.MaxValue.TotalSeconds - 1 ? value : null;
        }

        private static double? PositiveNumber(JObject source, string key)
        {
            var text = Text(source, key);
            var match = text == null ? Match.Empty : Number.Match(text);
            if (!match.Success || !double.TryParse(match.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            switch (match.Groups["unit"].Value.ToLowerInvariant())
            {
                case "k": value *= 1000; break;
                case "m": value *= 1000000; break;
                case "g": value *= 1000000000; break;
            }

            return double.IsFinite(value) && value > 0 ? value : null;
        }

        private static string Text(JObject source, string key)
        {
            var token = source?[key];
            if (token == null || token.Type == JTokenType.Null || token is JContainer)
            {
                return null;
            }

            var value = token.ToString().Trim();
            return value.Length == 0 || value.Length > 2048 ? null : value;
        }
    }
}
