using FluentValidation.Internal;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Validation.Paths
{
    public class AuthorExistsValidator : ReusablePropertyValidator<string>
    {
        private readonly IAuthorService _authorService;

        public AuthorExistsValidator(IAuthorService authorService)
        {
            _authorService = authorService;
        }

        protected override string GetDefaultMessageTemplate() => "This author has already been added";

        protected override bool IsValid(string value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            var foreignAuthorId = value;

            return _authorService.FindById(foreignAuthorId) == null;
        }
    }
}
