using System;
using System.Data.SQLite;
using System.Globalization;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Instrumentation
{
    [TestFixture]
    public class DatabaseTargetRenderingFixture : DbTest<DatabaseTargetRenderingFixture.ExposedDatabaseTarget, Log>
    {
        private const string Secret = "database-render-secret-12345";
        private const string Failure = "(removed, log message could not be safely cleansed)";

        protected override MigrationType MigrationType => MigrationType.Log;

        [Test]
        public void should_preserve_ordinary_message_exception_and_metadata_while_cleansing()
        {
            var exception = new InvalidOperationException("?apikey=" + Secret);
            var entry = new LogEventInfo(LogLevel.Error, "NzbDrone.Caller", "operation failed") { Exception = exception };

            Subject.WriteEntry(entry);

            StoredModel.Message.Should().Be("operation failed: ?apikey=(removed)");
            StoredModel.Exception.Should().NotContain(Secret).And.Contain("(removed)");
            StoredModel.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
            StoredModel.Logger.Should().Be("Caller");
            StoredModel.Level.Should().Be("Error");
            StoredModel.Time.ToUniversalTime().Should().BeCloseTo(entry.TimeStamp.ToUniversalTime());
        }

        [Test]
        public void should_save_a_safe_message_when_exception_message_rendering_throws()
        {
            var entry = new LogEventInfo(LogLevel.Error, "Caller", "operation failed")
            {
                Exception = new ThrowingMessageException()
            };

            Subject.WriteEntry(entry);

            StoredModel.Message.Should().Be(Failure);
            StoredModel.Exception.Should().Be(Failure);
            StoredModel.ExceptionType.Should().Be(typeof(ThrowingMessageException).FullName);
        }

        [Test]
        public void should_cleanse_nlogs_raw_message_fallback_when_the_custom_formatter_throws()
        {
            var entry = new LogEventInfo(LogLevel.Info, "Caller", "raw operation ?apikey=" + Secret)
            {
                MessageFormatter = logEvent => throw new InvalidOperationException("formatting failed ?apikey=" + Secret)
            };

            Subject.WriteEntry(entry);

            StoredModel.Message.Should().Be("raw operation ?apikey=(removed)");
            StoredModel.Exception.Should().BeNull();
            entry.Message.Should().Contain(Secret);
        }

        [Test]
        public void should_keep_the_preformatted_snapshot_without_rendering_a_mutated_parameter_again()
        {
            var parameter = new MutableParameter { Text = "initial ?apikey=" + Secret };
            var entry = new LogEventInfo(
                LogLevel.Info,
                "Caller",
                CultureInfo.InvariantCulture,
                "value {0}",
                new object[] { parameter });
            entry.FormattedMessage.Should().Be("value initial ?apikey=" + Secret);
            var calls = parameter.Calls;
            calls.Should().BeGreaterThan(0);
            parameter.Text = "changed ?apikey=" + Secret;

            Subject.WriteEntry(entry);

            StoredModel.Message.Should().Be("value initial ?apikey=(removed)");
            parameter.Calls.Should().Be(calls);
        }

        [Test]
        public void should_preserve_the_normal_message_format_provider()
        {
            var entry = new LogEventInfo(
                LogLevel.Info,
                "Caller",
                new CultureInfo("fr-FR"),
                "value {0:F2}",
                new object[] { 12.5 });

            Subject.WriteEntry(entry);

            StoredModel.Message.Should().Be("value 12,50");
        }

        [Test]
        public void should_save_a_safe_exception_when_exception_to_string_throws()
        {
            var entry = new LogEventInfo(LogLevel.Error, "Caller", "operation failed")
            {
                Exception = new ThrowingToStringException()
            };

            Subject.WriteEntry(entry);

            StoredModel.Message.Should().Be("operation failed: safe exception message");
            StoredModel.Exception.Should().Be(Failure);
        }

        [Test]
        public void oversized_database_messages_should_be_replaced_without_exposing_the_original()
        {
            Subject.WriteEntry(new LogEventInfo(LogLevel.Info, "Caller", new string('x', 70000) + "?apikey=" + Secret));

            StoredModel.Message.Should().Be(Failure);
            StoredModel.Exception.Should().BeNull();
        }

        [Test]
        public void sqlite_write_failures_should_still_reach_the_caller()
        {
            var connection = new SQLiteConnectionStringBuilder { DataSource = TempFolder }.ConnectionString;
            var factory = new Mock<IConnectionStringFactory>();
            factory.SetupGet(x => x.LogDbConnection).Returns(new DatabaseConnectionInfo(DatabaseType.SQLite, connection));
            using var target = new ExposedDatabaseTarget(factory.Object);

            Assert.Throws<SQLiteException>(() => target.WriteEntry(new LogEventInfo(LogLevel.Info, "Caller", "?apikey=" + Secret)));
        }

        public class ExposedDatabaseTarget : DatabaseTarget
        {
            public ExposedDatabaseTarget(IConnectionStringFactory connectionStringFactory)
                : base(connectionStringFactory)
            {
            }

            public void WriteEntry(LogEventInfo logEvent)
            {
                Write(logEvent);
            }
        }

        public class ThrowingMessageException : Exception
        {
            public override string Message => throw new InvalidOperationException("?apikey=" + Secret);
        }

        public class ThrowingToStringException : Exception
        {
            public ThrowingToStringException()
                : base("safe exception message")
            {
            }

            public override string ToString()
            {
                throw new InvalidOperationException("?apikey=" + Secret);
            }
        }

        public class MutableParameter
        {
            public string Text { get; set; }
            public int Calls { get; private set; }

            public override string ToString()
            {
                Calls++;
                return Text;
            }
        }
    }
}
