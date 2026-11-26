using System.Linq;
using FluentValidation.Internal;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Validation.Paths
{
    public class AuthorAncestorValidator : ReusablePropertyValidator<string>
    {
        private readonly IAuthorService _authorService;

        public AuthorAncestorValidator(IAuthorService authorService)
        {
            _authorService = authorService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is an ancestor of an existing author";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            formatter.AppendArgument("path", value);

            return !_authorService.AllAuthorPaths().Any(s => value.IsParentPath(s.Value));
        }
    }
}
