using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NLog;
using NLog.Config;
using NLog.Targets;
using Npgsql;
using NUnit.Framework;
using Readarr.Http.ErrorManagement;

namespace NzbDrone.Api.Test.ErrorManagement
{
    [TestFixture]
    public class PostgresErrorPrivacyFixture
    {
        private const string Secret = "private-database-sentinel";
        private const string Message = "PostgreSQL database request failed.";

        [TestCase("direct")]
        [TestCase("server")]
        [TestCase("wrapped")]
        [TestCase("aggregate")]
        [TestCase("throwing")]
        public async Task postgres_failures_should_not_render_provider_values_into_http_or_logs(string kind)
        {
            var provider = new NpgsqlException("Host=private;Password='" + Secret + ";tail'", new IOException(Secret));
            Exception failure = kind switch
            {
                "server" => new PostgresException("role " + Secret + " does not exist", "FATAL", "FATAL", "28000"),
                "wrapped" => new InvalidOperationException(Secret, provider),
                "aggregate" => new AggregateException(Secret, new InvalidOperationException(Secret), provider),
                "throwing" => new ThrowingPostgresException(),
                _ => provider
            };
            var target = new MemoryTarget("raw-diagnostics") { Layout = "${message}|${exception:format=ToString}|${exception:format=Data}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Error, LogLevel.Fatal, target);
            using var factory = new LogFactory { ThrowExceptions = true, Configuration = configuration };
            var pipeline = new ReadarrErrorPipeline(factory.GetLogger("PostgresPrivacy"));
            using var body = new MemoryStream();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/book";
            context.Response.Body = body;
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = failure });

            await pipeline.HandleException(context);
            factory.Flush(TimeSpan.FromSeconds(5));

            context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
            context.Response.ContentType.Should().Be("application/json");
            body.Position = 0;
            using var document = await JsonDocument.ParseAsync(body);
            document.RootElement.GetProperty("message").GetString().Should().Be(Message);
            document.RootElement.GetProperty("description").GetString().Should().Be(Message);
            target.Logs.Should().ContainSingle();
            target.Logs[0].Should().Be(Message + "||").And.NotContain(Secret);
        }

        private class ThrowingPostgresException : NpgsqlException
        {
            public override string Message => throw new InvalidOperationException(Secret);
            public override string ToString() => throw new InvalidOperationException(Secret);
        }
    }
}
