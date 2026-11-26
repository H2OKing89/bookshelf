using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Profiles.Delay
{
    public class DelayProfileTagInUseValidator
    {
        private readonly IDelayProfileService _delayProfileService;

        public DelayProfileTagInUseValidator(IDelayProfileService delayProfileService)
        {
            _delayProfileService = delayProfileService;
        }

        public IPropertyValidator<T, HashSet<int>> For<T>(Func<T, int> getId)
        {
            return new Validator<T>(_delayProfileService, getId);
        }

        private sealed class Validator<T> : PropertyValidator<T, HashSet<int>>
        {
            private readonly IDelayProfileService _delayProfileService;
            private readonly Func<T, int> _getId;

            public Validator(IDelayProfileService delayProfileService, Func<T, int> getId)
            {
                _delayProfileService = delayProfileService;
                _getId = getId;
            }

            public override string Name => nameof(DelayProfileTagInUseValidator);

            protected override string GetDefaultMessageTemplate(string errorCode) => "One or more tags is used in another profile";

            public override bool IsValid(ValidationContext<T> context, HashSet<int> value)
            {
                if (value == null || value.Empty())
                {
                    return true;
                }

                var instanceId = _getId(context.InstanceToValidate);

                return _delayProfileService.All().None(d => d.Id != instanceId && d.Tags.Intersect(value).Any());
            }
        }
    }
}
