using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MediaFiles
{
    public static class RejectedFileExtensions
    {
        private static readonly Regex ExtensionToken = new Regex(@"^[a-z0-9]{1,16}(\.[a-z0-9]{1,16}){0,2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.Length > 2048)
            {
                throw new ArgumentException("Rejected extensions must be at most 2048 characters");
            }

            var result = new List<string>();
            foreach (var entry in value.Split(','))
            {
                var token = entry.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                if (token.StartsWith(".", StringComparison.Ordinal))
                {
                    token = token.Substring(1);
                }

                if (!ExtensionToken.IsMatch(token))
                {
                    throw new ArgumentException("Use comma-separated file extensions without paths or wildcards");
                }

                result.Add(token.ToLowerInvariant());
            }

            return string.Join(", ", result.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public static bool IsValid(string value)
        {
            try
            {
                Normalize(value);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static HashSet<string> Parse(string value)
        {
            return Normalize(value).Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(extension => "." + extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
