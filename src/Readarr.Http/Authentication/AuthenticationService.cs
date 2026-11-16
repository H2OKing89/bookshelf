using System;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Readarr.Http.Extensions;

namespace Readarr.Http.Authentication
{
    public interface IAuthenticationService
    {
        void LogUnauthorized(HttpRequest context);
        User Login(HttpRequest request, string username, string password);
        DateTime? GetLockoutEndTime(HttpRequest request);
        void Logout(HttpContext context);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private static readonly Logger _authLogger = LogManager.GetLogger("Auth");
        private readonly IUserService _userService;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly ILoginLockoutPolicy _loginLockoutPolicy;

        public AuthenticationService(IConfigFileProvider configFileProvider, IUserService userService, ILoginLockoutPolicy loginLockoutPolicy)
        {
            _userService = userService;
            _configFileProvider = configFileProvider;
            _loginLockoutPolicy = loginLockoutPolicy;
        }

        public User Login(HttpRequest request, string username, string password)
        {
            var method = _configFileProvider.AuthenticationMethod;
            if (method == AuthenticationType.None)
            {
                return null;
            }

            User user;
            if (method == AuthenticationType.Forms || method == AuthenticationType.Basic)
            {
                var attempt = _loginLockoutPolicy.Authenticate(request.GetRemoteIP(), () => _userService.FindUser(username, password));
                if (attempt.IsBlocked)
                {
                    _authLogger.Warn("Auth-Lockout ip {0} username '{1}'", request.GetRemoteIP(), username);
                    return null;
                }

                user = attempt.User;
            }
            else
            {
                user = _userService.FindUser(username, password);
            }

            if (user != null)
            {
                LogSuccess(request, username);

                return user;
            }

            LogFailure(request, username);

            return null;
        }

        public DateTime? GetLockoutEndTime(HttpRequest request)
        {
            var method = _configFileProvider.AuthenticationMethod;
            return method == AuthenticationType.Forms || method == AuthenticationType.Basic
                ? _loginLockoutPolicy.GetLockoutEndTime(request.GetRemoteIP())
                : null;
        }

        public void Logout(HttpContext context)
        {
            if (_configFileProvider.AuthenticationMethod == AuthenticationType.None)
            {
                return;
            }

            if (context.User != null)
            {
                LogLogout(context.Request, context.User.Identity.Name);
            }
        }

        public void LogUnauthorized(HttpRequest context)
        {
            _authLogger.Info("Auth-Unauthorized ip {0} url '{1}'", context.GetRemoteIP(), context.Path);
        }

        private void LogInvalidated(HttpRequest context)
        {
            _authLogger.Info("Auth-Invalidated ip {0}", context.GetRemoteIP());
        }

        private void LogFailure(HttpRequest context, string username)
        {
            _authLogger.Warn("Auth-Failure ip {0} username '{1}'", context.GetRemoteIP(), username);
        }

        private void LogSuccess(HttpRequest context, string username)
        {
            _authLogger.Info("Auth-Success ip {0} username '{1}'", context.GetRemoteIP(), username);
        }

        private void LogLogout(HttpRequest context, string username)
        {
            _authLogger.Info("Auth-Logout ip {0} username '{1}'", context.GetRemoteIP(), username);
        }
    }
}
