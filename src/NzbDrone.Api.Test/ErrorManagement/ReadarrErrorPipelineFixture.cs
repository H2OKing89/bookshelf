using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;
using Readarr.Http.ErrorManagement;

namespace NzbDrone.Api.Test.ErrorManagement
{
    [TestFixture]
    public class ReadarrErrorPipelineFixture : TestBase<ReadarrErrorPipeline>
    {
        [OneTimeSetUp]
        public void InitializeLogger()
        {
            // Initialize the application's logger before LoggingTest installs its per-test targets.
            NzbDroneLogger.GetLogger(typeof(ReadarrErrorPipelineFixture));
        }

        [Test]
        public async Task should_return_not_found_when_updated_model_does_not_exist()
        {
            var exception = new ModelNotFoundException(typeof(QualityDefinition), 12345);

            await VerifyResponse(exception, "PUT", HttpStatusCode.NotFound);
        }

        [TestCase("PUT")]
        [TestCase("POST")]
        public async Task should_return_conflict_when_save_violates_sqlite_constraint(string method)
        {
            var exception = new SQLiteException(SQLiteErrorCode.Constraint, "UNIQUE constraint failed: Tags.Label");

            await VerifyResponse(exception, method, HttpStatusCode.Conflict);

            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase("PUT")]
        [TestCase("POST")]
        public async Task should_return_server_error_when_save_has_non_constraint_sqlite_failure(string method)
        {
            var exception = new SQLiteException(SQLiteErrorCode.ReadOnly, "attempt to write a readonly database");

            await VerifyResponse(exception, method, HttpStatusCode.InternalServerError);

            ExceptionVerification.ExpectedErrors(1);
        }

        private async Task VerifyResponse(Exception exception, string method, HttpStatusCode expectedStatus)
        {
            using (var body = new MemoryStream())
            {
                var context = new DefaultHttpContext();
                context.Request.Method = method;
                context.Request.Path = "/api/v1/qualitydefinition/12345";
                context.Response.Body = body;
                context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
                {
                    Error = exception,
                    Path = context.Request.Path
                });

                await Subject.HandleException(context);

                context.Response.StatusCode.Should().Be((int)expectedStatus);
                context.Response.ContentType.Should().Be("application/json");

                body.Position = 0;
                using (var content = await JsonDocument.ParseAsync(body))
                {
                    content.RootElement.GetProperty("message").GetString().Should().Be(exception.Message);
                }
            }
        }
    }
}
