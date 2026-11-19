using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Readarr.Http.Authentication
{
    [AllowAnonymous]
    [ApiController]
    public class AuthenticationController : Controller
    {
        private readonly IAuthenticationService _authService;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public AuthenticationController(IAuthenticationService authService, IConfigFileProvider configFileProvider, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _authService = authService;
            _configFileProvider = configFileProvider;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] LoginResource resource, [FromQuery] string returnUrl = null)
        {
            var user = _authService.Login(HttpContext.Request, resource.Username, resource.Password);

            if (user == null)
            {
                var parameters = new Dictionary<string, string>
                {
                    { "returnUrl", returnUrl ?? string.Empty }
                };
                var lockoutEnd = _authService.GetLockoutEndTime(HttpContext.Request);
                if (lockoutEnd.HasValue)
                {
                    parameters.Add("lockedUntil", lockoutEnd.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                }
                else
                {
                    parameters.Add("loginFailed", "true");
                }

                return Redirect(QueryHelpers.AddQueryString("~/login", parameters));
            }

            var claims = new List<Claim>
            {
                new Claim("user", user.Username),
                new Claim("identifier", user.Identifier.ToString()),
                new Claim("AuthType", AuthenticationType.Forms.ToString()),
                new Claim(UserCredentialStamp.ClaimType, UserCredentialStamp.Create(user))
            };

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = resource.RememberMe == "on"
            };

            try
            {
                await HttpContext.SignInAsync(AuthenticationType.Forms.ToString(), new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", "user", "identifier")), authProperties);
            }
            catch (CryptographicException e)
            {
                if (e.InnerException is XmlException)
                {
                    _logger.Error(e, "Failed to authenticate user due to corrupt XML. Please remove all XML files from {0} and restart Readarr", Path.Combine(_appFolderInfo.AppDataFolder, "asp"));
                }
                else
                {
                    _logger.Error(e, "Failed to authenticate user. {0}", e.Message);
                }

                return Unauthorized();
            }

            return Redirect(GetRedirectUrl(returnUrl));
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            _authService.Logout(HttpContext);
            await HttpContext.SignOutAsync(AuthenticationType.Forms.ToString());
            return Redirect(_configFileProvider.UrlBase + "/");
        }

        private string GetRedirectUrl(string returnUrl)
        {
            var urlBase = _configFileProvider.UrlBase;
            if (returnUrl.IsNullOrWhiteSpace() || !Url.IsLocalUrl(returnUrl))
            {
                return urlBase + "/";
            }

            if (returnUrl.StartsWith("~/", StringComparison.Ordinal))
            {
                return urlBase + returnUrl.Substring(1);
            }

            // Match the path boundary without decoding or changing query/fragment bytes.
            var pathEnd = returnUrl.IndexOfAny(new[] { '?', '#' });
            var path = pathEnd < 0 ? returnUrl : returnUrl.Substring(0, pathEnd);
            if (urlBase.IsNullOrWhiteSpace() ||
                path.Equals(urlBase, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(urlBase + "/", StringComparison.OrdinalIgnoreCase))
            {
                return returnUrl;
            }

            return urlBase + returnUrl;
        }
    }
}
