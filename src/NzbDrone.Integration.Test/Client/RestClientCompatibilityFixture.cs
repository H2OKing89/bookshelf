using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Readarr.Http.REST;
using RestSharp;

namespace NzbDrone.Integration.Test.Client
{
    public class RestClientCompatibilityFixture
    {
        [Test]
        public async Task get_preserves_query_api_key_and_cache_checks()
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            rest.AddDefaultHeader("X-Api-Key", "owned-key");
            var client = new ClientBase<OwnedResource>(rest, "owned-key", "owned");
            var request = client.BuildRequest("lookup");
            request.AddQueryParameter("term", "a + b/日本語");

            var response = client.Get<OwnedResource>(request);
            response.Id.Should().Be(7);
            server.Path.Should().Be("/api/v1/owned/lookup");
            server.Method.Should().Be("GET");
            server.QueryTerm.Should().Be("a + b/日本語");
            server.ApiKeys.Should().Equal("owned-key");
            server.Authorization.Should().Be("owned-key");
        }

        [TestCase(false, "False")]
        [TestCase(0, "0")]
        public async Task paging_preserves_explicit_false_and_zero_query_filters(object value, string expected)
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            var client = new ClientBase<OwnedResource>(rest, "owned-key", "owned");

            client.GetPaged(1, 15, "id", "desc", "filter", value).Records.Should().ContainSingle();
            server.QueryFilter.Should().Be(expected);
            server.Page.Should().Be("1");
        }

        [Test]
        public async Task mismatched_default_api_key_is_not_silently_sent()
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            rest.AddDefaultHeader("X-Api-Key", "another-owned-key");
            var client = new ClientBase<OwnedResource>(rest, "owned-key", "owned");

            Assert.Throws<InvalidOperationException>(() => client.BuildRequest());
            server.Method.Should().BeNull();
        }

        [Test]
        public async Task post_preserves_legacy_json_wire_shape_including_null_false_and_zero()
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            var client = new ClientBase<OwnedResource>(rest, "owned-key", "owned");
            var resource = new OwnedResource
            {
                Name = "Owned & <>",
                NullValue = null,
                Allowed = false,
                Count = 0,
                Kind = DayOfWeek.Monday
            };

            client.Post(resource).Id.Should().Be(7);
            server.Method.Should().Be("POST");
            server.ContentType.Should().StartWith("application/json");
            using var payload = JsonDocument.Parse(server.Body);
            payload.RootElement.GetProperty("Name").GetString().Should().Be("Owned & <>");
            payload.RootElement.GetProperty("NullValue").ValueKind.Should().Be(JsonValueKind.Null);
            payload.RootElement.GetProperty("Allowed").GetBoolean().Should().BeFalse();
            payload.RootElement.GetProperty("Count").GetInt32().Should().Be(0);
            payload.RootElement.GetProperty("Kind").GetInt32().Should().Be(1);
            payload.RootElement.TryGetProperty("name", out _).Should().BeFalse();
        }

        [TestCase(400)]
        [TestCase(404)]
        [TestCase(406)]
        public async Task expected_http_errors_keep_status_body_and_no_transport_exception(int status)
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            var client = new ClientBase(rest, "owned-key", "owned");
            var request = client.BuildRequest();
            request.AddQueryParameter("status", status);

            var body = client.Execute(request, (HttpStatusCode)status);

            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("id").GetInt32().Should().Be(7);
        }

        [Test]
        public async Task explicit_accept_header_is_not_replaced_by_default_serializer_header()
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            var request = new RestRequest("owned");
            request.AddHeader("Accept", "text/html, application/json");

            rest.Execute(request).StatusCode.Should().Be(HttpStatusCode.OK);
            server.Accept.Should().Be("text/html, application/json");
        }

        [Test]
        public async Task socket_failure_is_not_misclassified_as_expected_http_error()
        {
            await using var server = await OwnedListener.Start();
            using var rest = TestRestClient.Create(server.BaseUrl);
            await server.Stop();
            var response = rest.Execute(new RestRequest("owned"));

            response.ResponseStatus.Should().NotBe(ResponseStatus.Completed);
            response.ErrorException.Should().NotBeNull();
        }

        public class OwnedResource : RestResource
        {
            public string Name { get; set; }
            public string NullValue { get; set; }
            public bool Allowed { get; set; }
            public int Count { get; set; }
            public DayOfWeek Kind { get; set; }
        }

        // Own the actual bound port and every request. No application process,
        // credentials, DNS, external HTTP, or existing database is involved.
        private sealed class OwnedListener : IAsyncDisposable
        {
            private readonly WebApplication _app;

            private OwnedListener(WebApplication app)
            {
                _app = app;
            }

            public string BaseUrl => _app.Urls.Single() + "/api/v1/";
            public string Method { get; private set; }
            public string Path { get; private set; }
            public string QueryTerm { get; private set; }
            public string QueryFilter { get; private set; }
            public string Page { get; private set; }
            public string[] ApiKeys { get; private set; }
            public string Authorization { get; private set; }
            public string Accept { get; private set; }
            public string ContentType { get; private set; }
            public string Body { get; private set; }

            public static async Task<OwnedListener> Start()
            {
                var builder = WebApplication.CreateBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
                var app = builder.Build();
                var owner = new OwnedListener(app);
                app.Run(owner.Respond);
                await app.StartAsync();
                return owner;
            }

            public Task Stop() => _app.StopAsync();

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }

            private async Task Respond(HttpContext context)
            {
                var request = context.Request;
                Method = request.Method;
                Path = request.Path;
                QueryTerm = request.Query["term"];
                QueryFilter = request.Query["filter"];
                Page = request.Query["page"];
                ApiKeys = request.Headers["X-Api-Key"].ToArray();
                Authorization = request.Headers.Authorization;
                Accept = request.Headers.Accept;
                ContentType = request.ContentType;
                using var reader = new System.IO.StreamReader(request.Body);
                Body = await reader.ReadToEndAsync();

                context.Response.StatusCode = int.TryParse(request.Query["status"], out var status) ? status : HttpMethods.IsPost(request.Method) ? 201 : 200;
                context.Response.Headers.CacheControl = "no-store, no-cache";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "-1";
                if (request.Query.ContainsKey("page"))
                {
                    await context.Response.WriteAsJsonAsync(new { Records = new[] { new OwnedResource { Id = 7 } } });
                }
                else
                {
                    await context.Response.WriteAsJsonAsync(new OwnedResource { Id = 7 });
                }
            }
        }
    }
}
