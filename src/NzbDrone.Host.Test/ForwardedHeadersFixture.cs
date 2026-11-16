using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Host;
using NzbDrone.Http.Authentication;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class ForwardedHeadersFixture : TestBase
    {
        private const string ForwardedFor = "X-Forwarded-For";
        private const string ForwardedProto = "X-Forwarded-Proto";
        private const string ForwardedHost = "X-Forwarded-Host";

        [OneTimeSetUp]
        public void InitializeLogger()
        {
            NzbDroneLogger.GetLogger(typeof(ForwardedHeadersConfigurator));
        }

        [Test]
        public void startup_should_resolve_proxy_options_from_saved_configuration()
        {
            var config = Config("192.0.2.20");
            var services = new ServiceCollection();
            services.AddSingleton(config.Object);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["dataProtectionFolder"] = TempFolder
            }).Build();
            new Startup(configuration).ConfigureServices(services);

            using (var provider = services.BuildServiceProvider())
            {
                var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

                options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);
                options.ForwardLimit.Should().BeNull();
                options.KnownProxies.Should().Contain(IPAddress.IPv6Loopback);
                options.KnownNetworks.Should().Contain(network => network.Contains(IPAddress.Parse("127.0.0.1")));
                options.KnownNetworks.Should().Contain(network => network.Contains(IPAddress.Parse("192.0.2.20")));
                options.KnownNetworks.Should().NotContain(network => network.Contains(IPAddress.Parse("192.0.2.21")));
            }
        }

        [Test]
        public void invalid_startup_entries_should_warn_and_never_enable_trust_for_all_peers()
        {
            var options = new ForwardedHeadersOptions();

            ForwardedHeadersConfigurator.Configure(options, Config("bad, 192.0.2.20, 192.0.2.5/24").Object);

            options.KnownNetworks.Should().Contain(network => network.Contains(IPAddress.Loopback));
            options.KnownProxies.Should().Contain(IPAddress.IPv6Loopback);
            options.KnownNetworks.Should().Contain(network => network.Contains(IPAddress.Parse("192.0.2.20")));
            options.KnownNetworks.Should().NotContain(network => network.Contains(IPAddress.Parse("192.0.2.5")));
            ExceptionVerification.ExpectedWarns(2);
        }

        [TestCase("203.0.113.5")]
        [TestCase("::ffff:203.0.113.5")]
        [TestCase("2001:db8:abcd::5")]
        [TestCase("10.10.10.5")]
        public async Task untrusted_peer_should_not_override_client_scheme_or_host(string peer)
        {
            var context = await Process(peer, Headers("127.0.0.1"));

            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse(peer));
            context.Request.Scheme.Should().Be("http");
            context.Request.Host.Value.Should().Be("bookshelf.internal");
            context.Request.Headers[ForwardedFor].ToString().Should().Be("127.0.0.1");
            (await CanBypass(context)).Should().BeFalse();
        }

        [Test]
        public async Task untrusted_peer_should_not_override_scheme_or_host_without_a_forwarded_client_header()
        {
            var headers = Headers(null);
            var context = await Process("203.0.113.5", headers);

            context.Request.Scheme.Should().Be("http");
            context.Request.Host.Value.Should().Be("bookshelf.internal");
        }

        [TestCase("127.0.0.1", "")]
        [TestCase("::1", "")]
        [TestCase("::ffff:127.0.0.1", "")]
        [TestCase("192.0.2.20", "192.0.2.20")]
        [TestCase("192.0.2.20", "192.0.2.0/24")]
        [TestCase("::ffff:192.0.2.20", "192.0.2.20")]
        [TestCase("::ffff:192.0.2.20", "::ffff:192.0.2.0/120")]
        [TestCase("2001:db8:abcd::5", "2001:db8:abcd::/48")]
        public async Task trusted_proxy_should_resolve_the_client_scheme_and_host(string peer, string trusted)
        {
            var context = await Process(peer, Headers("203.0.113.5"), trusted);

            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.5"));
            context.Request.Scheme.Should().Be("https");
            context.Request.Host.Value.Should().Be("books.example.test");
            context.Request.Headers.Should().NotContainKey(ForwardedFor);
            (await CanBypass(context)).Should().BeFalse();
        }

        [TestCase("192.0.2.20, 192.0.2.21")]
        [TestCase("192.0.2.0/24")]
        public async Task complete_trusted_chain_should_consume_multiple_hops(string trusted)
        {
            var headers = Headers("203.0.113.5, 192.0.2.21");
            headers[ForwardedProto] = "https, http";
            headers[ForwardedHost] = "books.example.test, proxy.internal";

            var context = await Process("192.0.2.20", headers, trusted);

            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.5"));
            context.Request.Scheme.Should().Be("https");
            context.Request.Host.Value.Should().Be("books.example.test");
            context.Request.Headers.Should().NotContainKey(ForwardedFor);
            (await CanBypass(context)).Should().BeFalse();
        }

        [Test]
        public async Task remaining_untrusted_hop_should_prevent_local_bypass_and_further_header_changes()
        {
            var headers = Headers("127.0.0.1, 10.10.10.5");
            headers[ForwardedProto] = "https, http";
            headers[ForwardedHost] = "forged.example.test, proxy.internal";

            var context = await Process("192.0.2.20", headers, "192.0.2.20");

            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("10.10.10.5"));
            context.Request.Scheme.Should().Be("http");
            context.Request.Host.Value.Should().Be("proxy.internal");
            context.Request.Headers[ForwardedFor].ToString().Should().Be("127.0.0.1");
            (await CanBypass(context)).Should().BeFalse();
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase(", ,")]
        [TestCase("\"\"")]
        [TestCase("\" \"")]
        [TestCase("\"\", \" \"")]
        [TestCase("\", ,\"")]
        [TestCase("unknown")]
        [TestCase("not-an-ip")]
        public async Task unresolved_client_header_should_not_be_erased_when_scheme_or_host_is_supplied(string forwardedFor)
        {
            var context = await Process("127.0.0.1", Headers(forwardedFor));

            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Loopback);
            context.Request.Scheme.Should().Be("http");
            context.Request.Host.Value.Should().Be("bookshelf.internal");
            context.Request.Headers.Should().ContainKey(ForwardedFor);
            (await CanBypass(context)).Should().BeFalse();
        }

        [Test]
        public async Task unknown_transport_peer_should_not_be_replaced_by_a_forwarded_loopback_address()
        {
            var context = await Process(null, Headers("127.0.0.1"));

            context.Connection.RemoteIpAddress.Should().BeNull();
            context.Request.Scheme.Should().Be("http");
            context.Request.Host.Value.Should().Be("bookshelf.internal");
            context.Request.Headers.Should().ContainKey(ForwardedFor);
            (await CanBypass(context)).Should().BeFalse();
        }

        [Test]
        public async Task valid_resolved_local_client_should_keep_the_existing_opt_in_bypass()
        {
            var context = await Process("192.0.2.20", Headers("10.10.10.5"), "192.0.2.20");

            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("10.10.10.5"));
            context.Request.Headers.Should().NotContainKey(ForwardedFor);
            (await CanBypass(context)).Should().BeTrue();
        }

        private static Mock<IConfigFileProvider> Config(string trusted)
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.TrustedNetworks).Returns(trusted);
            config.SetupGet(c => c.AuthenticationRequired).Returns(AuthenticationRequiredType.DisabledForLocalAddresses);
            return config;
        }

        private static Dictionary<string, string> Headers(string forwardedFor)
        {
            var headers = new Dictionary<string, string>
            {
                [ForwardedProto] = "https",
                [ForwardedHost] = "books.example.test"
            };
            if (forwardedFor != null)
            {
                headers[ForwardedFor] = forwardedFor;
            }

            return headers;
        }

        private static async Task<HttpContext> Process(string peer, Dictionary<string, string> headers, string trusted = "")
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Config(trusted).Object);
            services.AddOptions<ForwardedHeadersOptions>().Configure<IConfigFileProvider>(ForwardedHeadersConfigurator.Configure);

            using (var provider = services.BuildServiceProvider())
            {
                var builder = new ApplicationBuilder(provider);
                builder.UseTrustedForwardedHeaders();
                builder.Run(context => Task.CompletedTask);
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = peer == null ? null : IPAddress.Parse(peer);
                context.Request.Scheme = "http";
                context.Request.Host = new HostString("bookshelf.internal");
                foreach (var header in headers)
                {
                    context.Request.Headers[header.Key] = header.Value;
                }

                await builder.Build()(context);
                return context;
            }
        }

        private static async Task<bool> CanBypass(HttpContext httpContext)
        {
            var requirement = new BypassableDenyAnonymousAuthorizationRequirement();
            var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(new ClaimsIdentity()), httpContext);
            IAuthorizationHandler handler = new UiAuthorizationHandler(Config("").Object);
            await handler.HandleAsync(context);
            return context.HasSucceeded;
        }
    }
}
