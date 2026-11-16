using System;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;
using AppAuthenticationService = Readarr.Http.Authentication.IAuthenticationService;
using AspNetAuthenticationService = Microsoft.AspNetCore.Authentication.IAuthenticationService;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class LoginLockoutPresentationFixture : TestBase
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        private Mock<AppAuthenticationService> _authentication;
        private Mock<AspNetAuthenticationService> _cookies;
        private Mock<TimeProvider> _clock;
        private DefaultHttpContext _context;

        [SetUp]
        public void Setup()
        {
            _authentication = new Mock<AppAuthenticationService>();
            _cookies = new Mock<AspNetAuthenticationService>();
            _clock = new Mock<TimeProvider>();
            _clock.Setup(clock => clock.GetUtcNow()).Returns(Now);
            _context = CreateContext();
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.UrlBase).Returns("/bookshelf");
        }

        [TestCase(null)]
        [TestCase("/books?sort=title&loginFailed=false&lockedUntil=forged#fragment")]
        public async Task forms_lockout_should_preserve_return_url_as_one_value_without_signing_in(string returnUrl)
        {
            var end = Now.AddMinutes(5).UtcDateTime;
            _authentication.Setup(service => service.GetLockoutEndTime(_context.Request)).Returns(end);
            var controller = CreateController();

            var result = await controller.Login(new LoginResource { Username = "reader", Password = "incorrect" }, returnUrl);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            var query = QueryHelpers.ParseQuery(redirect.Url.Substring(redirect.Url.IndexOf('?')));
            redirect.Url.Should().StartWith("~/login?");
            query.Keys.Should().BeEquivalentTo("returnUrl", "lockedUntil");
            query["returnUrl"].ToString().Should().Be(returnUrl ?? string.Empty);
            query["lockedUntil"].ToString().Should().Be(end.ToString("O", CultureInfo.InvariantCulture));
            _authentication.Verify(service => service.Login(_context.Request, "reader", "incorrect"), Times.Once());
            VerifyNoSignIn();
        }

        [TestCase(null)]
        [TestCase("/books?lockedUntil=forged&loginFailed=false&name=a+b#fragment")]
        public async Task normal_forms_failure_should_encode_return_url_and_keep_login_failed_feedback(string returnUrl)
        {
            var controller = CreateController();

            var result = await controller.Login(new LoginResource { Username = "reader", Password = "incorrect" }, returnUrl);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            var query = QueryHelpers.ParseQuery(redirect.Url.Substring(redirect.Url.IndexOf('?')));
            query.Keys.Should().BeEquivalentTo("returnUrl", "loginFailed");
            query["returnUrl"].ToString().Should().Be(returnUrl ?? string.Empty);
            query["loginFailed"].ToString().Should().Be("true");
            VerifyNoSignIn();
        }

        [TestCase("on", true, "/books?sort=title")]
        [TestCase(null, false, null)]
        public async Task successful_forms_login_should_keep_identity_persistence_and_safe_redirect(string rememberMe, bool persistent, string returnUrl)
        {
            var user = new User { Id = 17, Identifier = Guid.NewGuid(), Username = "reader" };
            _authentication.Setup(service => service.Login(_context.Request, "reader", "correct")).Returns(user);
            ClaimsPrincipal signedIn = null;
            AuthenticationProperties signedInProperties = null;
            _cookies.Setup(service => service.SignInAsync(_context, "Forms", It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((context, scheme, principal, properties) =>
                {
                    signedIn = principal;
                    signedInProperties = properties;
                })
                .Returns(Task.CompletedTask);
            var controller = CreateController();
            var urls = new Mock<IUrlHelper>();
            urls.Setup(helper => helper.IsLocalUrl(returnUrl)).Returns(true);
            controller.Url = urls.Object;

            var result = await controller.Login(new LoginResource { Username = "reader", Password = "correct", RememberMe = rememberMe }, returnUrl);

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().Be(returnUrl == null ? "/bookshelf/" : "/bookshelf" + returnUrl);
            signedIn.Identity.IsAuthenticated.Should().BeTrue();
            signedIn.Identity.Name.Should().Be(user.Username);
            signedIn.FindFirst("identifier").Value.Should().Be(user.Identifier.ToString());
            signedIn.FindFirst("AuthType").Value.Should().Be("Forms");
            signedInProperties.IsPersistent.Should().Be(persistent);
            _authentication.Verify(service => service.GetLockoutEndTime(It.IsAny<HttpRequest>()), Times.Never());
            _cookies.Verify(service => service.SignInAsync(_context, "Forms", It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Basic")]
        [TestCase("Basic not-base64!")]
        [TestCase("Basic cmVhZGVy")]
        [TestCase("Basic /zo=")]
        [TestCase("Bearer abc")]
        [TestCase("oversized")]
        public async Task malformed_basic_header_should_challenge_without_password_verification(string authorization)
        {
            if (authorization != null)
            {
                _context.Request.Headers["Authorization"] = authorization == "oversized" ? "Basic " + new string('A', 8192) : authorization;
            }

            var handler = await CreateBasicHandler(_context);

            var result = await handler.AuthenticateAsync();
            await handler.ChallengeAsync(new AuthenticationProperties());

            result.Succeeded.Should().BeFalse();
            result.Failure.Should().NotBeNull();
            _context.Response.StatusCode.Should().Be(401);
            _context.Response.Headers["WWW-Authenticate"].ToString().Should().StartWith("Basic realm=\"").And.EndWith("\"");
            _context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
            _authentication.Verify(service => service.Login(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task locked_basic_challenge_should_report_remaining_time_from_injected_clock_without_extending_deadline()
        {
            var end = Now.AddSeconds(90.25).UtcDateTime;
            _authentication.Setup(service => service.GetLockoutEndTime(It.IsAny<HttpRequest>())).Returns(end);
            var first = await CreateBasicHandler(_context);

            await first.ChallengeAsync(new AuthenticationProperties());

            _context.Response.StatusCode.Should().Be(401);
            _context.Response.Headers["Retry-After"].ToString().Should().Be("91");
            _context.Response.Headers["WWW-Authenticate"].ToString().Should().StartWith("Basic realm=\"");
            _clock.Setup(clock => clock.GetUtcNow()).Returns(Now.AddSeconds(30));
            var laterContext = CreateContext();
            var later = await CreateBasicHandler(laterContext);

            await later.ChallengeAsync(new AuthenticationProperties());

            laterContext.Response.StatusCode.Should().Be(401);
            laterContext.Response.Headers["Retry-After"].ToString().Should().Be("61");
            _authentication.Verify(service => service.Login(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task basic_challenge_should_round_a_positive_subsecond_wait_up_to_one_second()
        {
            _authentication.Setup(service => service.GetLockoutEndTime(_context.Request)).Returns(Now.AddMilliseconds(100).UtcDateTime);
            var handler = await CreateBasicHandler(_context);

            await handler.ChallengeAsync(new AuthenticationProperties());

            _context.Response.StatusCode.Should().Be(401);
            _context.Response.Headers["Retry-After"].ToString().Should().Be("1");
        }

        [Test]
        public async Task valid_basic_credentials_should_keep_case_insensitive_scheme_and_colons_in_password()
        {
            var user = new User { Id = 17, Identifier = Guid.NewGuid(), Username = "reader" };
            _context.Request.Headers["Authorization"] = "basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("Reader:correct:with:colons"));
            _authentication.Setup(service => service.Login(_context.Request, "Reader", "correct:with:colons")).Returns(user);
            var handler = await CreateBasicHandler(_context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeTrue();
            result.Ticket.AuthenticationScheme.Should().Be("Basic");
            result.Principal.Identity.Name.Should().Be(user.Username);
            result.Principal.FindFirst("identifier").Value.Should().Be(user.Identifier.ToString());
            result.Principal.FindFirst("AuthType").Value.Should().Be("Basic");
            _authentication.Verify(service => service.Login(_context.Request, "Reader", "correct:with:colons"), Times.Once());
        }

        private AuthenticationController CreateController()
        {
            var services = new Mock<IServiceProvider>();
            services.Setup(provider => provider.GetService(typeof(AspNetAuthenticationService))).Returns(_cookies.Object);
            _context.RequestServices = services.Object;
            return new AuthenticationController(
                _authentication.Object,
                Mocker.GetMock<IConfigFileProvider>().Object,
                Mocker.GetMock<IAppFolderInfo>().Object,
                TestLogger)
            {
                ControllerContext = new ControllerContext { HttpContext = _context }
            };
        }

        private async Task<BasicAuthenticationHandler> CreateBasicHandler(HttpContext context)
        {
            var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
            options.Setup(monitor => monitor.Get("Basic")).Returns(new AuthenticationSchemeOptions { TimeProvider = _clock.Object });
            var handler = new BasicAuthenticationHandler(
                _authentication.Object,
                options.Object,
                NullLoggerFactory.Instance,
                UrlEncoder.Default);
            await handler.InitializeAsync(new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler)), context);
            return handler;
        }

        private void VerifyNoSignIn()
        {
            _cookies.Verify(service => service.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()), Times.Never());
        }

        private static DefaultHttpContext CreateContext()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.80");
            return context;
        }
    }
}
