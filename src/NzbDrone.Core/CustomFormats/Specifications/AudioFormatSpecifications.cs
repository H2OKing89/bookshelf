using System;
using System.Linq;
using FluentValidation.Results;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public abstract class AudioFormatSpecificationBase : CustomFormatSpecificationBase
    {
        public override bool IsSatisfiedBy(CustomFormatInput input)
        {
            var match = MatchEvidence(input.Audio);
            return match.HasValue && match.Value != Negate;
        }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            return MatchEvidence(input.Audio) == true;
        }

        protected abstract bool? MatchEvidence(AudioFormatEvidence evidence);

        protected static NzbDroneValidationResult ValidateValue(bool valid, string message)
        {
            return valid ? new NzbDroneValidationResult() : new NzbDroneValidationResult(new[] { new ValidationFailure("Value", message) });
        }
    }

    public class AudioCodecSpecification : AudioFormatSpecificationBase
    {
        public override int Order => 10;
        public override string ImplementationName => "Audio Codec";

        [FieldDefinition(0, Label = "CustomFormatsSpecificationAudioCodec", HelpText = "CustomFormatsSpecificationAudioCodecHelpText", Type = FieldType.Select, SelectOptions = typeof(AudioCodecKind))]
        public int Value { get; set; } = (int)AudioCodecKind.AacLc;

        public override NzbDroneValidationResult Validate()
        {
            return ValidateValue(Enum.IsDefined(typeof(AudioCodecKind), Value), "Choose a supported audio codec.");
        }

        protected override bool? MatchEvidence(AudioFormatEvidence evidence)
        {
            var selected = (AudioCodecKind)Value;
            var actual = evidence?.Codec ?? AudioCodecKind.Unknown;
            if (!Enum.IsDefined(selected))
            {
                return null;
            }

            if (selected == AudioCodecKind.Unknown)
            {
                return actual == AudioCodecKind.Unknown;
            }

            if (actual == AudioCodecKind.Unknown ||
                (actual == AudioCodecKind.Aac && selected is AudioCodecKind.AacLc or AudioCodecKind.HeAac or AudioCodecKind.HeAacV2) ||
                (actual == AudioCodecKind.Usac && selected == AudioCodecKind.XheAac) ||
                (actual == AudioCodecKind.EAc3 && selected is AudioCodecKind.Atmos or AudioCodecKind.EAc3Atmos) ||
                (actual == AudioCodecKind.TrueHd && selected is AudioCodecKind.Atmos or AudioCodecKind.TrueHdAtmos))
            {
                return null;
            }

            return selected switch
            {
                AudioCodecKind.Atmos => actual is AudioCodecKind.Atmos or AudioCodecKind.EAc3Atmos or AudioCodecKind.TrueHdAtmos,
                AudioCodecKind.Lossless when actual == AudioCodecKind.Atmos => null,
                AudioCodecKind.Lossless => actual is AudioCodecKind.Flac or AudioCodecKind.Alac or AudioCodecKind.Pcm or
                    AudioCodecKind.TrueHd or AudioCodecKind.TrueHdAtmos or AudioCodecKind.DtsHdMasterAudio,
                AudioCodecKind.EAc3Atmos or AudioCodecKind.TrueHdAtmos when actual == AudioCodecKind.Atmos => null,
                _ => actual == selected
            };
        }
    }

    public class AudioChannelsSpecification : AudioFormatSpecificationBase
    {
        public override int Order => 11;
        public override string ImplementationName => "Audio Channels";

        [FieldDefinition(0, Label = "CustomFormatsSpecificationAudioChannels", HelpText = "CustomFormatsSpecificationAudioChannelsHelpText", Type = FieldType.Number)]
        public int Value { get; set; } = 2;

        public override NzbDroneValidationResult Validate()
        {
            return ValidateValue(Value >= -1, "Use -1 for three or more channels, 0 for unknown, or an exact channel count.");
        }

        protected override bool? MatchEvidence(AudioFormatEvidence evidence)
        {
            var channels = evidence?.Channels ?? 0;
            if (Value < -1 || (channels == 0 && Value != 0))
            {
                return null;
            }

            return Value == -1 ? channels >= 3 : channels == Value;
        }
    }

    public class AudioBitrateModeSpecification : AudioFormatSpecificationBase
    {
        public override int Order => 12;
        public override string ImplementationName => "Audio Bitrate Mode";

        [FieldDefinition(0, Label = "CustomFormatsSpecificationAudioBitrateMode", HelpText = "CustomFormatsSpecificationAudioBitrateModeHelpText", Type = FieldType.Select, SelectOptions = typeof(AudioBitrateKind))]
        public int Value { get; set; } = (int)AudioBitrateKind.Variable;

        public override NzbDroneValidationResult Validate()
        {
            return ValidateValue(Enum.IsDefined(typeof(AudioBitrateKind), Value), "Choose a supported audio bitrate mode.");
        }

        protected override bool? MatchEvidence(AudioFormatEvidence evidence)
        {
            var mode = evidence?.BitrateMode ?? AudioBitrateKind.Unknown;
            if (!Enum.IsDefined(typeof(AudioBitrateKind), Value) || (mode == AudioBitrateKind.Unknown && Value != 0))
            {
                return null;
            }

            return (int)mode == Value;
        }
    }

    public class AudioSampleRateSpecification : AudioFormatSpecificationBase
    {
        public override int Order => 13;
        public override string ImplementationName => "Audio Sample Rate";

        [FieldDefinition(0, Label = "CustomFormatsSpecificationAudioSampleRate", HelpText = "CustomFormatsSpecificationAudioSampleRateHelpText", Type = FieldType.Number, Unit = "Hz")]
        public int Value { get; set; } = 44100;

        public override NzbDroneValidationResult Validate()
        {
            return ValidateValue(Value >= 0, "Use 0 for unknown or an exact sample rate in Hz.");
        }

        protected override bool? MatchEvidence(AudioFormatEvidence evidence)
        {
            var rate = evidence?.SampleRate ?? 0;
            if (Value < 0 || (rate == 0 && Value != 0))
            {
                return null;
            }

            return rate == Value;
        }
    }

    public class NarratorSpecification : AudioFormatSpecificationBase
    {
        public override int Order => 14;
        public override string ImplementationName => "Narrator";

        [FieldDefinition(0, Label = "CustomFormatsSpecificationNarrator", HelpText = "CustomFormatsSpecificationNarratorHelpText")]
        public string Value { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return ValidateValue(!string.IsNullOrWhiteSpace(Value) && Value.Length <= 128, "Enter a full narrator name up to 128 characters.");
        }

        protected override bool? MatchEvidence(AudioFormatEvidence evidence)
        {
            if (string.IsNullOrWhiteSpace(Value) || evidence?.Narrators?.Length is not > 0)
            {
                return null;
            }

            return evidence.Narrators.Contains(Value.Trim(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
