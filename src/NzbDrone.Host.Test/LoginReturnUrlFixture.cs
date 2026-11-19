using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;
using AppAuthenticationService = Readarr.Http.Authentication.IAuthenticationService;
using CookieAuthenticationService = Microsoft.AspNetCore.Authentication.IAuthenticationService;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    public class LoginReturnUrlFixture : TestBase
    {
        private Mock<AppAuthenticationService> _authentication;
        private Mock<CookieAuthenticationService> _cookies;
        private Mock<IConfigFileProvider> _config;
        private DefaultHttpContext _context;
        private ServiceProvider _services;

        [SetUp]
        public void Setup()
        {
            _authentication = new Mock<AppAuthenticationService>();
            _authentication.Setup(service => service.Login(It.IsAny<HttpRequest>(), "reader", "fixture-password"))
                .Returns(new User { Identifier = Guid.NewGuid(), Username = "reader", Password = "fixture-digest" });
            _cookies = new Mock<CookieAuthenticationService>();
            _cookies.Setup(service => service.SignInAsync(It.IsAny<HttpContext>(), "Forms", It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            _cookies.Setup(service => service.SignOutAsync(It.IsAny<HttpContext>(), "Forms", It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            _config = new Mock<IConfigFileProvider>();
            _services = new ServiceCollection().AddSingleton(_cookies.Object).BuildServiceProvider();
            _context = new DefaultHttpContext { RequestServices = _services };
        }

        [TearDown]
        public void Teardown()
        {
            _services.Dispose();
        }

        [TestCase("", null, "/")]
        [TestCase("", "/", "/")]
        [TestCase("", "/books?sort=title#recordings", "/books?sort=title#recordings")]
        [TestCase("", "~/calendar", "/calendar")]
        [TestCase("", "https://other.invalid/books", "/")]
        [TestCase("/books", null, "/books/")]
        [TestCase("/books", "", "/books/")]
        [TestCase("/books", " ", "/books/")]
        [TestCase("/books", "/", "/books/")]
        [TestCase("/books", "/books", "/books")]
        [TestCase("/books", "/books/", "/books/")]
        [TestCase("/books", "/BOOKS", "/BOOKS")]
        [TestCase("/books", "/BOOKS/book/17", "/BOOKS/book/17")]
        [TestCase("/books", "/books?sort=title", "/books?sort=title")]
        [TestCase("/books", "/BOOKS#recordings", "/BOOKS#recordings")]
        [TestCase("/books", "/books?name=a+b%20c&next=%2Fcalendar#recordings", "/books?name=a+b%20c&next=%2Fcalendar#recordings")]
        [TestCase("/books", "/books/book/17?edition=us#files", "/books/book/17?edition=us#files")]
        [TestCase("/books", "/bookshelf", "/books/bookshelf")]
        [TestCase("/books", "/bookshelf/book/17?edition=us#files", "/books/bookshelf/book/17?edition=us#files")]
        [TestCase("/books", "/BOOKSHELF/book/17", "/books/BOOKSHELF/book/17")]
        [TestCase("/books", "/calendar?month=2026-11", "/books/calendar?month=2026-11")]
        [TestCase("/books", "/book/A%2FB?name=a%2Bb#files", "/books/book/A%2FB?name=a%2Bb#files")]
        [TestCase("/books", "/books/book/A%252FB", "/books/book/A%252FB")]
        [TestCase("/books", "/books%2Fbook/17", "/books/books%2Fbook/17")]
        [TestCase("/books", "/%62ooks/book/17", "/books/%62ooks/book/17")]
        [TestCase("/books", "/%2F%2Fother.invalid", "/books/%2F%2Fother.invalid")]
        [TestCase("/books", "~/", "/books/")]
        [TestCase("/books", "~/calendar?sort=title#recordings", "/books/calendar?sort=title#recordings")]
        [TestCase("/books", "~/books/book/17", "/books/books/book/17")]
        [TestCase("/books", "https://other.invalid/books", "/books/")]
        [TestCase("/books", "HTTP://other.invalid/books", "/books/")]
        [TestCase("/books", "//other.invalid/books", "/books/")]
        [TestCase("/books", "/\\other.invalid/books", "/books/")]
        [TestCase("/books", "~//other.invalid/books", "/books/")]
        [TestCase("/books", "book/17", "/books/")]
        [TestCase("/books", "?sort=title", "/books/")]
        [TestCase("/books", "#recordings", "/books/")]
        [TestCase("/books", "\n/books", "/books/")]
        [TestCase("/books", "/books/\r\nLocation: https://other.invalid", "/books/")]
        public async Task successful_login_should_keep_redirects_within_the_configured_application(string urlBase, string returnUrl, string expected)
        {
            var controller = Controller(urlBase);

            var result = await controller.Login(new LoginResource { Username = "reader", Password = "fixture-password" }, returnUrl);

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().Be(expected);
            _cookies.Verify(service => service.SignInAsync(_context, "Forms", It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        [TestCase("")]
        [TestCase("/books")]
        public async Task logout_should_keep_its_existing_base_path_destination(string urlBase)
        {
            var controller = Controller(urlBase);

            var result = await controller.Logout();

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().Be(urlBase + "/");
            _authentication.Verify(service => service.Logout(_context), Times.Once());
            _cookies.Verify(service => service.SignOutAsync(_context, "Forms", It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        private AuthenticationController Controller(string urlBase)
        {
            _config.SetupGet(config => config.UrlBase).Returns(urlBase);
            _context.Request.PathBase = urlBase;
            return new AuthenticationController(_authentication.Object, _config.Object, Mock.Of<IAppFolderInfo>(), TestLogger)
            {
                ControllerContext = new ControllerContext { HttpContext = _context },
                Url = new UrlHelper(new ActionContext(_context, new RouteData(), new ActionDescriptor()))
            };
        }
    }
}
