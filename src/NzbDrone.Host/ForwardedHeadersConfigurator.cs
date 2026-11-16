using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Network;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Host
{
    public static class ForwardedHeadersConfigurator
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(ForwardedHeadersConfigurator));

        public static void Configure(ForwardedHeadersOptions options, IConfigFileProvider configFileProvider)
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = null;

            var trustedNetworks = configFileProvider.TrustedNetworks;
            if (string.IsNullOrWhiteSpace(trustedNetworks))
            {
                return;
            }

            foreach (var entry in trustedNetworks.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (IPNetworkParser.TryParse(entry, out var address, out var prefixLength))
                {
                    // Retain framework loopback trust and use the .NET 6 network API.
                    options.KnownNetworks.Add(new IPNetwork(address, prefixLength));
                    Logger.Info("Trusting forwarded headers from {0}/{1}", address, prefixLength);
                }
                else
                {
                    Logger.Warn("Invalid trusted network '{0}', forwarded headers from it will not be trusted", entry);
                }
            }
        }

        public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app)
        {
            // .NET 6 accepts values without a transport peer, and can erase an empty
            // XFF header when applying only host/scheme. Retain that evidence for auth.
            return app.UseWhen(context => context.Connection.RemoteIpAddress != null &&
                (!context.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName) ||
                 context.Request.Headers.GetCommaSeparatedValues(ForwardedHeadersDefaults.XForwardedForHeaderName)
                     .Any(value => !string.IsNullOrWhiteSpace(value))),
                branch => branch.UseForwardedHeaders());
        }
    }
}
