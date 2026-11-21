using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.Test.CustomFormats
{
    [TestFixture]
    public class RecordingLanguageSpecificationFixture
    {
        [TestCase("English", 1, false, false, true)]
        [TestCase("English", 1, false, true, false)]
        [TestCase("French", 1, false, true, true)]
        [TestCase("English, French", 1, true, false, true)]
        [TestCase("English, French", 1, true, true, false)]
        [TestCase("English", 1, true, true, true)]
        [TestCase("English, und", 1, true, true, false)]
        [TestCase("French, unrecognized", 1, false, true, false)]
        [TestCase("English, und", 1, false, false, true)]
        [TestCase(null, 1, false, true, false)]
        [TestCase("und", 1, true, false, false)]
        [TestCase(null, 0, false, false, true)]
        [TestCase("und", 0, false, false, true)]
        [TestCase("English, und", 0, false, false, false)]
        [TestCase("English", 0, false, true, true)]
        [TestCase("English", -1, false, true, false)]
        public void should_evaluate_language_conditions_conservatively(string evidence, int value, bool except, bool negate, bool expected)
        {
            var condition = new LanguageSpecification { Value = value, ExceptLanguage = except, Negate = negate };
            var input = new CustomFormatInput { Languages = RecordingLanguageEvidence.Parse(evidence) };

            condition.IsSatisfiedBy(input).Should().Be(expected);
        }

        [TestCase(1, false, true)]
        [TestCase(2, false, false)]
        [TestCase(2, true, true)]
        [TestCase(0, false, false)]
        [TestCase(0, true, false)]
        public void selected_edition_condition_should_require_known_comparison_language(int selectedLanguageId, bool negate, bool expected)
        {
            var condition = new LanguageSpecification { Value = LanguageSpecification.SelectedEdition, Negate = negate };
            var input = new CustomFormatInput
            {
                Languages = new[] { Language.English },
                SelectedEditionLanguageId = selectedLanguageId
            };

            condition.IsSatisfiedBy(input).Should().Be(expected);
        }

        [TestCase(0, false, true)]
        [TestCase(0, true, false)]
        [TestCase(-1, false, false)]
        [TestCase(-2, true, true)]
        [TestCase(999, false, false)]
        public void should_reject_invalid_or_meaningless_conditions(int value, bool except, bool expected)
        {
            var condition = new LanguageSpecification { Value = value, ExceptLanguage = except };

            condition.Validate().IsValid.Should().Be(expected);
        }
    }
}
