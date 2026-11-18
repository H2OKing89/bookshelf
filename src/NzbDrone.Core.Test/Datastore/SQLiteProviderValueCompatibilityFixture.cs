using System;
using System.Data.SQLite;
using System.Globalization;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    [NonParallelizable]
    [Platform(Exclude = "Win")]
    public class SQLiteProviderValueCompatibilityFixture : CoreTest
    {
        private IDatabase _database;
        private string _connectionString;
        private MigrationContext _previousMigrationContext;

        [SetUp]
        public void Setup()
        {
            _previousMigrationContext = MigrationContext.Current;
            WithTempAsAppPath();
            var config = Mocker.GetMock<IConfigFileProvider>();
            config.SetupGet(provider => provider.PostgresHost).Returns(string.Empty);
            var connections = new ConnectionStringFactory(TestFolderInfo, config.Object);
            _connectionString = connections.MainDbConnection.ConnectionString;
            Mocker.SetConstant<IConnectionStringFactory>(connections);
            Mocker.SetConstant<ILoggerProvider>(NullLoggerProvider.Instance);
            Mocker.SetConstant<IMigrationController>(Mocker.Resolve<MigrationController>());
            _database = Mocker.Resolve<DbFactory>().Create(MigrationType.Main);
        }

        [TearDown]
        public void Cleanup()
        {
            MigrationContext.Current = _previousMigrationContext;
            SQLiteConnection.ClearAllPools();
        }

        [TestCase("fr-FR", "America/Chicago")]
        [TestCase("de-DE", "America/Chicago")]
        [TestCase("fr-FR", "Asia/Kathmandu")]
        [TestCase("de-DE", "Asia/Kathmandu")]
        public void production_repositories_should_preserve_text_guids_and_fractional_utc_dates(string culture, string timezone)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalTimezone = Environment.GetEnvironmentVariable("TZ");

            try
            {
                Environment.SetEnvironmentVariable("TZ", timezone);
                TimeZoneInfo.ClearCachedData();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;

                // Exercise UTC and local values across the Chicago fall-back boundary.
                var instant = new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc).AddTicks(1234567);
                TimeZoneInfo.Local.GetUtcOffset(instant).Should().NotBe(TimeSpan.Zero);
                var laterInstant = instant.AddHours(1).AddTicks(765432);
                var local = laterInstant.ToLocalTime();
                local.Kind.Should().Be(DateTimeKind.Local);
                local.ToUniversalTime().Should().Be(laterInstant);

                var options = new SQLiteConnectionStringBuilder(_connectionString);
                options.ContainsKey("binaryguid").Should().BeFalse("the test must retain the production connection contract");
                options.DateTimeKind.Should().Be(DateTimeKind.Utc);
                options.ForeignKeys.Should().BeFalse("the bundled engine must retain the legacy connection policy");

                var events = Mocker.GetMock<IEventAggregator>().Object;
                var users = new BasicRepository<User>(_database, events);
                var tasks = new BasicRepository<ScheduledTask>(_database, events);
                var identity = Guid.Parse("3375ca62-96d5-4e58-a902-d286d890c425");
                var user = users.Insert(new User
                {
                    Identifier = identity,
                    Username = "Synthetic SQLite compatibility user",
                    Password = "Synthetic credential value",
                    Salt = "Synthetic salt",
                    Iterations = 10000
                });
                var task = tasks.Insert(new ScheduledTask
                {
                    TypeName = "Synthetic.SQLiteCompatibilityTask",
                    Interval = 30,
                    LastExecution = instant,
                    LastStartTime = local
                });

                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture == "fr-FR" ? "de-DE" : "fr-FR");
                CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;

                var storedUser = users.Get(user.Id);
                storedUser.Identifier.Should().Be(identity);
                storedUser.Id.Should().Be(user.Id);
                var storedTask = tasks.Get(task.Id);
                storedTask.LastExecution.Kind.Should().Be(DateTimeKind.Utc);
                storedTask.LastExecution.Ticks.Should().Be(instant.Ticks);
                storedTask.LastStartTime.Kind.Should().Be(DateTimeKind.Utc);
                storedTask.LastStartTime.Ticks.Should().Be(laterInstant.Ticks);

                using var connection = _database.OpenConnection();
                connection.QuerySingle<int>("PRAGMA foreign_keys").Should().Be(0);
                connection.QuerySingle<string>("SELECT typeof(\"Identifier\") FROM \"Users\" WHERE \"Id\" = @Id", new { user.Id })
                    .Should().Be("text", "the production GUID converter must preserve the existing TEXT representation");
                connection.QuerySingle<string>("SELECT CAST(\"Identifier\" AS TEXT) FROM \"Users\" WHERE \"Id\" = @Id", new { user.Id })
                    .Should().Be(identity.ToString());
                connection.QuerySingle<string>("SELECT typeof(\"LastExecution\") FROM \"ScheduledTasks\" WHERE \"Id\" = @Id", new { task.Id })
                    .Should().Be("text");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                Environment.SetEnvironmentVariable("TZ", originalTimezone);
                TimeZoneInfo.ClearCachedData();
            }
        }
    }
}
