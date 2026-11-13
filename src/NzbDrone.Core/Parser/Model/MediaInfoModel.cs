using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Parser.Model
{
    public class AudioStreamInfo
    {
        public int Index { get; set; }
        public string AudioFormat { get; set; }
        public string AudioProfile { get; set; }
        public string AudioAdditionalFeatures { get; set; }
        public string AudioCodecId { get; set; }
        public string AudioCommercialName { get; set; }
        public int AudioBitrate { get; set; }
        public string AudioBitrateMode { get; set; }
        public int AudioChannels { get; set; }
        public string AudioChannelLayout { get; set; }
        public int AudioBits { get; set; }
        public int AudioSampleRate { get; set; }
        public double? DurationSeconds { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
        public bool? IsDefault { get; set; }
    }

    public class MediaInfoModel : AudioStreamInfo, IEmbeddedDocument
    {
        public string ContainerFormat { get; set; }
        public string Source { get; set; }
        public string SourceVersion { get; set; }
        public List<AudioStreamInfo> AudioStreams { get; set; } = new List<AudioStreamInfo>();

        public void SetPrimaryStream(AudioStreamInfo stream)
        {
            Index = stream.Index;
            AudioFormat = stream.AudioFormat;
            AudioProfile = stream.AudioProfile;
            AudioAdditionalFeatures = stream.AudioAdditionalFeatures;
            AudioCodecId = stream.AudioCodecId;
            AudioCommercialName = stream.AudioCommercialName;
            AudioBitrate = stream.AudioBitrate;
            AudioBitrateMode = stream.AudioBitrateMode;
            AudioChannels = stream.AudioChannels;
            AudioChannelLayout = stream.AudioChannelLayout;
            AudioBits = stream.AudioBits;
            AudioSampleRate = stream.AudioSampleRate;
            DurationSeconds = stream.DurationSeconds;
            Language = stream.Language;
            Title = stream.Title;
            IsDefault = stream.IsDefault;
        }
    }
}
