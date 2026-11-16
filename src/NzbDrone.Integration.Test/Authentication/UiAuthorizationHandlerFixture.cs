using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Http.Authentication;
using NzbDrone.Test.Common;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class UiAuthorizationHandlerFixture : TestBase<UiAuthorizationHandler>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(provider => provider.AuthenticationRequired)
                .Returns(AuthenticationRequiredType.DisabledForLocalAddresses);
        }

        [TestCase("127.0.0.1")]
        [TestCase("127.20.30.40")]
        [TestCase("10.0.0.5")]
        [TestCase("172.16.0.1")]
        [TestCase("172.31.255.254")]
        [TestCase("192.168.1.50")]
        [TestCase("169.254.10.1")]
        [TestCase("::1")]
        [TestCase("fe80::1")]
        [TestCase("fc00::1")]
        [TestCase("fec0::1")]
        [TestCase("::ffff:192.168.1.50")]
        public async Task should_bypass_authentication_for_a_direct_local_client(string address)
        {
            (await IsAuthorized(CreateContext(address))).Should().BeTrue();
        }

        [TestCase("203.0.113.66")]
        [TestCase("172.15.255.255")]
        [TestCase("172.32.0.0")]
        [TestCase("192.169.1.1")]
        [TestCase("2001:db8::1")]
        [TestCase("::ffff:203.0.113.66")]
        [TestCase("0.0.0.0")]
        [TestCase("::")]
        public async Task should_require_authentication_for_a_direct_public_or_unspecified_client(string address)
        {
            (await IsAuthorized(CreateContext(address))).Should().BeFalse();
        }

        [TestCase("100.64.0.1")]
        [TestCase("100.127.255.254")]
        [TestCase("::ffff:100.64.0.1")]
        public async Task should_require_authentication_for_cgnat_by_default(string address)
        {
            (await IsAuthorized(CreateContext(address))).Should().BeFalse();
        }

        [TestCase("100.64.0.1", true, true)]
        [TestCase("100.127.255.254", true, true)]
        [TestCase("::ffff:100.64.0.1", true, true)]
        [TestCase("100.64.0.1", false, false)]
        [TestCase("::ffff:100.64.0.1", false, false)]
        [TestCase("100.63.255.255", true, false)]
        [TestCase("100.128.0.0", true, false)]
        public async Task should_bypass_cgnat_only_with_explicit_trust(string address, bool trustCgnat, bool authorized)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(provider => provider.TrustCgnatIpAddresses).Returns(trustCgnat);

            (await IsAuthorized(CreateContext(address))).Should().Be(authorized);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("not-an-address")]
        [TestCase("127.0.0.1")]
        [TestCase("203.0.113.9")]
        [TestCase("203.0.113.9, 127.0.0.1")]
        [TestCase("127.0.0.1, 203.0.113.9")]
        public async Task should_require_authentication_when_any_forwarded_for_header_remains(string forwardedFor)
        {
            var context = CreateContext("10.0.0.99");
            context.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = forwardedFor;

            (await IsAuthorized(context)).Should().BeFalse();
        }

        [Test]
        public async Task should_require_authentication_when_multiple_forwarded_for_values_remain()
        {
            var context = CreateContext("192.168.1.50");
            context.Request.Headers["x-forwarded-for"] = new StringValues(new[] { "203.0.113.9", "127.0.0.1" });

            (await IsAuthorized(context)).Should().BeFalse();
        }

        [TestCase("10.0.0.2")]
        [TestCase("::ffff:10.0.0.2")]
        [TestCase("::1")]
        [TestCase("100.64.0.1")]
        public async Task should_require_authentication_when_a_proxy_chain_is_only_partly_consumed(string resolvedAddress)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(provider => provider.TrustCgnatIpAddresses).Returns(true);
            var context = CreateContext(resolvedAddress);
            context.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = "203.0.113.9";

            (await IsAuthorized(context)).Should().BeFalse();
        }

        [Test]
        public async Task should_bypass_authentication_when_the_local_client_is_resolved_and_no_forwarded_for_remains()
        {
            var context = CreateContext("192.168.1.50");
            context.Request.Headers["X-Original-For"] = "10.0.0.99:54321";

            (await IsAuthorized(context)).Should().BeTrue();
        }

        [Test]
        public async Task should_require_authentication_when_the_remote_address_is_unknown()
        {
            (await IsAuthorized(CreateContext(null))).Should().BeFalse();
        }

        [Test]
        public async Task should_require_authentication_without_an_http_context()
        {
            (await IsAuthorized(null)).Should().BeFalse();
        }

        [TestCase("127.0.0.1")]
        [TestCase("192.168.1.50")]
        [TestCase("::1")]
        [TestCase("100.64.0.1")]
        public async Task should_require_authentication_for_every_address_when_enabled(string address)
        {
            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(provider => provider.AuthenticationRequired)
                .Returns(AuthenticationRequiredType.Enabled);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(provider => provider.TrustCgnatIpAddresses).Returns(true);

            (await IsAuthorized(CreateContext(address))).Should().BeFalse();
        }

        [TestCase("Basic")]
        [TestCase("Cookies")]
        [TestCase("API Key")]
        public async Task should_allow_authenticated_principals_when_forwarded_for_remains(string authenticationType)
        {
            var context = CreateContext("203.0.113.66");
            context.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = "127.0.0.1";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", "reader") }, authenticationType));

            (await IsAuthorized(context, principal)).Should().BeTrue();
        }

        [TestCase("Basic")]
        [TestCase("Cookies")]
        [TestCase("API Key")]
        public async Task should_allow_authenticated_principals_when_authentication_is_required(string authenticationType)
        {
            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(provider => provider.AuthenticationRequired)
                .Returns(AuthenticationRequiredType.Enabled);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", "reader") }, authenticationType));

            (await IsAuthorized(CreateContext("203.0.113.66"), principal)).Should().BeTrue();
        }

        [Test]
        public async Task should_apply_authentication_requirement_changes_to_each_request()
        {
            var handler = Subject;
            (await IsAuthorized(CreateContext("127.0.0.1"), handler: handler)).Should().BeTrue();
            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(provider => provider.AuthenticationRequired)
                .Returns(AuthenticationRequiredType.Enabled);

            (await IsAuthorized(CreateContext("127.0.0.1"), handler: handler)).Should().BeFalse();
            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(provider => provider.AuthenticationRequired)
                .Returns(AuthenticationRequiredType.DisabledForLocalAddresses);

            (await IsAuthorized(CreateContext("127.0.0.1"), handler: handler)).Should().BeTrue();
        }

        [Test]
        public async Task should_keep_authentication_requirements_isolated_between_handler_instances()
        {
            var requiredConfig = new Mock<IConfigFileProvider>();
            requiredConfig.SetupGet(provider => provider.AuthenticationRequired).Returns(AuthenticationRequiredType.Enabled);
            var requiredHandler = new UiAuthorizationHandler(requiredConfig.Object);
            var localHandler = Subject;

            (await IsAuthorized(CreateContext("127.0.0.1"), handler: requiredHandler)).Should().BeFalse();
            (await IsAuthorized(CreateContext("127.0.0.1"), handler: localHandler)).Should().BeTrue();

            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(provider => provider.AuthenticationRequired)
                .Returns(AuthenticationRequiredType.Enabled);

            (await IsAuthorized(CreateContext("127.0.0.1"), handler: requiredHandler)).Should().BeFalse();
            (await IsAuthorized(CreateContext("127.0.0.1"), handler: localHandler)).Should().BeFalse();
            requiredConfig.SetupGet(provider => provider.AuthenticationRequired).Returns(AuthenticationRequiredType.DisabledForLocalAddresses);

            (await IsAuthorized(CreateContext("127.0.0.1"), handler: requiredHandler)).Should().BeTrue();
            (await IsAuthorized(CreateContext("127.0.0.1"), handler: localHandler)).Should().BeFalse();
        }

        private async Task<bool> IsAuthorized(HttpContext httpContext, ClaimsPrincipal principal = null, UiAuthorizationHandler handler = null)
        {
            var requirement = new BypassableDenyAnonymousAuthorizationRequirement();
            var context = new AuthorizationHandlerContext(new[] { requirement }, principal ?? new ClaimsPrincipal(), httpContext);

            await (handler ?? Subject).HandleAsync(context);
            await requirement.HandleAsync(context);

            return context.HasSucceeded;
        }

        private static HttpContext CreateContext(string address)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = address == null ? null : IPAddress.Parse(address);
            return context;
        }
    }
}
