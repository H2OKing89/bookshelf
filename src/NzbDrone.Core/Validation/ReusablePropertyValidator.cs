using System;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;

namespace NzbDrone.Core.Validation
{
    // Domain validators are injected once and used with several resource/model types.
    // Attach a typed FluentValidation component per rule, leaving rule options (including
    // severity and custom messages) on that rule rather than on the domain service.
    public abstract class ReusablePropertyValidator<TProperty>
    {
        protected abstract string GetDefaultMessageTemplate();

        protected abstract bool IsValid(TProperty value, MessageFormatter formatter);

        public IPropertyValidator<TModel, TProperty> For<TModel>()
        {
            return new AttachedValidator<TModel>(GetType().Name, GetDefaultMessageTemplate(), IsValid);
        }

        private sealed class AttachedValidator<TModel> : PropertyValidator<TModel, TProperty>
        {
            private readonly string _name;
            private readonly string _message;
            private readonly Func<TProperty, MessageFormatter, bool> _validate;

            public AttachedValidator(string name, string message, Func<TProperty, MessageFormatter, bool> validate)
            {
                _name = name;
                _message = message;
                _validate = validate;
            }

            public override string Name => _name;

            public override bool IsValid(ValidationContext<TModel> context, TProperty value)
            {
                return _validate(value, context.MessageFormatter);
            }

            protected override string GetDefaultMessageTemplate(string errorCode) => _message;
        }
    }

    public static class ReusablePropertyValidatorExtensions
    {
        public static IRuleBuilderOptions<TModel, TProperty> SetValidator<TModel, TProperty>(this IRuleBuilder<TModel, TProperty> rule, ReusablePropertyValidator<TProperty> validator)
        {
            return rule.SetValidator(validator.For<TModel>());
        }
    }
}
