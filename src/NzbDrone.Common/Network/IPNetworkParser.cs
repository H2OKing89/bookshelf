using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Common.Network
{
    public static class IPNetworkParser
    {
        public static bool TryParse(string value, out IPAddress address, out int prefixLength)
        {
            address = null;
            prefixLength = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length > 2)
            {
                return false;
            }

            var addressPart = parts[0];
            if (addressPart.IndexOfAny(new[] { '%', '[', ']' }) >= 0 ||
                !IPAddress.TryParse(addressPart, out var parsedAddress))
            {
                return false;
            }

            // IPAddress also accepts abbreviated, octal, hexadecimal and integer IPv4.
            // Trust settings require unambiguous dotted decimal, including embedded IPv4.
            if (addressPart.Contains('.'))
            {
                var ipv4Part = addressPart.Substring(addressPart.LastIndexOf(':') + 1);
                if (!IsDottedDecimal(ipv4Part))
                {
                    return false;
                }
            }
            else if (parsedAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                return false;
            }

            var maxPrefixLength = parsedAddress.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            var parsedPrefixLength = maxPrefixLength;
            if (parts.Length == 2 &&
                (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out parsedPrefixLength) ||
                 parsedPrefixLength < 1 || parsedPrefixLength > maxPrefixLength ||
                 ContainsAllMappedIPv4(parsedAddress, parsedPrefixLength) ||
                 HasHostBitsSet(parsedAddress, parsedPrefixLength)))
            {
                return false;
            }

            address = parsedAddress;
            prefixLength = parsedPrefixLength;
            return true;
        }

        public static bool IsValidList(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .All(entry => TryParse(entry, out _, out _));
        }

        public static string NormalizeList(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty :
                string.Join(", ", value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool IsDottedDecimal(string value)
        {
            var octets = value.Split('.');
            return octets.Length == 4 && octets.All(octet => octet.Length > 0 &&
                (octet.Length == 1 || octet[0] != '0') &&
                byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        }

        private static bool HasHostBitsSet(IPAddress address, int prefixLength)
        {
            var bytes = address.GetAddressBytes();
            for (var i = 0; i < bytes.Length; i++)
            {
                var prefixBitsInByte = prefixLength - (i * 8);
                if (prefixBitsInByte >= 8)
                {
                    continue;
                }

                var hostMask = prefixBitsInByte <= 0 ? 0xFF : 0xFF >> prefixBitsInByte;
                if ((bytes[i] & hostMask) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAllMappedIPv4(IPAddress address, int prefixLength)
        {
            if (address.AddressFamily != AddressFamily.InterNetworkV6 || prefixLength > 96)
            {
                return false;
            }

            // Broad native IPv6 ranges can also include every mapped IPv4 peer.
            var bytes = address.GetAddressBytes();
            var mappedBytes = IPAddress.Parse("::ffff:0.0.0.0").GetAddressBytes();
            for (var i = 0; i < bytes.Length; i++)
            {
                var bits = Math.Clamp(prefixLength - (i * 8), 0, 8);
                var mask = (byte)(0xFF << (8 - bits));
                if ((bytes[i] & mask) != (mappedBytes[i] & mask))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
