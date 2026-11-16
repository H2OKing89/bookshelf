using System.IO;
using System.Linq;
using System.Reflection;
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

            SharedValidator.RuleFor(c => c.UrlBase).ValidUrlBase();
            SharedValidator.RuleFor(c => c.InstanceName).ContainsReadarr().When(c => c.InstanceName.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c.Username).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Basic ||
                                                                          c.AuthenticationMethod == AuthenticationType.Forms);
            SharedValidator.RuleFor(c => c.Password).NotEmpty()
                .When(c => (c.AuthenticationMethod == AuthenticationType.Basic || c.AuthenticationMethod == AuthenticationType.Forms) &&
                           (c.Password != null || _userService.FindUser() == null));

            SharedValidator.RuleFor(c => c.Password).NotEqual(PrivateValue)
                .When(c => _userService.FindUser() == null);

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
            X509Certificate2 cert;
            try
            {
                cert = new X509Certificate2(resource.SslCertPath, resource.SslCertPassword, X509KeyStorageFlags.DefaultKeySet);
            }
            catch
            {
                return false;
            }

            return cert != null;
        }

        private bool IsMatchingPassword(HostConfigResource resource)
        {
            var user = _userService.FindUser();

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
        public HostConfigResource GetHostConfig()
        {
            var resource = HostConfigResourceMapper.ToResource(_configFileProvider, _configService);
            resource.Id = 1;

            var user = _userService.FindUser();

            resource.Username = user?.Username ?? string.Empty;
            resource.Password = user?.Password.IsNotNullOrWhiteSpace() == true ? PrivateValue : string.Empty;
            resource.PasswordConfirmation = string.Empty;

            return resource;
        }

        [RestPutById]
        public ActionResult<HostConfigResource> SaveHostConfig(HostConfigResource resource)
        {
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

            var unchangedPassword = IsUnchangedPassword(resource.Password, _userService.FindUser());
            if (!unchangedPassword && resource.Password.IsNotNullOrWhiteSpace() && resource.Password != PrivateValue &&
                resource.Password != resource.PasswordConfirmation)
            {
                throw new ValidationException(new[] { new ValidationFailure(nameof(resource.PasswordConfirmation), "Must match Password") });
            }

            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .Where(prop => prop.Name != nameof(HostConfigResource.Password) &&
                                                    prop.Name != nameof(HostConfigResource.PasswordConfirmation) &&
                                                    (prop.Name != nameof(HostConfigResource.TrustedNetworks) || resource.TrustedNetworks != null))
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            if (resource.Username.IsNotNullOrWhiteSpace())
            {
                if (unchangedPassword)
                {
                    _userService.Upsert(resource.Username, null);
                }
                else if (resource.Password.IsNotNullOrWhiteSpace() && resource.Password != PrivateValue)
                {
                    _userService.Upsert(resource.Username, resource.Password);
                }
            }

            return Accepted(resource.Id);
        }
    }
}
