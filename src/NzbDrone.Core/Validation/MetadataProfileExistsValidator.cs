using FluentValidation.Internal;
using NzbDrone.Core.Profiles.Metadata;

namespace NzbDrone.Core.Validation
{
    public class MetadataProfileExistsValidator : ReusablePropertyValidator<int>
    {
        private readonly IMetadataProfileService _profileService;

        public MetadataProfileExistsValidator(IMetadataProfileService profileService)
        {
            _profileService = profileService;
        }

        protected override string GetDefaultMessageTemplate() => "Metadata profile does not exist";

        protected override bool IsValid(int value, MessageFormatter formatter)
        {
            if (value == 0)
            {
                return true;
            }

            return _profileService.Exists(value);
        }
    }
}
