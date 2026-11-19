using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.HostFiltering;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Host
{
    public static class ConfigureHostFilteringOptions
    {
        public static void Configure(HostFilteringOptions options, IConfigFileProvider config)
        {
            var hosts = AllowedHostsParser.Parse(config.AllowedHosts);
            options.IncludeFailureMessage = false;
            options.AllowEmptyHosts = hosts.Count == 0;
            if (hosts.Count == 0)
            {
                options.AllowedHosts = new List<string> { "*" };
                return;
            }

            // Preserve a local recovery route without widening a configured list
            // to listener wildcards or every machine on the local network.
            hosts.AddRange(new[] { "localhost", "127.0.0.1", "[::1]" });
            if (AllowedHostsParser.IsValidList(Environment.MachineName))
            {
                hosts.AddRange(AllowedHostsParser.Parse(Environment.MachineName));
            }

            options.AllowedHosts = hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
