using System;
using System.Collections.Generic;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public static class MediaInfoFormatter
    {
        public static string FormatAudioBitrate(AudioStreamInfo mediaInfo)
        {
            return mediaInfo != null && mediaInfo.AudioBitrate > 0 ? mediaInfo.AudioBitrate + " kbps" : string.Empty;
        }

        public static string FormatAudioBitsPerSample(AudioStreamInfo mediaInfo)
        {
            return mediaInfo != null && mediaInfo.AudioBits > 0 ? mediaInfo.AudioBits + "bit" : string.Empty;
        }

        public static string FormatAudioSampleRate(AudioStreamInfo mediaInfo)
        {
            return mediaInfo != null && mediaInfo.AudioSampleRate > 0 ? $"{(double)mediaInfo.AudioSampleRate / 1000:0.#}kHz" : string.Empty;
        }

        public static decimal FormatAudioChannels(AudioStreamInfo mediaInfo)
        {
            return mediaInfo?.AudioChannels ?? 0;
        }

        public static readonly Dictionary<Codec, string> CodecNames = new Dictionary<Codec, string>
        {
            { Codec.MP1, "MP1" },
            { Codec.MP2, "MP2" },
            { Codec.AAC, "AAC" },
            { Codec.AACVBR, "AAC" },
            { Codec.ALAC, "ALAC" },
            { Codec.APE, "APE" },
            { Codec.FLAC, "FLAC" },
            { Codec.MP3CBR, "MP3" },
            { Codec.MP3VBR, "MP3" },
            { Codec.OGG, "OGG" },
            { Codec.OPUS, "OPUS" },
            { Codec.WAV, "PCM" },
            { Codec.WAVPACK, "WavPack" },
            { Codec.WMA, "WMA" }
        };

        public static string FormatAudioCodec(AudioStreamInfo mediaInfo)
        {
            if (mediaInfo == null)
            {
                return "Unknown";
            }

            var format = mediaInfo.AudioFormat?.Trim() ?? string.Empty;
            var reported = string.Join(" ", format, mediaInfo.AudioProfile, mediaInfo.AudioAdditionalFeatures, mediaInfo.AudioCommercialName);
            var normalized = reported.Replace(" ", string.Empty).Replace("_", "-");

            // USAC is also used outside the xHE-AAC profile. Require an explicit xHE-AAC label.
            if (Contains(normalized, "xHE-AAC") || Contains(normalized, "xHEAAC"))
            {
                return "xHE-AAC (USAC)";
            }

            if (Contains(reported, "USAC"))
            {
                return "USAC";
            }

            if (string.Equals(format, "MPEG Audio", StringComparison.OrdinalIgnoreCase))
            {
                if (Contains(normalized, "Layer3"))
                {
                    return "MP3";
                }

                if (Contains(normalized, "Layer2"))
                {
                    return "MP2";
                }

                if (Contains(normalized, "Layer1"))
                {
                    return "MP1";
                }
            }

            var codec = QualityParser.ParseCodec(format, null);
            var codecName = CodecNames.TryGetValue(codec, out var knownName) ? knownName : format;
            if (Contains(reported, "Atmos") ||
                (Contains(reported, "JOC") && (Contains(format, "E-AC-3") || Contains(format, "EAC3") || Contains(reported, "Dolby"))))
            {
                return string.IsNullOrWhiteSpace(format) ? "Dolby Atmos" : $"Dolby Atmos ({format})";
            }

            if (codec == Codec.AAC || codec == Codec.AACVBR || Contains(format, "AAC"))
            {
                if (Contains(normalized, "HE-AACv2") || Contains(normalized, "HEAACv2") || Contains(reported, "PS"))
                {
                    return "HE-AAC v2";
                }

                if (Contains(normalized, "HE-AAC") || Contains(normalized, "HEAAC") || Contains(reported, "SBR"))
                {
                    return "HE-AAC";
                }

                if (string.Equals(mediaInfo.AudioProfile?.Trim(), "LC", StringComparison.OrdinalIgnoreCase) || Contains(normalized, "AACLC"))
                {
                    return "AAC LC";
                }
            }

            if (codec == Codec.Unknown && !string.IsNullOrWhiteSpace(mediaInfo.AudioCommercialName))
            {
                return mediaInfo.AudioCommercialName.Trim();
            }

            return string.IsNullOrWhiteSpace(codecName) ? "Unknown" : codecName;
        }

        private static bool Contains(string value, string search)
        {
            return value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
