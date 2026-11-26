using System;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.CustomJson
{
    public class CustomJsonImportSettingsValidator : AbstractValidator<CustomJsonImportSettings>
    {
        public CustomJsonImportSettingsValidator()
        {
            RuleFor(settings => settings.BaseUrl)
                .Must(IsEndpoint).WithMessage("Enter a full HTTP(S) endpoint URL without credentials, query parameters or fragments.");

            RuleFor(settings => settings.ApiKey)
                .Must(IsBearerToken).WithMessage("Enter an optional bearer token without whitespace or control characters.");
        }

        private static bool IsEndpoint(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048 &&
            !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl) &&
            Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrEmpty(url.Host) && string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);

        private static bool IsBearerToken(string value) => string.IsNullOrEmpty(value) ||
            (value.Length <= 4096 && value.All(character => char.IsAsciiLetterOrDigit(character) || "-._~+/=".Contains(character)));
    }

    public class CustomJsonImportSettings : IImportListSettings
    {
        private static readonly CustomJsonImportSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "Endpoint URL", Type = FieldType.Url, HelpText = "Full URL of the Bookshelf custom JSON list endpoint; use HTTPS for private endpoints. Paging parameters are supplied automatically.")]
        public string BaseUrl { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Bearer Token", Type = FieldType.Password, Privacy = PrivacyLevel.ApiKey, HelpText = "Optional bearer token sent in the Authorization header. Leave empty for public lists.")]
        public string ApiKey { get; set; } = string.Empty;

        public NzbDroneValidationResult Validate() => new(Validator.Validate(this));
    }
}
