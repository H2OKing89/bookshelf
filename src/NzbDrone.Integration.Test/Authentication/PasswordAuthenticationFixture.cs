using System;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;
using AspNetAuthenticationService = Microsoft.AspNetCore.Authentication.IAuthenticationService;
using ReadarrAuthenticationService = Readarr.Http.Authentication.AuthenticationService;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class PasswordAuthenticationFixture : TestBase
    {
        private const string Password = "correct: horse battery staple";
        private static readonly Guid Identifier = Guid.Parse("9c9cd5d2-3b45-44c2-9c4f-3d6ac9bfbab8");
        private User _storedUser;

        [TestCase(true)]
        [TestCase(false)]
        public async Task forms_should_sign_in_legacy_and_current_passwords(bool legacy)
        {
            var user = GivenUser(legacy);
            var originalPassword = user.Password;
            var context = CreateContext();
            var cookies = UseCookieAuthentication(context);
            ClaimsPrincipal principal = null;
            cookies.Setup(service => service.SignInAsync(context, "Forms", It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((httpContext, scheme, signedInPrincipal, properties) => principal = signedInPrincipal)
                .Returns(Task.CompletedTask);
            var controller = CreateFormsController(context);

            var result = await controller.Login(new LoginResource { Username = "Reader", Password = Password, RememberMe = "on" });

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().Be("/bookshelf/");
            cookies.Verify(
                service => service.SignInAsync(
                    context,
                    "Forms",
                    It.IsAny<ClaimsPrincipal>(),
                    It.Is<AuthenticationProperties>(properties => properties.IsPersistent)),
                Times.Once());
            VerifyPrincipal(principal, AuthenticationType.Forms, "Cookies");
            VerifySuccessfulPassword(user, legacy, originalPassword);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task forms_should_not_sign_in_or_change_credentials_for_wrong_password(bool legacy)
        {
            var user = GivenUser(legacy);
            var original = Snapshot(user);
            var context = CreateContext();
            var cookies = UseCookieAuthentication(context);
            var controller = CreateFormsController(context);

            var result = await controller.Login(new LoginResource { Username = "Reader", Password = "wrong password" }, "/books");

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().Be("~/login?returnUrl=%2Fbooks&loginFailed=true");
            cookies.Verify(
                service => service.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties>()),
                Times.Never());
            user.Should().BeEquivalentTo(original);
            _storedUser.Should().BeEquivalentTo(original);
            Mocker.GetMock<IUserRepository>().Verify(repository => repository.TryUpdateCredentials(
                It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IUserRepository>().Verify(repository => repository.Update(It.IsAny<User>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task basic_should_authenticate_legacy_and_current_passwords(bool legacy)
        {
            var user = GivenUser(legacy);
            var originalPassword = user.Password;

            var result = await AuthenticateBasic(Password);

            result.Succeeded.Should().BeTrue();
            result.Ticket.AuthenticationScheme.Should().Be("Basic");
            VerifyPrincipal(result.Principal, AuthenticationType.Basic, "Basic");
            VerifySuccessfulPassword(user, legacy, originalPassword);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task basic_should_not_authenticate_or_change_credentials_for_wrong_password(bool legacy)
        {
            var user = GivenUser(legacy);
            var original = Snapshot(user);

            var result = await AuthenticateBasic("wrong password");

            result.Succeeded.Should().BeFalse();
            result.Failure.Should().NotBeNull();
            result.Principal.Should().BeNull();
            user.Should().BeEquivalentTo(original);
            _storedUser.Should().BeEquivalentTo(original);
            Mocker.GetMock<IUserRepository>().Verify(repository => repository.TryUpdateCredentials(
                It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IUserRepository>().Verify(repository => repository.Update(It.IsAny<User>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        private User GivenUser(bool legacy)
        {
            var user = new User { Id = 42, Identifier = Identifier, Username = "reader" };

            if (legacy)
            {
                user.Password = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password))).ToLowerInvariant();
            }
            else
            {
                user.Salt = "AAECAwQFBgcICQoLDA0ODw==";
                user.Iterations = 220000;
                user.Password = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                    Password,
                    Convert.FromBase64String(user.Salt),
                    user.Iterations,
                    HashAlgorithmName.SHA512,
                    32));
            }

            _storedUser = Snapshot(user);
            Mocker.GetMock<IUserRepository>().Setup(repository => repository.FindUser("reader")).Returns(() => Snapshot(_storedUser));
            Mocker.GetMock<IUserRepository>().Setup(repository => repository.TryUpdateCredentials(
                    It.IsAny<User>(), It.IsAny<User>(), false, true))
                .Callback<User, User, bool, bool>((original, updated, updateUsername, updatePassword) => _storedUser = Snapshot(updated))
                .Returns(true);

            return user;
        }

        private ReadarrAuthenticationService CreateAuthenticationService(AuthenticationType method)
        {
            var config = Mocker.GetMock<IConfigFileProvider>();
            config.SetupGet(provider => provider.AuthenticationMethod).Returns(method);
            var users = new UserService(
                Mocker.GetMock<IUserRepository>().Object,
                Mocker.GetMock<IAppFolderInfo>().Object,
                Mocker.GetMock<IDiskProvider>().Object);

            return new ReadarrAuthenticationService(config.Object, users, new LoginLockoutPolicy(TimeProvider.System));
        }

        private AuthenticationController CreateFormsController(HttpContext context)
        {
            var authentication = CreateAuthenticationService(AuthenticationType.Forms);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(provider => provider.UrlBase).Returns("/bookshelf");

            return new AuthenticationController(
                authentication,
                Mocker.GetMock<IConfigFileProvider>().Object,
                Mocker.GetMock<IAppFolderInfo>().Object,
                TestLogger)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
        }

        private async Task<AuthenticateResult> AuthenticateBasic(string password)
        {
            var context = CreateContext();
            context.Request.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("Reader:" + password));
            var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
            options.Setup(monitor => monitor.Get("Basic")).Returns(new AuthenticationSchemeOptions());
            var handler = new BasicAuthenticationHandler(
                CreateAuthenticationService(AuthenticationType.Basic),
                options.Object,
                NullLoggerFactory.Instance,
                UrlEncoder.Default);
            await handler.InitializeAsync(new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler)), context);

            return await handler.AuthenticateAsync();
        }

        private void VerifySuccessfulPassword(User user, bool legacy, string originalPassword)
        {
            _storedUser.Id.Should().Be(42);
            _storedUser.Identifier.Should().Be(Identifier);
            _storedUser.Username.Should().Be("reader");
            _storedUser.Iterations.Should().Be(220000);
            Convert.FromBase64String(_storedUser.Salt).Should().HaveCount(16);
            Mocker.GetMock<IUserRepository>().Verify(repository => repository.TryUpdateCredentials(
                It.IsAny<User>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>()), legacy ? Times.Once() : Times.Never());
            Mocker.GetMock<IUserRepository>().Verify(repository => repository.Update(It.IsAny<User>()), Times.Never());
            user.Password.Should().Be(originalPassword);

            if (legacy)
            {
                _storedUser.Password.Should().NotBe(originalPassword);
            }
            else
            {
                _storedUser.Should().BeEquivalentTo(user);
            }
        }

        private static void VerifyPrincipal(ClaimsPrincipal principal, AuthenticationType method, string identityType)
        {
            principal.Should().NotBeNull();
            principal.Identity.IsAuthenticated.Should().BeTrue();
            principal.Identity.AuthenticationType.Should().Be(identityType);
            principal.Identity.Name.Should().Be("reader");
            principal.FindFirst("identifier").Value.Should().Be(Identifier.ToString());
            principal.FindFirst("AuthType").Value.Should().Be(method.ToString());
        }

        private static DefaultHttpContext CreateContext()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            return context;
        }

        private static Mock<AspNetAuthenticationService> UseCookieAuthentication(HttpContext context)
        {
            var cookies = new Mock<AspNetAuthenticationService>();
            var services = new Mock<IServiceProvider>();
            services.Setup(provider => provider.GetService(typeof(AspNetAuthenticationService))).Returns(cookies.Object);
            context.RequestServices = services.Object;
            return cookies;
        }

        private static User Snapshot(User user)
        {
            return new User
            {
                Id = user.Id,
                Identifier = user.Identifier,
                Username = user.Username,
                Password = user.Password,
                Salt = user.Salt,
                Iterations = user.Iterations
            };
        }
    }
}
