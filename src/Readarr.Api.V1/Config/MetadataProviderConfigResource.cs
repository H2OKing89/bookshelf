using NzbDrone.Core.Configuration;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    public class MetadataProviderConfigResource : RestResource
    {
        public string AudnexusUrl { get; set; }
        public string AudnexusRegion { get; set; }
        public bool PreferPathAsin { get; set; }
        public bool AudibleRsEnabled { get; set; }
        public string AudibleRsUrl { get; set; }
        public string AudibleRsTokenFile { get; set; }
        public string AudibleRsAccount { get; set; }
        public WriteAudioTagsType WriteAudioTags { get; set; }
        public bool ScrubAudioTags { get; set; }
        public WriteBookTagsType WriteBookTags { get; set; }
        public bool UpdateCovers { get; set; }
        public bool EmbedMetadata { get; set; }
    }

    public static class MetadataProviderConfigResourceMapper
    {
        public static MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return new MetadataProviderConfigResource
            {
                AudnexusUrl = model.AudnexusUrl,
                AudnexusRegion = model.AudnexusRegion,
                PreferPathAsin = model.PreferPathAsin,
                AudibleRsEnabled = model.AudibleRsEnabled,
                AudibleRsUrl = model.AudibleRsUrl,
                AudibleRsTokenFile = model.AudibleRsTokenFile,
                AudibleRsAccount = model.AudibleRsAccount,
                WriteAudioTags = model.WriteAudioTags,
                ScrubAudioTags = model.ScrubAudioTags,
                WriteBookTags = model.WriteBookTags,
                UpdateCovers = model.UpdateCovers,
                EmbedMetadata = model.EmbedMetadata
            };
        }
    }
}
