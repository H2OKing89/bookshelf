using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Readarr.Http.Authentication;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class CredentialCookieFixture
    {
        [TestCase("unchanged", true)]
        [TestCase("missing", false)]
        [TestCase("password", false)]
        [TestCase("salt", false)]
        [TestCase("iterations", false)]
        [TestCase("username", false)]
        [TestCase("identifier", false)]
        [TestCase("deleted", false)]
        public async Task forms_cookie_should_be_bound_to_the_existing_credentials(string change, bool accepted)
        {
            var user = new User { Identifier = Guid.NewGuid(), Username = "owned-reader", Password = "owned-digest", Salt = "owned-salt", Iterations = 220000 };
            var identifier = user.Identifier;
            var claims = new List<Claim> { new Claim("identifier", identifier.ToString()), new Claim("user", user.Username) };
            if (change != "missing")
            {
                claims.Add(new Claim(UserCredentialStamp.ClaimType, UserCredentialStamp.Create(user)));
            }

            switch (change)
            {
                case "password":
                    user.Password = "changed-digest";
                    break;
                case "salt":
                    user.Salt = "changed-salt";
                    break;
                case "iterations":
                    user.Iterations++;
                    break;
                case "username":
                    user.Username = "renamed-reader";
                    break;
                case "identifier":
                    user.Identifier = Guid.NewGuid();
                    break;
            }

            var users = new Mock<IUserService>();
            users.Setup(service => service.FindUser(identifier)).Returns(change == "deleted" ? null : user);
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(provider => provider.InstanceName).Returns("Readarr");
            var auth = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
            auth.Setup(service => service.SignOutAsync(It.IsAny<HttpContext>(), "Forms", It.IsAny<AuthenticationProperties>())).Returns(Task.CompletedTask);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(config.Object);
            services.AddSingleton(users.Object);
            services.AddAppAuthentication();
            services.AddSingleton(auth.Object);
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Forms");
            var http = new DefaultHttpContext { RequestServices = provider };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", "user", "identifier"));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), "Forms");
            var context = new CookieValidatePrincipalContext(http, new AuthenticationScheme("Forms", null, typeof(CookieAuthenticationHandler)), options, ticket);

            await options.Events.OnValidatePrincipal(context);

            (context.Principal != null).Should().Be(accepted);
            auth.Verify(service => service.SignOutAsync(http, "Forms", It.IsAny<AuthenticationProperties>()), accepted ? Times.Never() : Times.Once());
        }
    }
}
