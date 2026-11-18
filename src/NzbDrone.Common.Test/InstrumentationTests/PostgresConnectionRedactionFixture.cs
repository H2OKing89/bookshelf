using System;
using System.IO;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    public class PostgresConnectionRedactionFixture : TestBase
    {
        private const string Secret = "database-credential-sentinel";
        private const string Removed = "(removed, database connection configuration)";

        [TestCase("Main")]
        [TestCase("Log")]
        [TestCase("Cache")]
        public void assigned_database_settings_should_be_removed_as_a_whole(string database)
        {
            var connection = "Host=server;Database=books;Password=\"" + Secret + ";suffix\";Application Name=" + Secret;
            var name = "Postgres" + database + "DbConnectionString";
            var messages = new[]
            {
                name + "=" + connection,
                "{\"" + name + "\":\"" + connection + "\"}",
                "<" + name + ">" + connection + "</" + name + ">",
                "Readarr__Postgres__" + database + "DbConnectionString=" + connection,
                "Readarr:Postgres:" + database + "DbConnectionString: " + connection,
                name.ToLowerInvariant() + " = " + Secret + "\nunknown malformed content " + Secret
            };

            foreach (var message in messages)
            {
                CleanseLogMessage.Cleanse(message).Should().Be(Removed);
            }
        }

        [TestCase("Host")]
        [TestCase("Server")]
        [TestCase("Data Source")]
        [TestCase("Database")]
        [TestCase("Initial Catalog")]
        [TestCase("User ID")]
        public void unnamed_connection_strings_should_not_retain_credentials_or_other_values(string firstKey)
        {
            var message = "Operation failed: " + firstKey + " = " + Secret +
                ";Pwd='first;" + Secret + "';SSL Password=\"second;" + Secret + "\";Application Name=" + Secret;

            CleanseLogMessage.Cleanse(message).Should().Be(Removed);
        }

        [TestCase("Password=\"database-credential-sentinel; suffix\" next")]
        [TestCase("Pwd='database-credential-sentinel; suffix' next")]
        [TestCase("SSL Password=\"database-credential-sentinel; suffix\" next")]
        [TestCase("Username = 'database-credential-sentinel; suffix' next")]
        [TestCase("User ID='database-credential-sentinel; suffix' next")]
        [TestCase("Password='database-credential-sentinel''; suffix' next")]
        [TestCase("Password=\"database-credential-sentinel\"\"; suffix\" next")]
        [TestCase("Password='database-credential-sentinel; suffix without terminator")]
        [TestCase("Pwd=\"database-credential-sentinel; suffix without terminator")]
        public void quoted_and_unterminated_credential_values_should_not_leak_a_separator_suffix(string message)
        {
            var cleansed = CleanseLogMessage.Cleanse(message);

            cleansed.Should().NotContain(Secret).And.NotContain("suffix").And.Contain("(removed");
        }

        [TestCase("Invalid PostgresMainDbConnectionString setting.")]
        [TestCase("PostgresLogDbConnectionString is required while database logging is enabled.")]
        [TestCase("Starting PostgreSQL migration for main database")]
        public void diagnostic_messages_naming_a_setting_without_its_value_should_remain_useful(string message)
        {
            CleanseLogMessage.Cleanse(message).Should().Be(message);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void file_targets_should_remove_complete_connection_configuration_in_exception_layouts(bool wrapped)
        {
            var path = Path.Combine(TempFolder, "database-private.txt");
            var inner = "${level}|${message}|${exception:format=ToString}|${exception:format=Data}";
            var target = wrapped ? new FileTarget() : new NzbDroneFileTarget();
            target.Name = "database-privacy";
            target.FileName = path;
            target.KeepFileOpen = false;
            target.Layout = wrapped ? new CleansedLayout(inner) : inner;
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, target);
            var error = new InvalidOperationException("Host=private;Password='" + Secret + ";tail'");
            error.Data["PostgresCacheDbConnectionString"] = "Host=private;Database=" + Secret;
            using var factory = new LogFactory { ThrowExceptions = true, Configuration = configuration };

            factory.GetLogger("DatabasePrivacy").Error(error, "A database operation failed");
            factory.Flush(TimeSpan.FromSeconds(5));

            File.ReadAllText(path).TrimEnd().Should().Be(Removed);
        }
    }
}
