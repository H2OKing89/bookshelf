using System;
using FluentValidation.Internal;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Validation.Paths
{
    public class FolderWritableValidator : ReusablePropertyValidator<string>
    {
        private readonly IDiskProvider _diskProvider;

        public FolderWritableValidator(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        protected override string GetDefaultMessageTemplate() => "Folder '{path}' is not writable by user '{user}'";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return false;
            }

            formatter.AppendArgument("path", value);
            formatter.AppendArgument("user", Environment.UserName);

            return _diskProvider.FolderWritable(value);
        }
    }
}
