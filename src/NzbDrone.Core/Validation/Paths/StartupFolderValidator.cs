using FluentValidation.Internal;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Validation.Paths
{
    public class StartupFolderValidator : ReusablePropertyValidator<string>
    {
        private readonly IAppFolderInfo _appFolderInfo;

        public StartupFolderValidator(IAppFolderInfo appFolderInfo)
        {
            _appFolderInfo = appFolderInfo;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' cannot be {relationship} the start up folder";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            var startupFolder = _appFolderInfo.StartUpFolder;
            var folder = value;
            formatter.AppendArgument("path", folder);

            if (startupFolder.PathEquals(folder))
            {
                formatter.AppendArgument("relationship", "set to");

                return false;
            }

            if (startupFolder.IsParentPath(folder))
            {
                formatter.AppendArgument("relationship", "child of");

                return false;
            }

            return true;
        }
    }
}
