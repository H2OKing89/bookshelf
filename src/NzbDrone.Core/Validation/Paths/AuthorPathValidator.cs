using System;
using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Validation.Paths
{
    public class AuthorPathValidator
    {
        private readonly IAuthorService _authorService;

        public AuthorPathValidator(IAuthorService authorService)
        {
            _authorService = authorService;
        }

        public IPropertyValidator<T, string> For<T>(Func<T, int> getId)
        {
            return new Validator<T>(_authorService, getId);
        }

        private sealed class Validator<T> : PropertyValidator<T, string>
        {
            private readonly IAuthorService _authorService;
            private readonly Func<T, int> _getId;

            public Validator(IAuthorService authorService, Func<T, int> getId)
            {
                _authorService = authorService;
                _getId = getId;
            }

            public override string Name => nameof(AuthorPathValidator);

            protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is already configured for another author";

            public override bool IsValid(ValidationContext<T> context, string value)
            {
                if (value == null)
                {
                    return true;
                }

                context.MessageFormatter.AppendArgument("path", value);
                var instanceId = _getId(context.InstanceToValidate);

                return !_authorService.AllAuthorPaths().Any(s => s.Value.PathEquals(value) && s.Key != instanceId);
            }
        }
    }
}
