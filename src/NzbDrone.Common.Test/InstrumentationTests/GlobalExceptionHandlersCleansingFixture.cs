using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    [NonParallelizable]
    public class GlobalExceptionHandlersCleansingFixture
    {
        private const string Secret = "emergency-console-secret-12345";
        private const string Failure = "(removed, log message could not be safely cleansed)";
        private TextWriter _previousConsole;
        private LoggingConfiguration _previousConfiguration;
        private bool _previousThrowExceptions;
        private StringWriter _console;
        private RecordingTarget _target;

        [SetUp]
        public void Setup()
        {
            NzbDroneLogger.GetLogger(typeof(GlobalExceptionHandlers));
            _previousConsole = Console.Out;
            _previousConfiguration = LogManager.Configuration;
            _previousThrowExceptions = LogManager.ThrowExceptions;
            _console = new StringWriter();
            _target = new RecordingTarget { Name = "emergency-capture" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, _target, nameof(GlobalExceptionHandlers));
            LogManager.Configuration = configuration;
            LogManager.ThrowExceptions = true;
            Console.SetOut(_console);
        }

        [TearDown]
        public void Teardown()
        {
            try
            {
                Console.SetOut(_previousConsole);
                LogManager.ThrowExceptions = _previousThrowExceptions;
                LogManager.Configuration = _previousConfiguration;
            }
            finally
            {
                _console.Dispose();
            }
        }

        [Test]
        public void should_cleanse_app_domain_output_once_and_preserve_the_fatal_exception()
        {
            var exception = new CountingNullReferenceException("ordinary context ?apikey=" + Secret);

            InvokeHandler("HandleAppDomainException", new UnhandledExceptionEventArgs(exception, true));

            _console.ToString().Should().Contain("EPIC FAIL: ordinary context ?apikey=(removed)").And.NotContain(Secret);
            exception.RenderCount.Should().Be(1);
            _target.Events.Should().HaveCount(1);
            _target.Events[0].Level.Should().Be(LogLevel.Fatal);
            _target.Events[0].Message.Should().Be("EPIC FAIL.");
            ReferenceEquals(_target.Events[0].Exception, exception).Should().BeTrue();
        }

        [Test]
        public void should_cleanse_task_output_and_preserve_the_error_exception()
        {
            var exception = new AggregateException("task context ?access_token=" + Secret);

            InvokeHandler("HandleTaskException", new UnobservedTaskExceptionEventArgs(exception));

            _console.ToString().Should().Contain("Task Error: ").And.Contain("task context ?access_token=(removed)")
                .And.NotContain(Secret);
            _target.Events.Should().HaveCount(1);
            _target.Events[0].Level.Should().Be(LogLevel.Error);
            _target.Events[0].Message.Should().Be("Task Error");
            ReferenceEquals(_target.Events[0].Exception, exception).Should().BeTrue();
        }

        [Test]
        public void should_keep_fatal_classification_when_exception_rendering_returns_null()
        {
            var exception = new CountingNullReferenceException(null);

            InvokeHandler("HandleAppDomainException", new UnhandledExceptionEventArgs(exception, true));

            _console.ToString().Should().Be("EPIC FAIL: " + Environment.NewLine).And.NotContain(Secret);
            exception.RenderCount.Should().Be(1);
            _target.Events.Should().HaveCount(1);
            _target.Events[0].Level.Should().Be(LogLevel.Fatal);
            ReferenceEquals(_target.Events[0].Exception, exception).Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_use_a_constant_when_exception_rendering_throws_without_retrying(bool task)
        {
            var failure = new ThrowingToStringException();
            var exception = task ? new AggregateException(failure) : (Exception)failure;

            if (task)
            {
                InvokeHandler("HandleTaskException", new UnobservedTaskExceptionEventArgs((AggregateException)exception));
            }
            else
            {
                InvokeHandler("HandleAppDomainException", new UnhandledExceptionEventArgs(exception, true));
            }

            _console.ToString().Should().Be((task ? "Task Error: " : "EPIC FAIL: ") + Failure + Environment.NewLine);
            failure.RenderCount.Should().Be(1);
            _target.Events.Should().HaveCount(1);
            _target.Events[0].Level.Should().Be(task ? LogLevel.Error : LogLevel.Fatal);
            ReferenceEquals(_target.Events[0].Exception, exception).Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase("not an exception")]
        public void should_ignore_null_and_non_exception_app_domain_values(object value)
        {
            InvokeHandler("HandleAppDomainException", new UnhandledExceptionEventArgs(value, true));

            _console.ToString().Should().BeEmpty();
            _target.Events.Should().BeEmpty();
        }

        [Test]
        public void should_keep_the_legacy_heartbeat_warning_without_console_or_fatal_output()
        {
            var exception = new CountingNullReferenceException(
                "Microsoft.AspNet.SignalR.Transports.TransportHeartbeat.ProcessServerCommand ?apikey=" + Secret);

            InvokeHandler("HandleAppDomainException", new UnhandledExceptionEventArgs(exception, false));

            exception.RenderCount.Should().Be(1);
            _console.ToString().Should().BeEmpty();
            _target.Events.Should().HaveCount(1);
            _target.Events[0].Level.Should().Be(LogLevel.Warn);
            _target.Events[0].Message.Should().Be("SignalR Heartbeat interrupted");
            _target.Events[0].Exception.Should().BeNull();
        }

        private static void InvokeHandler(string name, EventArgs arguments)
        {
            var handler = typeof(GlobalExceptionHandlers).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            handler.Invoke(null, new object[] { null, arguments });
        }

        public class RecordingTarget : Target
        {
            public List<LogEventInfo> Events { get; } = new List<LogEventInfo>();

            protected override void Write(LogEventInfo logEvent)
            {
                Events.Add(logEvent);
            }
        }

        public class CountingNullReferenceException : NullReferenceException
        {
            private readonly string _text;

            public CountingNullReferenceException(string text)
                : base(text ?? "null renderer ?apikey=" + Secret)
            {
                _text = text;
            }

            public int RenderCount { get; private set; }

            public override string ToString()
            {
                RenderCount++;
                return _text;
            }
        }

        public class ThrowingToStringException : NullReferenceException
        {
            public int RenderCount { get; private set; }

            public override string ToString()
            {
                RenderCount++;
                throw new InvalidOperationException("?apikey=" + Secret);
            }
        }
    }
}
