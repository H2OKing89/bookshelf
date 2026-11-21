using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class LanguageSpecificationValidator : AbstractValidator<LanguageSpecification>
    {
        public LanguageSpecificationValidator()
        {
            RuleFor(condition => condition.Value).Must(LanguageSpecification.IsSupported)
                .WithMessage("Choose a supported language.");
            RuleFor(condition => condition.ExceptLanguage)
                .Must((condition, except) => !except || condition.Value != Language.Unknown.Id)
                .WithMessage("Except Language requires a known language or Selected edition language.");
        }
    }

    public class LanguageSpecification : CustomFormatSpecificationBase
    {
        public const int SelectedEdition = -2;

        private static readonly LanguageSpecificationValidator Validator = new ();

        public override int Order => 3;
        public override string ImplementationName => "Language";

        [FieldDefinition(0, Label = "CustomFormatsSpecificationLanguage", HelpText = "CustomFormatsSpecificationLanguageHelpText", Type = FieldType.Select, SelectOptions = typeof(RecordingLanguageOptions))]
        public int Value { get; set; } = Language.English.Id;

        [FieldDefinition(1, Label = "CustomFormatsSpecificationExceptLanguage", HelpText = "CustomFormatsSpecificationExceptLanguageHelpText", Type = FieldType.Checkbox)]
        public bool ExceptLanguage { get; set; }

        public override bool IsSatisfiedBy(CustomFormatInput input)
        {
            if (!IsSupported(Value))
            {
                return false;
            }

            var languages = input.Languages ?? Array.Empty<Language>();
            var hasKnown = languages.Any(language => language != null && RecordingLanguageEvidence.IsKnown(language.Id));
            if (Value == Language.Unknown.Id)
            {
                return !ExceptLanguage && Negate == hasKnown;
            }

            var comparedLanguage = Value == SelectedEdition ? input.SelectedEditionLanguageId : Value;
            if (!hasKnown || !RecordingLanguageEvidence.IsKnown(comparedLanguage))
            {
                return false;
            }

            var match = IsSatisfiedByWithoutNegate(input);
            return Negate ? !match && languages.All(language => language != null && RecordingLanguageEvidence.IsKnown(language.Id)) : match;
        }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            var comparedLanguage = Value == SelectedEdition ? input.SelectedEditionLanguageId : Value;
            return input.Languages.Any(language => language != null && RecordingLanguageEvidence.IsKnown(language.Id) &&
                (ExceptLanguage ? language.Id != comparedLanguage : language.Id == comparedLanguage));
        }

        public override NzbDroneValidationResult Validate() => new NzbDroneValidationResult(Validator.Validate(this));

        internal static bool IsSupported(int id) => id == SelectedEdition || id == Language.Unknown.Id || RecordingLanguageEvidence.IsKnown(id);
    }

    public class RecordingLanguageOptions : ISelectOptionsConverter
    {
        public List<SelectOption> GetSelectOptions()
        {
            var options = Language.All.Where(language => LanguageSpecification.IsSupported(language.Id) && language.Id >= 0)
                .Select(language => new SelectOption { Value = language.Id, Name = language.Name, Order = language.Id }).ToList();
            options.Insert(0, new SelectOption { Value = LanguageSpecification.SelectedEdition, Name = "Selected edition language", Order = -1 });
            return options;
        }
    }
}
