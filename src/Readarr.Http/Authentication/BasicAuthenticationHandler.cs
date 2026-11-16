using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;

namespace Readarr.Http.Authentication
{
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IAuthenticationService _authService;

        public BasicAuthenticationHandler(IAuthenticationService authService,
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
            _authService = authService;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization header missing."));
            }

            var authorizationHeader = Request.Headers["Authorization"].ToString();
            if (authorizationHeader.Length > 8192 ||
                !AuthenticationHeaderValue.TryParse(authorizationHeader, out var header) ||
                !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(header.Parameter))
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization code not formatted properly."));
            }

            string credentials;
            try
            {
                credentials = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(header.Parameter));
            }
            catch (Exception e) when (e is FormatException || e is DecoderFallbackException)
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization code not formatted properly."));
            }

            var separator = credentials.IndexOf(':');
            if (separator < 0)
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization code not formatted properly."));
            }

            var authUsername = credentials.Substring(0, separator);
            var authPassword = credentials.Substring(separator + 1);

            var user = _authService.Login(Request, authUsername, authPassword);

            if (user == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("The username or password is not correct."));
            }

            var claims = new List<Claim>
            {
                new Claim("user", user.Username),
                new Claim("identifier", user.Identifier.ToString()),
                new Claim("AuthType", AuthenticationType.Basic.ToString())
            };

            var identity = new ClaimsIdentity(claims, "Basic", "user", "identifier");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Basic");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.Add("WWW-Authenticate", $"Basic realm=\"{BuildInfo.AppName}\"");
            var lockoutEnd = _authService.GetLockoutEndTime(Request);
            if (lockoutEnd.HasValue)
            {
                var seconds = Math.Max(1, Math.Ceiling((lockoutEnd.Value.ToUniversalTime() - Clock.UtcNow.UtcDateTime).TotalSeconds));
                Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
            }

            Response.StatusCode = 401;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            return Task.CompletedTask;
        }
    }
}
