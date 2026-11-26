using FluentValidation.Internal;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Validation
{
    public class QualityProfileExistsValidator : ReusablePropertyValidator<int>
    {
        private readonly IQualityProfileService _qualityProfileService;

        public QualityProfileExistsValidator(IQualityProfileService qualityProfileService)
        {
            _qualityProfileService = qualityProfileService;
        }

        protected override string GetDefaultMessageTemplate() => "Quality Profile does not exist";

        protected override bool IsValid(int value, MessageFormatter formatter)
        {
            if (value == 0)
            {
                return true;
            }

            return _qualityProfileService.Exists(value);
        }
    }
}
