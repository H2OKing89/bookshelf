using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Http.Authentication;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;
using Readarr.Http.Frontend;
using Readarr.Http.Frontend.Mappers;

namespace NzbDrone.Host.Test
{
    [TestFixture("")]
    [TestFixture("/bookshelf")]
    [NonParallelizable]
    public class AuthenticationRedirectFixture : TestBase
    {
        private readonly string _urlBase;
        private IHost _host;
        private HttpClient _client;

        public AuthenticationRedirectFixture(string urlBase)
        {
            _urlBase = urlBase;
        }

        [SetUp]
        public async Task Setup()
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(provider => provider.InstanceName).Returns("RedirectFixture");
            config.SetupGet(provider => provider.UrlBase).Returns(_urlBase);
            config.SetupGet(provider => provider.AuthenticationMethod).Returns(AuthenticationType.Forms);
            config.SetupGet(provider => provider.AuthenticationRequired).Returns(AuthenticationRequiredType.Enabled);

            var mapper = new Mock<IMapHttpRequestsToDisk>();
            mapper.Setup(m => m.CanHandle(It.IsAny<string>())).Returns(true);
            mapper.Setup(m => m.GetResponse(It.IsAny<string>()))
                .Returns((string path) => new ContentResult
                {
                    ContentType = "text/html",
                    Content = path == "/login" ? "<title>Login Test</title>" : "<title>Library Test</title>"
                });

            _host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseKestrel()
                    .UseUrls("http://127.0.0.1:0")
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(config.Object);
                        services.AddSingleton(TestLogger);
                        services.AddSingleton(mapper.Object);
                        services.AddDataProtection().UseEphemeralDataProtectionProvider();
                        services.AddAppAuthentication();
                        services.AddAuthorization();
                        services.AddSingleton<IAuthorizationPolicyProvider, UiAuthorizationPolicyProvider>();
                        services.AddSingleton<IAuthorizationHandler, UiAuthorizationHandler>();
                        services.AddCors(options => options.AddPolicy("AllowGet", policy => policy.AllowAnyOrigin().WithMethods("GET")));
                        services.AddControllers()
                            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new RedirectControllerFeatureProvider()))
                            .AddControllersAsServices();
                    })
                    .Configure(app =>
                    {
                        app.UsePathBase(_urlBase);
                        app.UseRouting();
                        app.UseCors();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    }))
                .Build();

            await _host.StartAsync();
            var address = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
            _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        }

        [TearDown]
        public async Task Teardown()
        {
            _client?.Dispose();
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        [TestCase("/")]
        [TestCase("/book/fixture-book?sort=title&direction=ascending")]
        [TestCase("/initialize.json")]
        public async Task unauthenticated_ui_should_redirect_to_login_and_preserve_return_url(string path)
        {
            using var response = await _client.GetAsync(_urlBase + path);

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location.Should().NotBeNull();
            var location = new Uri(_client.BaseAddress, response.Headers.Location);
            location.AbsolutePath.Should().Be(_urlBase + "/login");
            QueryHelpers.ParseQuery(location.Query)["returnUrl"].ToString().Should().Be(_urlBase + path);
        }

        [Test]
        public async Task anonymous_login_page_should_remain_accessible()
        {
            using var response = await _client.GetAsync(_urlBase + "/login");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType.MediaType.Should().Be("text/html");
            (await response.Content.ReadAsStringAsync()).Should().Contain("Login Test");
        }

        [Test]
        public async Task api_endpoint_should_still_return_unauthorized_instead_of_redirecting()
        {
            using var response = await _client.GetAsync(_urlBase + "/api/auth-redirect-probe");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [TestCase("/")]
        [TestCase("/initialize.json")]
        public async Task ajax_ui_request_should_still_return_unauthorized(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _urlBase + path);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task authenticated_ui_should_return_the_library_instead_of_redirecting()
        {
            using var signIn = await _client.PostAsync(_urlBase + "/api/auth-redirect-probe/sign-in", null);
            signIn.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var response = await _client.GetAsync(_urlBase + "/");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Library Test");
        }

        private sealed class RedirectControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                feature.Controllers.Clear();
                feature.Controllers.Add(typeof(StaticResourceController).GetTypeInfo());
                feature.Controllers.Add(typeof(InitializeJsonController).GetTypeInfo());
                feature.Controllers.Add(typeof(AuthenticationRedirectProbeController).GetTypeInfo());
            }
        }
    }

    [ApiController]
    [Authorize(AuthenticationSchemes = "Forms")]
    [Route("api/auth-redirect-probe")]
    public class AuthenticationRedirectProbeController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { Authenticated = true });
        }

        [AllowAnonymous]
        [HttpPost("sign-in")]
        public async Task<IActionResult> SignIn()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "fixture-reader") }, "Cookies"));
            await HttpContext.SignInAsync("Forms", principal);
            return NoContent();
        }
    }
}
