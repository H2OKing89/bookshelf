using System;
using System.Linq;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookCatalogPolicy
    {
        public static bool IsRecording(AudnexusBookResource resource)
        {
            if (resource == null)
            {
                return false;
            }

            // Original recordings include legitimate audiobooks. Only an explicit
            // non-book product type excludes an item; titles and missing runtime
            // are not reliable evidence, particularly for preorders.
            return new[] { resource.ContentType, resource.ContentDeliveryType, resource.PublicationType, resource.FormatType }
                .All(value => !IsNonBookType(value));
        }

        private static bool IsNonBookType(string value)
        {
            var type = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            return type.StartsWith("podcast", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("episode", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("radio", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("radioprogram", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("music", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("newspaper", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("magazine", StringComparison.OrdinalIgnoreCase);
        }
    }
}
