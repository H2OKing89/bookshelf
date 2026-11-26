using FluentValidation.Internal;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public class ImportListExclusionExistsValidator : ReusablePropertyValidator<string>
    {
        private readonly IImportListExclusionService _importListExclusionService;

        public ImportListExclusionExistsValidator(IImportListExclusionService importListExclusionService)
        {
            _importListExclusionService = importListExclusionService;
        }

        protected override string GetDefaultMessageTemplate() => "This exclusion has already been added.";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            return !_importListExclusionService.All().Exists(s => s.ForeignId == value);
        }
    }
}
