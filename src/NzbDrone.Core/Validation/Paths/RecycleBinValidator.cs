using FluentValidation.Internal;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Validation.Paths
{
    public class RecycleBinValidator : ReusablePropertyValidator<string>
    {
        private readonly IConfigService _configService;

        public RecycleBinValidator(IConfigService configService)
        {
            _configService = configService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is {relationship} configured recycle bin folder";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            var recycleBin = _configService.RecycleBin;

            if (value == null || recycleBin.IsNullOrWhiteSpace())
            {
                return true;
            }

            var folder = value;
            formatter.AppendArgument("path", folder);

            if (recycleBin.PathEquals(folder))
            {
                formatter.AppendArgument("relationship", "set to");

                return false;
            }

            if (recycleBin.IsParentPath(folder))
            {
                formatter.AppendArgument("relationship", "child of");

                return false;
            }

            return true;
        }
    }
}
