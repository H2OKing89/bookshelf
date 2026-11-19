using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Readarr.Api.V1.Config;
using HttpAuthenticationService = Microsoft.AspNetCore.Authentication.IAuthenticationService;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class HostAccountAuthenticationFixture
    {
        [TestCase(AuthenticationType.Forms)]
        [TestCase(AuthenticationType.Basic)]
        public async Task authorized_api_requests_can_attach_a_secondary_account_without_replacing_api_identity(AuthenticationType method)
        {
            var http = new DefaultHttpContext();
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("ApiKey", "true") }, "API Key"));
            var account = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("AuthType", method.ToString()) }, method.ToString()));
            var authentication = new Mock<HttpAuthenticationService>();
            authentication.Setup(service => service.AuthenticateAsync(http, method.ToString()))
                .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(account, method.ToString())));
            var config = Mock.Of<IConfigFileProvider>(provider => provider.AuthenticationMethod == method);
            using var services = new ServiceCollection().AddSingleton(config).AddSingleton(authentication.Object).BuildServiceProvider();
            http.RequestServices = services;
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var filters = new List<IFilterMetadata>();
            var executing = new ResourceExecutingContext(action, filters, new List<IValueProviderFactory>());
            var reachedAction = false;

            await new HostAccountAuthenticationAttribute().OnResourceExecutionAsync(executing, () =>
            {
                reachedAction = true;
                return Task.FromResult(new ResourceExecutedContext(action, filters));
            });

            reachedAction.Should().BeTrue();
            http.User.HasClaim("ApiKey", "true").Should().BeTrue();
            http.User.HasClaim("AuthType", method.ToString()).Should().BeTrue();
            http.User.Identities.Should().HaveCount(2);
        }

        [Test]
        public async Task secondary_credentials_should_never_authenticate_an_unauthorized_api_request()
        {
            var http = new DefaultHttpContext();
            var authentication = new Mock<HttpAuthenticationService>(MockBehavior.Strict);
            var config = Mock.Of<IConfigFileProvider>(provider => provider.AuthenticationMethod == AuthenticationType.Forms);
            using var services = new ServiceCollection().AddSingleton(config).AddSingleton(authentication.Object).BuildServiceProvider();
            http.RequestServices = services;
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var filters = new List<IFilterMetadata>();
            var executing = new ResourceExecutingContext(action, filters, new List<IValueProviderFactory>());

            await new HostAccountAuthenticationAttribute().OnResourceExecutionAsync(
                executing,
                () => Task.FromResult(new ResourceExecutedContext(action, filters)));

            http.User.Identity.IsAuthenticated.Should().BeFalse();
            authentication.VerifyNoOtherCalls();
        }
    }
}
