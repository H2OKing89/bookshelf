using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Http.Authentication;

namespace NzbDrone.Integration.Test.Authentication
{
    [TestFixture]
    public class ApiKeyAuthenticationFixture : TestBase
    {
        private const string Key = "Configured-API-Key-0123456789";
        private const string WrongKey = "incorrect-api-key-9876543210";
        private const string RotatedKey = "rotated-api-key-abcdef012345";
        private string _configuredKey;
        private List<string> _messages;
        private HashSet<string> _sensitiveKeys;
        private ILoggerFactory _loggerFactory;

        [SetUp]
        public void Setup()
        {
            _configuredKey = Key;
            _messages = new List<string>();
            _sensitiveKeys = new HashSet<string>(StringComparer.Ordinal) { Key, WrongKey, RotatedKey };
            _loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddProvider(new CapturingLoggerProvider(_messages)));
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.ApiKey).Returns(() => _configuredKey);
        }

        [TearDown]
        public void VerifyCredentialLogging()
        {
            _loggerFactory.Dispose();
            var output = string.Join(Environment.NewLine, _messages);
            foreach (var key in _sensitiveKeys.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                output.Should().NotContain(key);
            }
        }

        [TestCase("API", "query")]
        [TestCase("API", "header")]
        [TestCase("API", "bearer")]
        [TestCase("SignalR", "query")]
        [TestCase("SignalR", "header")]
        [TestCase("SignalR", "bearer")]
        public async Task should_authenticate_supported_transports_with_the_existing_ticket_and_claim(string scheme, string transport)
        {
            var result = await Authenticate(scheme, CredentialContext(scheme, transport, Key));

            VerifySuccess(result);
        }

        [TestCase("API", "header", null)]
        [TestCase("API", "header", "")]
        [TestCase("API", "header", " \t ")]
        [TestCase("API", "header", WrongKey)]
        [TestCase("API", "header", "configured-api-key-0123456789")]
        [TestCase("SignalR", "query", null)]
        [TestCase("SignalR", "query", "")]
        [TestCase("SignalR", "query", " \t ")]
        [TestCase("SignalR", "query", WrongKey)]
        [TestCase("SignalR", "query", "configured-api-key-0123456789")]
        public async Task should_return_no_result_for_missing_blank_or_incorrect_credentials(string scheme, string transport, string supplied)
        {
            VerifyNoResult(await Authenticate(scheme, CredentialContext(scheme, transport, supplied)));
        }

        [TestCase("API", Key, WrongKey, true)]
        [TestCase("API", WrongKey, Key, false)]
        [TestCase("API", "", Key, false)]
        [TestCase("API", " ", Key, false)]
        [TestCase("SignalR", Key, WrongKey, true)]
        [TestCase("SignalR", WrongKey, Key, false)]
        [TestCase("SignalR", "", Key, false)]
        [TestCase("SignalR", " ", Key, false)]
        public async Task query_value_should_take_precedence_without_falling_back_to_headers(string scheme, string query, string header, bool succeeds)
        {
            var context = CredentialContext(scheme, "query", query);
            context.Request.Headers["X-Api-Key"] = header;
            context.Request.Headers["Authorization"] = "Bearer " + header;

            VerifyResult(await Authenticate(scheme, context), succeeds);
        }

        [TestCase("API", Key, WrongKey, true)]
        [TestCase("SignalR", Key, WrongKey, true)]
        [TestCase("API", WrongKey, Key, false)]
        [TestCase("API", "", Key, false)]
        public async Task api_key_header_should_take_precedence_without_falling_back_to_authorization(string scheme, string header, string bearer, bool succeeds)
        {
            var context = CredentialContext(scheme, "header", header);
            context.Request.Headers["Authorization"] = "Bearer " + bearer;

            VerifyResult(await Authenticate(scheme, context), succeeds);
        }

        [TestCase("API", "query", Key, WrongKey, true)]
        [TestCase("API", "query", WrongKey, Key, false)]
        [TestCase("SignalR", "query", "", Key, false)]
        [TestCase("API", "header", "", Key, false)]
        [TestCase("API", "authorization", WrongKey, Key, false)]
        public async Task repeated_values_should_use_only_the_first_value(string scheme, string transport, string first, string second, bool succeeds)
        {
            var context = new DefaultHttpContext();
            var values = new StringValues(new[] { first, second });
            if (transport == "query")
            {
                context.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { QueryName(scheme), values } });
            }
            else
            {
                context.Request.Headers[transport == "header" ? "X-Api-Key" : "Authorization"] = values;
            }

            VerifyResult(await Authenticate(scheme, context), succeeds);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t ")]
        public async Task should_reject_missing_or_blank_configured_key(string configured)
        {
            _configuredKey = configured;

            VerifyNoResult(await Authenticate("API", CredentialContext("API", "header", Key)));
        }

        [Test]
        public async Task rotation_after_handler_initialization_should_reject_old_key_and_accept_new_key()
        {
            var oldHandler = await CreateHandler("API", CredentialContext("API", "header", Key));
            var newHandler = await CreateHandler("API", CredentialContext("API", "header", RotatedKey));
            _configuredKey = RotatedKey;

            VerifyNoResult(await oldHandler.AuthenticateAsync());
            VerifySuccess(await newHandler.AuthenticateAsync());

            Mocker.GetMock<IConfigFileProvider>().VerifyGet(config => config.ApiKey, Times.Exactly(2));
        }

        [TestCase("API")]
        [TestCase("SignalR")]
        public async Task should_preserve_unauthorized_challenge_and_forbidden_response(string scheme)
        {
            var context = new DefaultHttpContext();
            var handler = await CreateHandler(scheme, context);

            await handler.ChallengeAsync(new AuthenticationProperties());

            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            context.Response.Headers.Should().NotContainKey("WWW-Authenticate");

            await handler.ForbidAsync(new AuthenticationProperties());

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            context.Response.Headers.Should().NotContainKey("WWW-Authenticate");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public async Task should_compare_exact_ordinal_strings_without_normalizing_trimming_or_replacing_surrogates(int scenario)
        {
            var cases = new[]
            {
                (Configured: "surrogate-key-\ud800", Supplied: "surrogate-key-\ud800", Succeeds: true),
                (Configured: "surrogate-key-\ud800", Supplied: "surrogate-key-\ud801", Succeeds: false),
                (Configured: "surrogate-key-\ud800", Supplied: "surrogate-key-\ufffd", Succeeds: false),
                (Configured: "unicode-key-🔑", Supplied: "unicode-key-🔑", Succeeds: true),
                (Configured: "réader-api-key", Supplied: "re\u0301ader-api-key", Succeeds: false),
                (Configured: " padded-api-key ", Supplied: "padded-api-key", Succeeds: false),
                (Configured: " padded-api-key ", Supplied: " padded-api-key ", Succeeds: true)
            };
            var selected = cases[scenario];
            _configuredKey = selected.Configured;
            _sensitiveKeys.Add(selected.Configured);
            _sensitiveKeys.Add(selected.Supplied);

            VerifyResult(await Authenticate("API", CredentialContext("API", "header", selected.Supplied)), selected.Succeeds);
        }

        [TestCase(Key, true)]
        [TestCase("bearer " + Key, false)]
        [TestCase("Bearer Bearer " + Key, true)]
        public async Task authorization_should_preserve_legacy_literal_bearer_replacement(string authorization, bool succeeds)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = authorization;

            VerifyResult(await Authenticate("API", context), succeeds);
        }

        [Test]
        public async Task handler_logs_should_be_captured_without_configured_or_supplied_credentials()
        {
            VerifyNoResult(await Authenticate("API", CredentialContext("API", "header", WrongKey)));
            VerifySuccess(await Authenticate("API", CredentialContext("API", "bearer", Key)));

            _messages.Should().NotBeEmpty();
        }

        private async Task<AuthenticateResult> Authenticate(string scheme, DefaultHttpContext context)
        {
            var handler = await CreateHandler(scheme, context);
            return await handler.AuthenticateAsync();
        }

        private async Task<ApiKeyAuthenticationHandler> CreateHandler(string scheme, DefaultHttpContext context)
        {
            var options = new Mock<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
            options.Setup(monitor => monitor.Get(scheme)).Returns(new ApiKeyAuthenticationOptions
            {
                HeaderName = "X-Api-Key",
                QueryName = QueryName(scheme)
            });
            var handler = new ApiKeyAuthenticationHandler(
                options.Object,
                _loggerFactory,
                UrlEncoder.Default,
                new SystemClock(),
                Mocker.GetMock<IConfigFileProvider>().Object);
            await handler.InitializeAsync(new AuthenticationScheme(scheme, null, typeof(ApiKeyAuthenticationHandler)), context);
            return handler;
        }

        private DefaultHttpContext CredentialContext(string scheme, string transport, string value)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = scheme == "SignalR" ? "/signalr/messages" : "/api/v1/book";
            if (value == null)
            {
                return context;
            }

            _sensitiveKeys.Add(value);
            if (transport == "query")
            {
                context.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { QueryName(scheme), value } });
            }
            else
            {
                context.Request.Headers[transport == "header" ? "X-Api-Key" : "Authorization"] =
                    transport == "bearer" ? "Bearer " + value : value;
            }

            return context;
        }

        private static string QueryName(string scheme) => scheme == "SignalR" ? "access_token" : "apikey";

        private static void VerifyResult(AuthenticateResult result, bool succeeds)
        {
            if (succeeds)
            {
                VerifySuccess(result);
            }
            else
            {
                VerifyNoResult(result);
            }
        }

        private static void VerifySuccess(AuthenticateResult result)
        {
            result.Succeeded.Should().BeTrue();
            result.Failure.Should().BeNull();
            result.Ticket.AuthenticationScheme.Should().Be(ApiKeyAuthenticationOptions.DefaultScheme);
            result.Principal.Identity.IsAuthenticated.Should().BeTrue();
            result.Principal.Identity.AuthenticationType.Should().Be(ApiKeyAuthenticationOptions.DefaultScheme);
            var claim = result.Principal.Claims.Single();
            claim.Type.Should().Be("ApiKey");
            claim.Value.Should().Be("true");
        }

        private static void VerifyNoResult(AuthenticateResult result)
        {
            result.Succeeded.Should().BeFalse();
            result.None.Should().BeTrue();
            result.Failure.Should().BeNull();
            result.Principal.Should().BeNull();
            result.Ticket.Should().BeNull();
        }

        private class CapturingLoggerProvider : ILoggerProvider
        {
            private readonly List<string> _messages;

            public CapturingLoggerProvider(List<string> messages)
            {
                _messages = messages;
            }

            public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

            public void Dispose()
            {
            }
        }

        private class CapturingLogger : ILogger
        {
            private readonly List<string> _messages;

            public CapturingLogger(List<string> messages)
            {
                _messages = messages;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _messages.Add(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object>> properties)
                {
                    _messages.AddRange(properties.Select(property => property.Value?.ToString() ?? string.Empty));
                }

                if (exception != null)
                {
                    _messages.Add(exception.ToString());
                }
            }
        }
    }
}
