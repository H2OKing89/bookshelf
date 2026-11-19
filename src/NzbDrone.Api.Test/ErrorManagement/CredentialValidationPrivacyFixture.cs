using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using Readarr.Http.ErrorManagement;

namespace NzbDrone.Api.Test.ErrorManagement
{
    [TestFixture]
    public class CredentialValidationPrivacyFixture
    {
        [TestCase("Password")]
        [TestCase("PasswordConfirmation")]
        [TestCase("ProxyPassword")]
        public async Task credential_validation_should_not_expose_attempted_values_or_formatter_state(string property)
        {
            const string secret = "private-password-validation-canary";
            var failure = new ValidationFailure(property, secret, secret)
            {
                ErrorCode = secret,
                CustomState = secret,
                FormattedMessagePlaceholderValues = new Dictionary<string, object> { { "PropertyValue", secret } }
            };
            var target = new MemoryTarget { Layout = "${message}|${exception:format=ToString}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Warn, LogLevel.Fatal, target);
            using var factory = new LogFactory { Configuration = configuration };
            var pipeline = new ReadarrErrorPipeline(factory.GetLogger("CredentialValidation"));
            var context = new DefaultHttpContext();
            using var body = new MemoryStream();
            context.Response.Body = body;
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = new ValidationException(new[] { failure }) });

            await pipeline.HandleException(context);
            factory.Flush(TimeSpan.FromSeconds(5));

            context.Response.StatusCode.Should().Be(400);
            Encoding.UTF8.GetString(body.ToArray()).Should().Contain(property).And.NotContain(secret);
            target.Logs.Should().ContainSingle();
            target.Logs[0].Should().NotContain(secret);
        }
    }
}
