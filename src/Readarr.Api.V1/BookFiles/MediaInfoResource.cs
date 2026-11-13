using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using Readarr.Http.REST;

namespace Readarr.Api.V1.BookFiles
{
    public class MediaInfoResource : RestResource
    {
        public decimal AudioChannels { get; set; }
        public string AudioBitRate { get; set; }
        public string AudioCodec { get; set; }
        public string AudioBits { get; set; }
        public string AudioSampleRate { get; set; }
        public string AudioFormat { get; set; }
        public string AudioProfile { get; set; }
        public string AudioAdditionalFeatures { get; set; }
        public string AudioCodecId { get; set; }
        public string AudioCommercialName { get; set; }
        public string AudioBitrateMode { get; set; }
        public string AudioChannelLayout { get; set; }
        public double? DurationSeconds { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
        public bool? IsDefault { get; set; }
        public string ContainerFormat { get; set; }
        public string Source { get; set; }
        public string SourceVersion { get; set; }
        public List<AudioStreamInfoResource> AudioStreams { get; set; }
    }

    public class AudioStreamInfoResource
    {
        public int Index { get; set; }
        public decimal AudioChannels { get; set; }
        public string AudioBitRate { get; set; }
        public string AudioCodec { get; set; }
        public string AudioBits { get; set; }
        public string AudioSampleRate { get; set; }
        public string AudioFormat { get; set; }
        public string AudioProfile { get; set; }
        public string AudioAdditionalFeatures { get; set; }
        public string AudioCodecId { get; set; }
        public string AudioCommercialName { get; set; }
        public string AudioBitrateMode { get; set; }
        public string AudioChannelLayout { get; set; }
        public double? DurationSeconds { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
        public bool? IsDefault { get; set; }
    }

    public static class MediaInfoResourceMapper
    {
        public static MediaInfoResource ToResource(this MediaInfoModel model)
        {
            if (model == null)
            {
                return null;
            }

            return new MediaInfoResource
            {
                AudioChannels = MediaInfoFormatter.FormatAudioChannels(model),
                AudioCodec = MediaInfoFormatter.FormatAudioCodec(model),
                AudioBitRate = MediaInfoFormatter.FormatAudioBitrate(model),
                AudioBits = MediaInfoFormatter.FormatAudioBitsPerSample(model),
                AudioSampleRate = MediaInfoFormatter.FormatAudioSampleRate(model),
                AudioFormat = model.AudioFormat,
                AudioProfile = model.AudioProfile,
                AudioAdditionalFeatures = model.AudioAdditionalFeatures,
                AudioCodecId = model.AudioCodecId,
                AudioCommercialName = model.AudioCommercialName,
                AudioBitrateMode = model.AudioBitrateMode,
                AudioChannelLayout = model.AudioChannelLayout,
                DurationSeconds = model.DurationSeconds,
                Language = model.Language,
                Title = model.Title,
                IsDefault = model.IsDefault,
                ContainerFormat = model.ContainerFormat,
                Source = model.Source,
                SourceVersion = model.SourceVersion,
                AudioStreams = model.AudioStreams?.Where(stream => stream != null).Select(ToResource).ToList() ?? new List<AudioStreamInfoResource>()
            };
        }

        public static AudioStreamInfoResource ToResource(this AudioStreamInfo model)
        {
            return new AudioStreamInfoResource
            {
                Index = model.Index,
                AudioChannels = MediaInfoFormatter.FormatAudioChannels(model),
                AudioCodec = MediaInfoFormatter.FormatAudioCodec(model),
                AudioBitRate = MediaInfoFormatter.FormatAudioBitrate(model),
                AudioBits = MediaInfoFormatter.FormatAudioBitsPerSample(model),
                AudioSampleRate = MediaInfoFormatter.FormatAudioSampleRate(model),
                AudioFormat = model.AudioFormat,
                AudioProfile = model.AudioProfile,
                AudioAdditionalFeatures = model.AudioAdditionalFeatures,
                AudioCodecId = model.AudioCodecId,
                AudioCommercialName = model.AudioCommercialName,
                AudioBitrateMode = model.AudioBitrateMode,
                AudioChannelLayout = model.AudioChannelLayout,
                DurationSeconds = model.DurationSeconds,
                Language = model.Language,
                Title = model.Title,
                IsDefault = model.IsDefault
            };
        }
    }
}
