using FluentValidation;
using FluentValidation.Internal;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Validation
{
    public static class UrlValidation
    {
        public static IRuleBuilderOptions<T, string> IsValidUrl<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new UrlValidator());
        }
    }

    public class UrlValidator : ReusablePropertyValidator<string>
    {
        protected override string GetDefaultMessageTemplate() => "Invalid Url: '{url}'";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return false;
            }

            formatter.AppendArgument("url", value);

            return value.IsValidUrl();
        }
    }
}
