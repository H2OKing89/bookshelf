using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    public class CleanseLogMessageSafetyFixture
    {
        private const int MessageLimit = 64 * 1024;
        private const string Secret = "synthetic-sensitive-credential";
        private const string FailureMessage = "(removed, log message could not be safely cleansed)";

        [TestCase("#")]
        [TestCase(" ")]
        public void oversized_input_should_be_removed_in_full(string repeated)
        {
            var message = string.Concat(Enumerable.Repeat(repeated, MessageLimit + 1));

            CleanseLogMessage.Cleanse(message).Should().Be(FailureMessage);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void oversized_input_should_never_fall_back_to_an_unredacted_prefix(bool secretFirst)
        {
            var padding = new string('#', MessageLimit);
            var message = secretFirst ? Secret + padding : padding + Secret;

            CleanseLogMessage.Cleanse(message).Should().Be(FailureMessage);
        }

        [Test]
        public void ordinary_message_at_the_size_limit_should_be_preserved()
        {
            var message = new string('#', MessageLimit);

            CleanseLogMessage.Cleanse(message).Should().Be(message);
        }

        [Test]
        public void redaction_that_expands_output_beyond_the_limit_should_remove_the_entire_message()
        {
            var message = string.Concat(Enumerable.Repeat("&key=x", 9000));
            message.Length.Should().BeLessThan(MessageLimit);

            CleanseLogMessage.Cleanse(message).Should().Be(FailureMessage);
        }

        [TestCase("a", 60000)]
        [TestCase("a.", 30000)]
        [TestCase("/rss", 12000)]
        [TestCase("&", 60000)]
        [TestCase("?", 60000)]
        public void malformed_long_input_should_finish_promptly_without_exposing_credentials(string repeated, int count)
        {
            var message = string.Concat(Enumerable.Repeat(repeated, count)) + "?apikey=" + Secret;
            var stopwatch = Stopwatch.StartNew();

            var cleansed = CleanseLogMessage.Cleanse(message);

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
            cleansed.Should().NotContain(Secret);
            (cleansed == FailureMessage || cleansed.EndsWith("?apikey=(removed)", StringComparison.Ordinal)).Should().BeTrue();
        }

        [Test]
        public void actual_regex_timeout_should_remove_input_and_pattern_without_throwing()
        {
            var message = new string('a', 2048) + "!" + Secret;
            var regex = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(10));
            var stopwatch = Stopwatch.StartNew();

            var cleansed = CleanseLogMessage.CleanseWithRules(message, new[] { regex });

            cleansed.Should().Be(FailureMessage);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void rules_with_infinite_or_excessive_timeout_should_be_rejected_before_execution(int timeoutMilliseconds)
        {
            var timeout = timeoutMilliseconds < 0 ? Regex.InfiniteMatchTimeout : TimeSpan.FromMilliseconds(timeoutMilliseconds);
            var regex = new Regex("never-matches", RegexOptions.None, timeout);

            CleanseLogMessage.CleanseWithRules("ordinary message", new[] { regex }).Should().Be(FailureMessage);
        }

        [Test]
        public void rendered_event_should_be_cleansed_once_without_changing_the_original_event()
        {
            const string message = "GET https://catalog.example/books?apikey=" + Secret + "&offset=3";
            var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var logEvent = new LogEventInfo(LogLevel.Info, "Catalog", message) { TimeStamp = timestamp };
            var calls = 0;

            var cleansed = CleanseLogMessage.Cleanse(logEvent, entry =>
            {
                calls++;
                entry.Should().BeSameAs(logEvent);
                return $"{entry.TimeStamp:O}|{entry.Level}|{entry.LoggerName}|{entry.FormattedMessage}";
            });

            cleansed.Should().Be(timestamp.ToString("O", CultureInfo.InvariantCulture) +
                "|Info|Catalog|GET https://catalog.example/books?apikey=(removed)&offset=3");
            calls.Should().Be(1);
            logEvent.Message.Should().Be(message);
            logEvent.TimeStamp.Should().Be(timestamp);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t\r\n ")]
        public void rendered_null_empty_and_whitespace_should_be_preserved(string rendered)
        {
            var calls = 0;
            var logEvent = new LogEventInfo(LogLevel.Info, "Test", "Raw " + Secret);

            var cleansed = CleanseLogMessage.Cleanse(logEvent, entry =>
            {
                calls++;
                return rendered;
            });

            cleansed.Should().Be(rendered);
            calls.Should().Be(1);
        }

        [Test]
        public void renderer_failure_should_not_retry_rendering_or_return_raw_event_contents()
        {
            var calls = 0;
            var logEvent = new LogEventInfo(LogLevel.Error, "Test", "Raw " + Secret);

            var cleansed = CleanseLogMessage.Cleanse(logEvent, entry =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("Rendering failed: " + Secret);
                }

                return "Unsafe fallback: " + Secret;
            });

            cleansed.Should().Be(FailureMessage);
            calls.Should().Be(1);
            logEvent.Message.Should().Be("Raw " + Secret);
        }

        [TestCase("format")]
        [TestCase("parameter")]
        [TestCase("exception")]
        public void malformed_log_events_and_throwing_values_should_fail_closed(string failure)
        {
            var logEvent = new LogEventInfo(LogLevel.Error, "Test", "Raw " + Secret + " {0}")
            {
                Parameters = new object[] { new ThrowingValue() },
                Exception = new ThrowingException()
            };
            if (failure == "format")
            {
                logEvent.Message = "Raw " + Secret + " {1}";
                logEvent.Parameters = new object[] { Secret };
            }

            var cleansed = CleanseLogMessage.Cleanse(logEvent, entry => failure == "exception"
                ? entry.Exception.ToString()
                : string.Format(CultureInfo.InvariantCulture, entry.Message, entry.Parameters));

            cleansed.Should().Be(FailureMessage);
        }

        [Test]
        public void rendering_regex_timeout_should_not_expose_exception_input_or_pattern()
        {
            var logEvent = new LogEventInfo(LogLevel.Error, "Test", "Raw " + Secret);

            var cleansed = CleanseLogMessage.Cleanse(logEvent, entry =>
                throw new RegexMatchTimeoutException(Secret, "pattern-" + Secret, TimeSpan.FromMilliseconds(10)));

            cleansed.Should().Be(FailureMessage);
        }

        [Test]
        public void oversized_rendered_output_should_be_removed_without_retrying_renderer()
        {
            var calls = 0;
            var logEvent = new LogEventInfo(LogLevel.Info, "Test", "Raw " + Secret);

            var cleansed = CleanseLogMessage.Cleanse(logEvent, entry =>
            {
                calls++;
                return Secret + new string('#', MessageLimit);
            });

            cleansed.Should().Be(FailureMessage);
            calls.Should().Be(1);
        }

        private class ThrowingValue
        {
            public override string ToString() => throw new InvalidOperationException(Secret);
        }

        private class ThrowingException : Exception
        {
            public override string ToString() => throw new InvalidOperationException(Secret);
        }
    }
}
