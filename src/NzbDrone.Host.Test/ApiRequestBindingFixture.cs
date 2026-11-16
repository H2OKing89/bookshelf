using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Commands;
using Readarr.Api.V1.Config;
using Readarr.Http;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    [NonParallelizable]
    public class ApiRequestBindingFixture : TestBase
    {
        private IHost _host;
        private HttpClient _client;
        private RequestBindingProbeDependency _dependency;

        [SetUp]
        public async Task Setup()
        {
            _dependency = new RequestBindingProbeDependency();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { { "dataProtectionFolder", TempFolder } })
                .Build();
            _host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseKestrel()
                    .UseUrls("http://127.0.0.1:0")
                    .ConfigureServices(services =>
                    {
                        new Startup(configuration).ConfigureServices(services);
                        services.AddSingleton(Mock.Of<IConfigFileProvider>());
                        services.AddSingleton(_dependency);

                        // Production's assembly scan registers these resources in DryIoc.
                        services.AddTransient<CommandResource>(_ => new CommandResource { Name = "resolved-from-services" });
                        services.AddTransient<HostConfigResource>(_ => new HostConfigResource { EnableSsl = true, ProxyPort = 1234 });
                        services.AddControllers()
                            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new ProbeControllerFeatureProvider()))
                            .AddControllersAsServices();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCors();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    }))
                .Build();
            await _host.StartAsync();
            var address = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
            _client = new HttpClient { BaseAddress = new Uri(address) };
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

        [Test]
        public async Task registered_command_resource_should_bind_from_json_while_dependencies_come_from_services()
        {
            using var response = await Post("command", "{\"name\":\"Backup\"}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("name").GetString().Should().Be("Backup");
            body.RootElement.GetProperty("constructorDependency").GetString().Should().Be(_dependency.Identifier);
            body.RootElement.GetProperty("actionDependency").GetString().Should().Be(_dependency.Identifier);
            _dependency.ActionCalls.Should().Be(1);
        }

        [Test]
        public async Task registered_settings_resource_should_preserve_explicit_false_and_zero_from_json()
        {
            using var response = await Post("settings", "{\"enableSsl\":false,\"proxyPort\":0}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("enableSsl").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("proxyPort").GetInt32().Should().Be(0);
            _dependency.ActionCalls.Should().Be(1);
        }

        [TestCase("{\"name\":")]
        [TestCase("{\"name\":\"Backup\",\"id\":\"invalid\"}")]
        [TestCase("")]
        public async Task malformed_or_missing_json_should_be_rejected_before_the_action(string json)
        {
            using var response = await Post("command", json);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
            _dependency.ActionCalls.Should().Be(0);
        }

        private async Task<HttpResponseMessage> Post(string action, string json)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _client.PostAsync("api/v1/request-binding-probe/" + action, content);
        }

        private sealed class ProbeControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                feature.Controllers.Clear();
                feature.Controllers.Add(typeof(RequestBindingProbeController).GetTypeInfo());
            }
        }
    }

    public class RequestBindingProbeDependency
    {
        public string Identifier { get; } = Guid.NewGuid().ToString();
        public int ActionCalls { get; set; }
    }

    [V1ApiController("request-binding-probe")]
    public class RequestBindingProbeController : ControllerBase
    {
        private readonly RequestBindingProbeDependency _dependency;

        public RequestBindingProbeController(RequestBindingProbeDependency dependency)
        {
            _dependency = dependency;
        }

        [HttpPost("command")]
        public IActionResult Command(CommandResource resource, [FromServices] RequestBindingProbeDependency dependency)
        {
            _dependency.ActionCalls++;
            return Ok(new
            {
                resource.Name,
                ConstructorDependency = _dependency.Identifier,
                ActionDependency = dependency.Identifier
            });
        }

        [HttpPost("settings")]
        public IActionResult Settings(HostConfigResource resource)
        {
            _dependency.ActionCalls++;
            return Ok(new { resource.EnableSsl, resource.ProxyPort });
        }
    }
}
