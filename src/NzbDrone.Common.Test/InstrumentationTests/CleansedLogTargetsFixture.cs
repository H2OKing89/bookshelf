using System;
using System.IO;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    public class CleansedLogTargetsFixture : TestBase
    {
        private const string Secret = "caller-secret-12345";
        private const string Failure = "(removed, log message could not be safely cleansed)";

        [TestCase(false)]
        [TestCase(true)]
        public void should_write_normal_layout_and_redact_secrets_to_a_real_file(bool wrappedLayout)
        {
            var path = Path.Combine(TempFolder, "cleansed.txt");
            WriteFile(path, "GET /bookshelf/api?apikey=" + Secret + "&page=2", wrappedLayout);

            File.ReadAllText(path).TrimEnd().Should().Be("Info|Caller|GET /bookshelf/api?apikey=(removed)&page=2");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_write_only_a_safe_failure_for_oversized_rendered_output(bool wrappedLayout)
        {
            var path = Path.Combine(TempFolder, "oversized.txt");
            WriteFile(path, new string('x', 70000) + "?apikey=" + Secret, wrappedLayout);

            File.ReadAllText(path).TrimEnd().Should().Be(Failure);
        }

        [Test]
        public void wrapper_should_preserve_ordinary_layout_output()
        {
            var entry = new LogEventInfo(LogLevel.Warn, "Caller", "ordinary log message");
            var inner = new SimpleLayout("[${level}] ${logger}: ${message}");

            new CleansedLayout(inner).Render(entry).Should().Be(inner.Render(entry));
        }

        [Test]
        public void wrapper_should_cleanse_message_exception_and_exception_data_together()
        {
            var exception = new InvalidOperationException("https://example.invalid/?apikey=" + Secret);
            exception.Data["Context"] = "password=" + Secret;
            var entry = new LogEventInfo(LogLevel.Error, "Caller", "?access_token=" + Secret) { Exception = exception };
            var layout = new CleansedLayout("${message}|${exception:format=ToString}|${exception:format=Data}");

            var rendered = layout.Render(entry);

            rendered.Should().NotContain(Secret).And.Contain("(removed)");
            exception.Message.Should().Contain(Secret);
            exception.Data["Context"].Should().Be("password=" + Secret);
        }

        [Test]
        public void wrapper_should_not_expose_or_rethrow_an_inner_layout_failure()
        {
            var layout = new CleansedLayout(new ThrowingLayout());

            layout.Render(new LogEventInfo(LogLevel.Error, "Caller", "?apikey=" + Secret)).Should().Be(Failure);
        }

        [Test]
        public void file_target_should_protect_layout_rendering_inside_the_cleansing_boundary()
        {
            using var target = new ExposedFileTarget { Layout = new ThrowingLayout() };

            target.RenderEntry(new LogEventInfo(LogLevel.Error, "Caller", "?apikey=" + Secret)).Should().Be(Failure);
        }

        private static void WriteFile(string path, string message, bool wrappedLayout)
        {
            var inner = new SimpleLayout("${level}|${logger}|${message}");
            var target = wrappedLayout ? new FileTarget() : new NzbDroneFileTarget();
            target.Name = "private-file";
            target.FileName = path;
            target.Layout = wrappedLayout ? new CleansedLayout(inner) : inner;
            target.KeepFileOpen = false;
            target.ConcurrentWrites = false;
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, target);
            using var factory = new LogFactory { ThrowExceptions = true };
            factory.Configuration = configuration;
            factory.GetLogger("Caller").Info(message);
            factory.Flush(TimeSpan.FromSeconds(5));
        }

        public class ThrowingLayout : Layout
        {
            protected override string GetFormattedMessage(LogEventInfo logEvent)
            {
                throw new InvalidOperationException("?apikey=" + Secret);
            }
        }

        public class ExposedFileTarget : NzbDroneFileTarget
        {
            public string RenderEntry(LogEventInfo logEvent)
            {
                var target = new System.Text.StringBuilder();
                RenderFormattedMessage(logEvent, target);
                return target.ToString();
            }
        }
    }
}
