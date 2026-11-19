using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NzbDrone.Common.Network;

namespace NzbDrone.Core.Configuration
{
    public static class AllowedHostsParser
    {
        private static readonly char[] Separators = { ',', ';' };

        public static List<string> Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            var hosts = value.Split(Separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (hosts.Length == 0 || hosts.Any(host => !TryNormalizeHost(host, out _)))
            {
                throw new FormatException("Allowed Hosts must contain host names or IP addresses without ports, schemes, paths or bind addresses. Use an empty value to allow any host.");
            }

            return hosts.Select(host =>
            {
                TryNormalizeHost(host, out var normalized);
                return normalized;
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool IsValidList(string value)
        {
            try
            {
                Parse(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static string NormalizeList(string value)
        {
            return string.Join(", ", Parse(value));
        }

        private static bool TryNormalizeHost(string host, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(host) || host.IndexOfAny(new[] { '/', '\\', '%', '@', '?', '#', '\r', '\n' }) >= 0)
            {
                return false;
            }

            var addressPart = host;
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
            {
                addressPart = host.Substring(1, host.Length - 2);
                if (!addressPart.Contains(':'))
                {
                    return false;
                }
            }

            if (IPAddress.TryParse(addressPart, out var address))
            {
                if (!IPNetworkParser.TryParse(addressPart, out address, out _) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                {
                    return false;
                }

                normalized = address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString();
                return true;
            }

            var wildcard = host.StartsWith("*.", StringComparison.Ordinal);
            var name = wildcard ? host.Substring(2) : host;
            if (IPAddress.TryParse(name, out _) || name.IndexOfAny(new[] { '*', ':', '[', ']' }) >= 0)
            {
                return false;
            }

            try
            {
                var ascii = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(name).ToLowerInvariant();
                if (ascii.Length > 253 || ascii.TrimEnd('.').Split('.').Any(label => label.Length == 0 || label.Length > 63 ||
                    label.StartsWith("-", StringComparison.Ordinal) || label.EndsWith("-", StringComparison.Ordinal) ||
                    label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
                {
                    return false;
                }

                normalized = (wildcard ? "*." : string.Empty) + ascii;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
