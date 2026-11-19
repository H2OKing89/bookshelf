using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Network;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/host")]
    [HostAccountAuthentication]
    public class HostConfigController : RestController<HostConfigResource>
    {
        private const string PrivateValue = "********";

        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigService _configService;
        private readonly IUserService _userService;

        public HostConfigController(IConfigFileProvider configFileProvider,
                                    IConfigService configService,
                                    IUserService userService,
                                    FileExistsValidator fileExistsValidator)
        {
            _configFileProvider = configFileProvider;
            _configService = configService;
            _userService = userService;

            SharedValidator.RuleFor(c => c.BindAddress)
                           .ValidIpAddress()
                           .When(c => c.BindAddress != "*" && c.BindAddress != "localhost");

            SharedValidator.RuleFor(c => c.Port).ValidPort();

            SharedValidator.RuleFor(c => c.TrustedNetworks)
                .Must(IPNetworkParser.IsValidList)
                .WithMessage("Enter comma-separated IP addresses or CIDR networks, such as 192.0.2.10 or 192.0.2.0/24")
                .When(c => c.TrustedNetworks != null);

            SharedValidator.RuleFor(c => c.AllowedHosts)
                .Must(AllowedHostsParser.IsValidList)
                .WithMessage("Enter host names or IP addresses without ports, schemes, paths or bind addresses. Leave empty to allow any host.")
                .When(c => c.AllowedHosts != null);

            SharedValidator.RuleFor(c => c.UrlBase).ValidUrlBase();
            SharedValidator.RuleFor(c => c.InstanceName).ContainsReadarr().When(c => c.InstanceName.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c).Custom((resource, context) =>
            {
                try
                {
                    ValidateAccountSelection(resource);
                }
                catch (ValidationException error)
                {
                    foreach (var failure in error.Errors)
                    {
                        context.AddFailure(failure);
                    }
                }
            });

            SharedValidator.RuleFor(c => c.Username).NotEmpty().When(c =>
                (c.AuthenticationMethod == AuthenticationType.Basic || c.AuthenticationMethod == AuthenticationType.Forms) &&
                !IsUnselectedGeneralSave(c));
            SharedValidator.RuleFor(c => c.Password).NotEmpty()
                .When(c => !IsUnselectedGeneralSave(c) &&
                           (c.AuthenticationMethod == AuthenticationType.Basic || c.AuthenticationMethod == AuthenticationType.Forms) &&
                           (c.Password != null || SelectedAccount(c.UserIdentifier) == null));

            SharedValidator.RuleFor(c => c.Password).NotEqual(PrivateValue)
                .When(c => !IsUnselectedGeneralSave(c) && SelectedAccount(c.UserIdentifier) == null);

            SharedValidator.RuleFor(c => c.PasswordConfirmation)
                .Must((resource, p) => IsMatchingPassword(resource)).WithMessage("Must match Password");

            SharedValidator.RuleFor(c => c.SslPort).ValidPort().When(c => c.EnableSsl);
            SharedValidator.RuleFor(c => c.SslPort).NotEqual(c => c.Port).When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.SslCertPath)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath()
                .SetValidator(fileExistsValidator)
                .Must((resource, path) => IsValidSslCertificate(resource)).WithMessage("Invalid SSL certificate file or password")
                .When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.Branch).NotEmpty().WithMessage("Branch name is required, 'master' is the default");
            SharedValidator.RuleFor(c => c.UpdateScriptPath).IsValidPath().When(c => c.UpdateMechanism == UpdateMechanism.Script);

            SharedValidator.RuleFor(c => c.BackupFolder).IsValidPath().When(c => Path.IsPathRooted(c.BackupFolder));
            SharedValidator.RuleFor(c => c.BackupInterval).InclusiveBetween(1, 7);
            SharedValidator.RuleFor(c => c.BackupRetention).InclusiveBetween(1, 90);
        }

        private bool IsValidSslCertificate(HostConfigResource resource)
        {
            try
            {
                using var certificate = X509CertificateLoader.LoadPkcs12FromFile(resource.SslCertPath, resource.SslCertPassword, X509KeyStorageFlags.DefaultKeySet);
                return certificate != null;
            }
            catch
            {
                return false;
            }
        }

        private bool IsMatchingPassword(HostConfigResource resource)
        {
            if (IsUnselectedGeneralSave(resource))
            {
                return true;
            }

            var user = SelectedAccount(resource.UserIdentifier);

            if (IsUnchangedPassword(resource.Password, user))
            {
                return true;
            }

            return resource.Password != PrivateValue && resource.Password == resource.PasswordConfirmation;
        }

        private static bool IsUnchangedPassword(string password, User user)
        {
            return user != null && (password == null || password == PrivateValue ||
                (user.Password != null && password.Length == user.Password.Length &&
                 CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(user.Password))));
        }

        protected override HostConfigResource GetResourceById(int id)
        {
            return GetHostConfig();
        }

        [HttpGet]
        public HostConfigResource GetHostConfig([FromQuery] Guid? userIdentifier = null)
        {
            if (!userIdentifier.HasValue && HttpContext?.Request.Query.TryGetValue("userIdentifier", out var query) == true)
            {
                if (!Guid.TryParse(query, out var parsed))
                {
                    throw SelectionError("Select a valid account identifier");
                }

                userIdentifier = parsed;
            }

            var resource = HostConfigResourceMapper.ToResource(_configFileProvider, _configService);
            resource.Id = 1;

            var accounts = _userService.GetUsers();
            var user = SelectedAccount(userIdentifier);

            resource.Accounts = accounts.Where(account => AuthenticatedAccountIdentity() == null || account.Identifier == user?.Identifier)
                .Select(account => new HostAccountResource
            {
                Identifier = account.Identifier,
                Username = account.Username,
                HasPassword = account.Password.IsNotNullOrWhiteSpace()
            }).ToList();
            resource.UserIdentifier = user?.Identifier;
            resource.AccountSelectionRequired = user == null && accounts.Count > 1;

            resource.Username = user?.Username ?? string.Empty;
            resource.Password = user?.Password.IsNotNullOrWhiteSpace() == true ? PrivateValue : string.Empty;
            resource.PasswordConfirmation = string.Empty;

            return resource;
        }

        [RestPutById]
        public ActionResult<HostConfigResource> SaveHostConfig(HostConfigResource resource)
        {
            ValidateAccountSelection(resource);
            var selected = SelectedAccount(resource.UserIdentifier);
            if (resource.AllowedHosts != null)
            {
                if (!AllowedHostsParser.IsValidList(resource.AllowedHosts))
                {
                    throw new ValidationException(new[]
                    {
                        new ValidationFailure(nameof(resource.AllowedHosts),
                            "Enter host names or IP addresses without ports, schemes, paths or bind addresses. Leave empty to allow any host.")
                    });
                }

                resource.AllowedHosts = AllowedHostsParser.NormalizeList(resource.AllowedHosts);
            }

            if (resource.TrustedNetworks != null)
            {
                if (!IPNetworkParser.IsValidList(resource.TrustedNetworks))
                {
                    throw new ValidationException(new[]
                    {
                        new ValidationFailure(nameof(resource.TrustedNetworks),
                            "Enter comma-separated IP addresses or CIDR networks, such as 192.0.2.10 or 192.0.2.0/24")
                    });
                }

                resource.TrustedNetworks = IPNetworkParser.NormalizeList(resource.TrustedNetworks);
            }

            var unchangedPassword = IsUnchangedPassword(resource.Password, selected);
            if (!unchangedPassword && resource.Password.IsNotNullOrWhiteSpace() && resource.Password != PrivateValue &&
                resource.Password != resource.PasswordConfirmation)
            {
                throw new ValidationException(new[] { new ValidationFailure(nameof(resource.PasswordConfirmation), "Must match Password") });
            }

            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .Where(prop => prop.Name != nameof(HostConfigResource.Password) &&
                                                    prop.Name != nameof(HostConfigResource.PasswordConfirmation) &&
                                                    prop.Name != nameof(HostConfigResource.UserIdentifier) &&
                                                    prop.Name != nameof(HostConfigResource.Accounts) &&
                                                    prop.Name != nameof(HostConfigResource.AccountSelectionRequired) &&
                                                    (prop.Name != nameof(HostConfigResource.AllowedHosts) || resource.AllowedHosts != null) &&
                                                    (prop.Name != nameof(HostConfigResource.TrustedNetworks) || resource.TrustedNetworks != null))
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            if (resource.Username.IsNotNullOrWhiteSpace())
            {
                if (unchangedPassword)
                {
                    SaveAccount(selected, resource.Username, null);
                }
                else if (resource.Password.IsNotNullOrWhiteSpace() && resource.Password != PrivateValue)
                {
                    SaveAccount(selected, resource.Username, resource.Password);
                }
            }

            return Accepted(resource.Id);
        }

        private void SaveAccount(User selected, string username, string password)
        {
            if (selected != null)
            {
                _userService.Upsert(selected.Identifier, username, password);
            }
            else
            {
                _userService.Upsert(username, password);
            }
        }

        private User SelectedAccount(Guid? requested)
        {
            var authenticatedAccount = AuthenticatedAccountIdentity();
            if (authenticatedAccount != null)
            {
                if (!Guid.TryParse(authenticatedAccount.FindFirst("identifier")?.Value, out var identifier) ||
                    (requested.HasValue && requested.Value != identifier))
                {
                    throw SelectionError("An authenticated account may edit only its own credentials");
                }

                requested = identifier;
            }

            var users = _userService.GetUsers();
            if (requested.HasValue)
            {
                var selected = users.SingleOrDefault(user => user.Identifier == requested.Value);
                if (selected == null)
                {
                    throw SelectionError("The selected account no longer exists; reload settings");
                }

                return selected;
            }

            return users.Count == 1 ? users[0] : null;
        }

        private ClaimsIdentity AuthenticatedAccountIdentity()
        {
            return HttpContext?.User?.Identities.FirstOrDefault(identity => identity.IsAuthenticated &&
                identity.Claims.Any(claim => claim.Type == "AuthType" &&
                    (claim.Value == AuthenticationType.Forms.ToString() || claim.Value == AuthenticationType.Basic.ToString())));
        }

        private bool IsUnselectedGeneralSave(HostConfigResource resource)
        {
            return _userService.GetUsers().Count > 1 && SelectedAccount(resource.UserIdentifier) == null &&
                resource.Username.IsNullOrWhiteSpace() && resource.Password.IsNullOrWhiteSpace() &&
                resource.PasswordConfirmation.IsNullOrWhiteSpace() &&
                resource.AuthenticationMethod == _configFileProvider.AuthenticationMethod;
        }

        private void ValidateAccountSelection(HostConfigResource resource)
        {
            var users = _userService.GetUsers();
            var selected = SelectedAccount(resource.UserIdentifier);
            if (selected == null && users.Count > 1 && !IsUnselectedGeneralSave(resource))
            {
                throw SelectionError("Select an account before changing credentials or authentication mode");
            }

            if (selected != null && resource.Username.IsNotNullOrWhiteSpace() && users.Any(user =>
                user.Identifier != selected.Identifier && string.Equals(user.Username, resource.Username, StringComparison.OrdinalIgnoreCase)))
            {
                throw SelectionError("The requested username belongs to another account");
            }
        }

        private static ValidationException SelectionError(string message)
        {
            return new ValidationException(new[] { new ValidationFailure(nameof(HostConfigResource.UserIdentifier), message) });
        }
    }
}
