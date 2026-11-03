using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audnexus;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    // These are verified catalog identities, not transliteration rules. Never infer
    // that two authors are the same person from a similar name or a shared series.
    public class AudiobookAuthorIdentity
    {
        private static readonly AudiobookAuthorIdentity[] Verified =
        {
            // https://www.audible.com/series/B0F7Z67917
            // English recordings credit both profiles, and sometimes omit the ASIN.
            new AudiobookAuthorIdentity("us", "B0DSLB5CG5", "Kumanano", new[] { "B074QPMWRL" }, new[] { "くまなの" })
        };

        public string Region { get; }
        public string Asin { get; }
        public string Name { get; }
        public string[] AlternateAsins { get; }
        public string[] Aliases { get; }

        private AudiobookAuthorIdentity(string region, string asin, string name, string[] alternateAsins, string[] aliases)
        {
            Region = region;
            Asin = asin;
            Name = name;
            AlternateAsins = alternateAsins;
            Aliases = aliases;
        }

        public static AudiobookAuthorIdentity Find(AudnexusContributorResource contributor, string region)
        {
            return Verified.SingleOrDefault(identity => identity.Region == region &&
                (AudiobookId.IsAsin(contributor.Asin)
                    ? identity.Asin == contributor.Asin || identity.AlternateAsins.Contains(contributor.Asin)
                    : identity.Names().Contains(AudiobookTextNormalizer.Text(contributor.Name), StringComparer.OrdinalIgnoreCase)));
        }

        public static string CanonicalId(string id)
        {
            var parsed = AudiobookId.Parse(id);
            var identity = Find(new AudnexusContributorResource
            {
                Asin = parsed.IsName ? null : parsed.Value,
                Name = parsed.IsName ? parsed.Value : null
            }, parsed.Region);
            return identity == null ? id : AudiobookId.ForAsin(parsed.Region, identity.Asin);
        }

        public static IEnumerable<string> SearchNames(AuthorMetadata metadata, string region)
        {
            var parsed = AudiobookId.Parse(metadata.ForeignAuthorId);
            var identity = Find(new AudnexusContributorResource
            {
                Asin = parsed.IsName ? null : parsed.Value,
                Name = metadata.Name
            }, region);
            return AudiobookTextNormalizer.Names((identity?.Names() ?? Enumerable.Empty<string>())
                .Concat(new[] { metadata.Name }).Concat(metadata.Aliases));
        }

        public static void PreserveNames(AuthorMetadata target, AuthorMetadata previous)
        {
            // An identical provider ID is evidence that these observed spellings
            // belong together. Names alone never establish this relationship.
            if (previous == null || target.ForeignAuthorId != previous.ForeignAuthorId)
            {
                return;
            }

            target.Aliases = AudiobookTextNormalizer.Names(target.Aliases.Concat(previous.Aliases).Append(previous.Name))
                .Where(name => !string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private IEnumerable<string> Names() => new[] { Name }.Concat(Aliases);
    }
}
