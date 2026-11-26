using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Books;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.CustomFormats
{
    public enum AudioCodecKind
    {
        Unknown = 0,
        [FieldOption(Label = "AAC (profile unreported)")]
        Aac = 1,
        [FieldOption(Label = "AAC LC")]
        AacLc = 2,
        [FieldOption(Label = "HE-AAC")]
        HeAac = 3,
        [FieldOption(Label = "HE-AAC v2")]
        HeAacV2 = 4,
        [FieldOption(Label = "xHE-AAC")]
        XheAac = 5,
        [FieldOption(Label = "USAC (profile unreported)")]
        Usac = 6,
        [FieldOption(Label = "AC-3")]
        Ac3 = 7,
        [FieldOption(Label = "E-AC-3")]
        EAc3 = 8,
        [FieldOption(Label = "E-AC-3 with Dolby Atmos")]
        EAc3Atmos = 9,
        [FieldOption(Label = "Dolby TrueHD")]
        TrueHd = 10,
        [FieldOption(Label = "TrueHD with Dolby Atmos")]
        TrueHdAtmos = 11,
        Opus = 12,
        [FieldOption(Label = "FLAC")]
        Flac = 13,
        [FieldOption(Label = "ALAC")]
        Alac = 14,
        [FieldOption(Label = "PCM")]
        Pcm = 15,
        [FieldOption(Label = "MP3")]
        Mp3 = 16,
        Vorbis = 17,
        [FieldOption(Label = "DTS-HD Master Audio")]
        DtsHdMasterAudio = 18,
        [FieldOption(Label = "Dolby Atmos (any carrier)")]
        Atmos = 19,
        Lossless = 20
    }

    public enum AudioBitrateKind
    {
        Unknown = 0,
        [FieldOption(Label = "Constant (CBR)")]
        Constant = 1,
        [FieldOption(Label = "Variable (VBR)")]
        Variable = 2
    }

    public enum AudioEvidenceOrigin
    {
        Release = 1,
        File = 2
    }

    public sealed class AudioFormatEvidence
    {
        public const string HistoryKey = "audioFormatEvidence";
        public const int SnapshotVersion = 1;
        private const int MaximumSnapshotLength = 64 * 1024;

        public int Version { get; set; }
        public AudioEvidenceOrigin Origin { get; set; }
        public AudioCodecKind Codec { get; set; }
        public int Channels { get; set; }
        public AudioBitrateKind BitrateMode { get; set; }
        public int SampleRate { get; set; }
        public string[] Narrators { get; set; } = Array.Empty<string>();

        public static AudioFormatEvidence ForRelease(RemoteBook book)
        {
            if (book.RecordedAudioFormatEvidence?.Origin == AudioEvidenceOrigin.Release)
            {
                return book.RecordedAudioFormatEvidence;
            }

            var release = book.Release;
            var stream = PrimaryStream(release?.MediaInfo?.Source == "MyAnonaMouse" ? release.MediaInfo : null);
            var evidence = FromStream(stream, AudioEvidenceOrigin.Release, release?.Narrators);
            if (stream == null && !string.IsNullOrWhiteSpace(release?.Codec))
            {
                evidence.Codec = ClassifyCodec(new AudioStreamInfo { AudioFormat = release.Codec });
            }

            return evidence;
        }

        public static AudioFormatEvidence ForFile(MediaInfoModel mediaInfo, Edition edition)
        {
            // Older file rows may predate the source label. Release metadata comes from a different path.
            var stream = PrimaryStream(mediaInfo?.Source is null or "MediaInfo" or "TagLib" ? mediaInfo : null);
            return FromStream(stream, AudioEvidenceOrigin.File, edition?.IsEbook == false ? edition.Narrators : null);
        }

        public static AudioFormatEvidence FromHistory(IDictionary<string, string> data)
        {
            return FromStored(RecordingLanguageEvidence.ReadHistory(data, HistoryKey));
        }

        public static AudioFormatEvidence FromStored(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumSnapshotLength ||
                !STJson.TryDeserialize<AudioFormatEvidence>(value, out var evidence) || evidence?.Version != SnapshotVersion ||
                !Enum.IsDefined(evidence.Origin))
            {
                return null;
            }

            if (!Enum.IsDefined(evidence.Codec) || evidence.Codec == AudioCodecKind.Lossless)
            {
                evidence.Codec = AudioCodecKind.Unknown;
            }

            if (!Enum.IsDefined(evidence.BitrateMode))
            {
                evidence.BitrateMode = AudioBitrateKind.Unknown;
            }

            evidence.Channels = Math.Max(0, evidence.Channels);
            evidence.SampleRate = Math.Max(0, evidence.SampleRate);
            evidence.Narrators = NormalizeNarrators(evidence.Narrators);
            return evidence;
        }

        public string ToStored()
        {
            return STJson.ToJson(this);
        }

        private static AudioFormatEvidence FromStream(AudioStreamInfo stream, AudioEvidenceOrigin origin, IEnumerable<string> narrators)
        {
            return new AudioFormatEvidence
            {
                Version = SnapshotVersion,
                Origin = origin,
                Codec = ClassifyCodec(stream),
                Channels = Math.Max(0, stream?.AudioChannels ?? 0),
                BitrateMode = Normalize(stream?.AudioBitrateMode) switch
                {
                    "CBR" or "CONSTANT" or "CONSTANTBITRATE" => AudioBitrateKind.Constant,
                    "VBR" or "VARIABLE" or "VARIABLEBITRATE" => AudioBitrateKind.Variable,
                    _ => AudioBitrateKind.Unknown
                },
                SampleRate = Math.Max(0, stream?.AudioSampleRate ?? 0),
                Narrators = NormalizeNarrators(narrators)
            };
        }

        private static string[] NormalizeNarrators(IEnumerable<string> values)
        {
            return values?.Where(name => !string.IsNullOrWhiteSpace(name) && name.Length <= 128)
                .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray() ?? Array.Empty<string>();
        }

        private static AudioStreamInfo PrimaryStream(MediaInfoModel mediaInfo)
        {
            if (mediaInfo?.AudioStreams?.Count > 0)
            {
                // Keep all conditions on the same stream. Never combine the codec of a default stream with another stream's channels.
                var reported = mediaInfo.AudioStreams.Where(stream => stream != null && !string.IsNullOrWhiteSpace(stream.AudioFormat));
                return reported.FirstOrDefault(stream => stream.IsDefault == true) ?? reported.FirstOrDefault();
            }

            return mediaInfo;
        }

        private static AudioCodecKind ClassifyCodec(AudioStreamInfo stream)
        {
            if (stream == null)
            {
                return AudioCodecKind.Unknown;
            }

            var format = Normalize(stream.AudioFormat);
            var profile = Normalize(stream.AudioProfile);
            var features = Normalize(stream.AudioAdditionalFeatures);
            var commercial = Normalize(stream.AudioCommercialName);
            var codecId = Normalize(stream.AudioCodecId);
            var aacFormat = format is "AAC" or "AACLC" or "HEAAC" or "HEAACV1" or "HEAACV2";

            if (format is "USAC" or "XHEAAC" || aacFormat || (format.Length == 0 && codecId == "MP4A4042"))
            {
                if (format == "XHEAAC" || profile == "XHEAAC" || features == "XHEAAC" || commercial == "XHEAAC")
                {
                    return AudioCodecKind.XheAac;
                }

                if (format == "USAC" || profile == "USAC" || codecId == "MP4A4042")
                {
                    return AudioCodecKind.Usac;
                }
            }

            if (aacFormat || (format.Length == 0 && codecId is "MP4A402" or "MP4A405" or "MP4A4029"))
            {
                if (format == "HEAACV2" || profile == "HEAACV2" || commercial == "HEAACV2" ||
                    features is "LCSBRPS" or "SBRPS" || codecId == "MP4A4029")
                {
                    return AudioCodecKind.HeAacV2;
                }

                if (format is "HEAAC" or "HEAACV1" || profile is "HEAAC" or "HEAACV1" || commercial == "HEAAC" ||
                    features is "LCSBR" or "SBR" || codecId == "MP4A405")
                {
                    return AudioCodecKind.HeAac;
                }

                return format == "AACLC" || profile is "LC" or "AACLC" || features == "LC" || codecId == "MP4A402" ? AudioCodecKind.AacLc : AudioCodecKind.Aac;
            }

            var atmos = commercial.EndsWith("WITHDOLBYATMOS", StringComparison.Ordinal) || commercial == "DOLBYATMOS";
            if (format is "EAC3" or "EAC3JOC" or "DOLBYDIGITALPLUS" || (format.Length == 0 && codecId == "EC3"))
            {
                return atmos || format == "EAC3JOC" || features == "JOC" ? AudioCodecKind.EAc3Atmos : AudioCodecKind.EAc3;
            }

            if (format is "TRUEHD" or "DOLBYTRUEHD" or "MLPFBA" || (format.Length == 0 && codecId == "TRUEHD"))
            {
                return atmos || profile == "TRUEHDATMOS" ? AudioCodecKind.TrueHdAtmos : AudioCodecKind.TrueHd;
            }

            if (format is "DOLBYATMOS" or "ATMOS" || atmos)
            {
                return AudioCodecKind.Atmos;
            }

            return format switch
            {
                "AC3" or "DOLBYDIGITAL" => AudioCodecKind.Ac3,
                "OPUS" => AudioCodecKind.Opus,
                "FLAC" => AudioCodecKind.Flac,
                "ALAC" => AudioCodecKind.Alac,
                "PCM" or "LPCM" => AudioCodecKind.Pcm,
                "MP3" or "MPEGVERSION1AUDIOLAYER3" or "MPEGVERSION2AUDIOLAYER3" => AudioCodecKind.Mp3,
                "MPEGAUDIO" when profile == "LAYER3" => AudioCodecKind.Mp3,
                "VORBIS" => AudioCodecKind.Vorbis,
                "DTSHDMA" or "DTSHDMASTERAUDIO" => AudioCodecKind.DtsHdMasterAudio,
                "DTSXLL" when profile == "MA" || commercial == "DTSHDMASTERAUDIO" => AudioCodecKind.DtsHdMasterAudio,
                _ => AudioCodecKind.Unknown
            };
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
        }
    }
}
