using FluentValidation;
using FluentValidation.Internal;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Validation.Paths
{
    public static class PathValidation
    {
        public static IRuleBuilderOptions<T, string> IsValidPath<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new PathValidator());
        }
    }

    public class PathValidator : ReusablePropertyValidator<string>
    {
        protected override string GetDefaultMessageTemplate() => "Invalid Path: '{path}'";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return false;
            }

            formatter.AppendArgument("path", value);

            return value.IsPathValid(PathValidationType.CurrentOs);
        }
    }
}
