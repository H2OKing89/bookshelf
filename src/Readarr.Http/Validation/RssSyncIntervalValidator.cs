using FluentValidation.Internal;
using NzbDrone.Core.Validation;

namespace Readarr.Http.Validation
{
    public class RssSyncIntervalValidator : ReusablePropertyValidator<int>
    {
        protected override string GetDefaultMessageTemplate() => "Must be between 10 and 120 or 0 to disable";

        protected override bool IsValid(int value, MessageFormatter formatter)
        {
            if (value == 0)
            {
                return true;
            }

            return value is >= 10 and <= 120;
        }
    }
}
