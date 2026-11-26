using FluentValidation.Internal;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Validation.Paths
{
    public class PathExistsValidator : ReusablePropertyValidator<string>
    {
        private readonly IDiskProvider _diskProvider;

        public PathExistsValidator(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' does not exist";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return false;
            }

            formatter.AppendArgument("path", value);

            return _diskProvider.FolderExists(value);
        }
    }
}
