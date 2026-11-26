using FluentValidation.Internal;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.Validation
{
    public class DownloadClientExistsValidator : ReusablePropertyValidator<int>
    {
        private readonly IDownloadClientFactory _downloadClientFactory;

        public DownloadClientExistsValidator(IDownloadClientFactory downloadClientFactory)
        {
            _downloadClientFactory = downloadClientFactory;
        }

        protected override string GetDefaultMessageTemplate() => "Download Client does not exist";

        protected override bool IsValid(int value, MessageFormatter formatter)
        {
            if (value == 0)
            {
                return true;
            }

            return _downloadClientFactory.Exists(value);
        }
    }
}
