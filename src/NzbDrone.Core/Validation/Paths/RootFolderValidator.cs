using FluentValidation.Internal;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class RootFolderValidator : ReusablePropertyValidator<string>
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderValidator(IRootFolderService rootFolderService)
        {
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is already configured as a root folder";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            formatter.AppendArgument("path", value);

            return !_rootFolderService.All().Exists(r => r.Path.PathEquals(value));
        }
    }
}
