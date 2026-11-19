using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    public class HostFilteringFixture
    {
        [TestCase("")]
        [TestCase("unlisted.example.test:8787")]
        [TestCase("[2001:db8::1]:8787")]
        public async Task empty_configuration_should_preserve_permissive_access(string host)
        {
            (await Process(string.Empty, host)).Response.StatusCode.Should().Be(200);
        }

        [TestCase("books.test")]
        [TestCase("BOOKS.TEST:8787")]
        [TestCase("localhost:8787")]
        [TestCase("127.0.0.1:8787")]
        [TestCase("[::1]:8787")]
        public async Task exact_hosts_and_local_recovery_should_remain_reachable(string host)
        {
            (await Process("books.test", host)).Response.StatusCode.Should().Be(200);
        }

        [TestCase("xn--bcher-kva.test", "bücher.test")]
        [TestCase("[2001:db8::12]:8787", "2001:0db8::12")]
        [TestCase("foo.books.test", "*.books.test")]
        [TestCase("deeper.foo.books.test:443", "*.books.test")]
        public async Task normalized_ip_idn_and_subdomain_patterns_should_match(string host, string configured)
        {
            (await Process(configured, host)).Response.StatusCode.Should().Be(200);
        }

        [TestCase("unexpected.test")]
        [TestCase("books.test.attacker.test")]
        [TestCase("books.test")]
        [TestCase("")]
        public async Task configured_subdomain_pattern_should_reject_unlisted_and_parent_hosts(string host)
        {
            (await Process("*.books.test", host)).Response.StatusCode.Should().Be(400);
        }

        [TestCase("/login")]
        [TestCase("/api/v1/config/host")]
        [TestCase("/signalr/messages")]
        [TestCase("/bookshelf/initialize.json")]
        public async Task rejected_hosts_should_stop_before_ui_api_or_signalr_endpoints(string path)
        {
            var context = await Process("books.test", "unexpected.test", path: path);

            context.Response.StatusCode.Should().Be(400);
            context.Items.Should().NotContainKey("endpointReached");
        }

        [Test]
        public async Task current_machine_hostname_should_remain_a_recovery_route()
        {
            (await Process("books.test", Environment.MachineName)).Response.StatusCode.Should().Be(200);
        }

        [TestCase("192.0.2.20", "", "proxy.internal", "books.test", 400)]
        [TestCase("192.0.2.20", "192.0.2.20", "proxy.internal", "books.test", 200)]
        [TestCase("192.0.2.20", "192.0.2.20", "books.test", "unexpected.test", 400)]
        [TestCase("192.0.2.20", "", "books.test", "unexpected.test", 200)]
        public async Task host_filter_should_use_only_trusted_forwarded_host(string peer, string trusted, string host, string forwarded, int expected)
        {
            (await Process("books.test", host, peer, trusted, forwarded)).Response.StatusCode.Should().Be(expected);
        }

        [TestCase("*")]
        [TestCase("0.0.0.0")]
        [TestCase("books.test:8787")]
        [TestCase(";,")]
        public void invalid_configuration_should_fail_during_options_resolution(string configured)
        {
            using var provider = Services(configured).BuildServiceProvider();

            Assert.Throws<FormatException>(() => _ = provider.GetRequiredService<IOptions<HostFilteringOptions>>().Value);
        }

        private static ServiceCollection Services(string allowedHosts, string trusted = "")
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(provider => provider.AllowedHosts).Returns(allowedHosts);
            config.SetupGet(provider => provider.TrustedNetworks).Returns(trusted);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(config.Object);
            services.AddOptions<HostFilteringOptions>().Configure<IConfigFileProvider>(ConfigureHostFilteringOptions.Configure);
            services.AddOptions<ForwardedHeadersOptions>().Configure<IConfigFileProvider>(ForwardedHeadersConfigurator.Configure);
            return services;
        }

        private static async Task<HttpContext> Process(string allowedHosts, string host, string peer = "198.51.100.20", string trusted = "", string forwarded = null, string path = "/")
        {
            using var provider = Services(allowedHosts, trusted).BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            app.UseTrustedForwardedHeaders();
            app.UseHostFiltering();
            app.Run(context =>
            {
                context.Items["endpointReached"] = true;
                return Task.CompletedTask;
            });
            var request = new DefaultHttpContext();
            request.RequestServices = provider;
            request.Request.Host = new HostString(host);
            request.Request.Path = path;
            request.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            if (forwarded != null)
            {
                request.Request.Headers["X-Forwarded-Host"] = forwarded;
            }

            await app.Build()(request);
            return request;
        }
    }
}
