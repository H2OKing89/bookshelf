using System.Linq;
using FluentValidation.Internal;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class RootFolderAncestorValidator : ReusablePropertyValidator<string>
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderAncestorValidator(IRootFolderService rootFolderService)
        {
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is an ancestor of an existing root folder";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            formatter.AppendArgument("path", value);

            return !_rootFolderService.All().Any(s => value.IsParentPath(s.Path));
        }
    }
}
