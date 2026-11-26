using FluentValidation.Internal;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Validation.Paths
{
    public class SystemFolderValidator : ReusablePropertyValidator<string>
    {
        protected override string GetDefaultMessageTemplate() => "Path '{path}' is {relationship} system folder {systemFolder}";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            var folder = value;
            formatter.AppendArgument("path", folder);

            foreach (var systemFolder in SystemFolders.GetSystemFolders())
            {
                formatter.AppendArgument("systemFolder", systemFolder);

                if (systemFolder.PathEquals(folder))
                {
                    formatter.AppendArgument("relationship", "set to");

                    return false;
                }

                if (systemFolder.IsParentPath(folder))
                {
                    formatter.AppendArgument("relationship", "child of");

                    return false;
                }
            }

            return true;
        }
    }
}
