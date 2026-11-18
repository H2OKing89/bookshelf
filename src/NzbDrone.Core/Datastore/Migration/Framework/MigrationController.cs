using System;
using System.Diagnostics;
using System.Reflection;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public interface IMigrationController
    {
        void Migrate(string connectionString, MigrationContext migrationContext, DatabaseType databaseType);
    }

    public class MigrationController : IMigrationController
    {
        private readonly Logger _logger;
        private readonly ILoggerProvider _migrationLoggerProvider;

        public MigrationController(Logger logger,
                                   ILoggerProvider migrationLoggerProvider)
        {
            _logger = logger;
            _migrationLoggerProvider = migrationLoggerProvider;
        }

        public void Migrate(string connectionString, MigrationContext migrationContext, DatabaseType databaseType)
        {
            var sw = Stopwatch.StartNew();

            _logger.Info("*** Migrating {0} {1} database ***", databaseType, migrationContext.MigrationType);

            var db = databaseType switch
            {
                DatabaseType.SQLite => ProcessorIdConstants.SQLite,
                DatabaseType.PostgreSQL => ProcessorIdConstants.PostgreSQL,
                _ => throw new NotSupportedException($"Unknown database type: {databaseType}")
            };

            // Runner diagnostics can embed unlabelled server authentication
            // fields. Our migration classes still report stages and versions;
            // DbFactory reports only safe PostgreSQL failure codes.
            using var serviceProvider = new ServiceCollection()
                .AddLogging(b => b.AddProvider(databaseType == DatabaseType.PostgreSQL
                    ? NullLoggerProvider.Instance
                    : _migrationLoggerProvider))
                .AddFluentMigratorCore()
                .Configure<RunnerOptions>(cfg => cfg.IncludeUntaggedMaintenances = true)
                .ConfigureRunner(
                    builder => builder
                    .AddNzbDronePostgres()
                    .AddNzbDroneSQLite()
                    .WithGlobalConnectionString(connectionString)
                    .ScanIn(Assembly.GetExecutingAssembly()).For.All())
                .Configure<TypeFilterOptions>(opt => opt.Namespace = "NzbDrone.Core.Datastore.Migration")
                .Configure<ProcessorOptions>(opt =>
                {
                    opt.PreviewOnly = false;
                    opt.Timeout = TimeSpan.FromMinutes(5);
                })
                .Configure<SelectingProcessorAccessorOptions>(cfg =>
                {
                    cfg.ProcessorId = db;
                })
                .Configure<SelectingGeneratorAccessorOptions>(cfg =>
                {
                    cfg.GeneratorId = db;
                })
                .BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

                var previousContext = MigrationContext.Current;
                MigrationContext.Current = migrationContext;
                try
                {
                    if (migrationContext.DesiredVersion.HasValue)
                    {
                        runner.MigrateUp(migrationContext.DesiredVersion.Value);
                    }
                    else
                    {
                        runner.MigrateUp();
                    }
                }
                finally
                {
                    MigrationContext.Current = previousContext;
                }
            }

            sw.Stop();

            _logger.Debug("Took: {0}", sw.Elapsed);
        }
    }
}
