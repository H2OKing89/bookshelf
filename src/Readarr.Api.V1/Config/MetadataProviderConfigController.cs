using FluentValidation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Validation;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/metadataprovider")]
    public class MetadataProviderConfigController : ConfigController<MetadataProviderConfigResource>
    {
        public MetadataProviderConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.AudnexusUrl).IsValidUrl();
            SharedValidator.RuleFor(c => c.AudnexusRegion)
                .Must(region => region != null && AudnexusProxy.Regions.Contains(region))
                .WithMessage("Select a supported Audible marketplace.");
            SharedValidator.RuleFor(c => c.AudibleRsUrl).IsValidUrl().When(c => c.AudibleRsEnabled);
            SharedValidator.RuleFor(c => c.AudibleRsTokenFile).NotEmpty().When(c => c.AudibleRsEnabled);
        }

        protected override MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return MetadataProviderConfigResourceMapper.ToResource(model);
        }
    }
}
