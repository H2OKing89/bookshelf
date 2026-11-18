using Microsoft.Extensions.Configuration;

namespace NzbDrone.Core.Datastore
{
    public class PostgresOptions
    {
        public string Host { get; set; }
        public int? Port { get; set; }

        // Nullable binding treats an explicit empty port as null. Keep its source presence.
        [ConfigurationKeyName(nameof(Port))]
        public string ConfiguredPort { get; set; }

        public string User { get; set; }
        public string Password { get; set; }
        public string MainDb { get; set; }
        public string LogDb { get; set; }
        public string CacheDb { get; set; }
        public string MainDbConnectionString { get; set; }
        public string LogDbConnectionString { get; set; }
        public string CacheDbConnectionString { get; set; }

        public static PostgresOptions GetOptions()
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var postgresOptions = new PostgresOptions();
            config.GetSection("Readarr:Postgres").Bind(postgresOptions);

            return postgresOptions;
        }
    }
}
