using FluentValidation.Internal;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Validation
{
    public class FolderChmodValidator : ReusablePropertyValidator<string>
    {
        private readonly IDiskProvider _diskProvider;

        public FolderChmodValidator(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        protected override string GetDefaultMessageTemplate() => "Must contain a valid Unix permissions octal";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return false;
            }

            return _diskProvider.IsValidFolderPermissionMask(value);
        }
    }
}
