using System;
using FluentValidation.Internal;

namespace NzbDrone.Core.Validation
{
    public class GuidValidator : ReusablePropertyValidator<string>
    {
        protected override string GetDefaultMessageTemplate() => "String is not a valid Guid";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return false;
            }

            return Guid.TryParse(value, out _);
        }
    }
}
