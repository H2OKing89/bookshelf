using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    public static class AudiobookContributors
    {
        private static readonly Regex RoleSuffix = new Regex(@"\s+-\s+(?<role>author|narrator|translator|editor|illustrator|adapter|adaptation|foreword|afterword|introduction|contributor|compiler|performer)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        public static AudiobookContributor Credit(AudnexusContributorResource source, string defaultRole)
        {
            var name = AudiobookTextNormalizer.Text(source?.Name);
            if (name == null)
            {
                return null;
            }

            var suffix = RoleSuffix.Match(name);
            var role = AudiobookTextNormalizer.Text(source.Role) ?? (suffix.Success ? suffix.Groups["role"].Value : defaultRole);
            if (suffix.Success)
            {
                name = AudiobookTextNormalizer.Text(name.Substring(0, suffix.Index));
            }

            if (name == null)
            {
                return null;
            }

            role = role?.ToLowerInvariant();
            role = role == "adaptation" ? "adapter" : role;
            return new AudiobookContributor
            {
                Name = name,
                Asin = AudiobookId.IsAsin(source.Asin) ? source.Asin : null,
                Role = string.IsNullOrEmpty(role) ? defaultRole : char.ToUpperInvariant(role[0]) + role.Substring(1)
            };
        }

        public static AudnexusContributorResource PrimaryAuthor(AudnexusBookResource resource)
        {
            return resource?.Authors?.Select(source => Credit(source, "Author"))
                .Where(credit => credit?.Role == "Author")
                .Select(credit => new AudnexusContributorResource { Name = credit.Name, Asin = credit.Asin, Role = credit.Role })
                .FirstOrDefault();
        }

        public static List<AudiobookContributor> All(AudnexusBookResource resource)
        {
            return (resource.Authors ?? new List<AudnexusContributorResource>()).Select(source => Credit(source, "Author"))
                .Concat((resource.Narrators ?? new List<AudnexusContributorResource>()).Select(source => Credit(source, "Narrator")))
                .Where(credit => credit != null)
                .DistinctBy(credit => (credit.Asin ?? credit.Name.ToUpperInvariant()) + ":" + credit.Role)
                .ToList();
        }
    }
}
