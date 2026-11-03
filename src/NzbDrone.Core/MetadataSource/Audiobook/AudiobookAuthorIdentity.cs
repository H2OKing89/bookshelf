using System;
using System.Linq;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Audiobook
{
    // These are verified catalog identities, not transliteration rules. Never infer
    // that two authors are the same person from a similar name or a shared series.
    public class AudiobookAuthorIdentity
    {
        public string Region { get; }
        public string Asin { get; }
        public string Name { get; }
        public string[] AlternateAsins { get; }
        public string[] Aliases { get; }

        public AudiobookAuthorIdentity(string region, string asin, string name, string[] alternateAsins, string[] aliases)
        {
            Region = region;
            Asin = asin;
            Name = name;
            AlternateAsins = alternateAsins;
            Aliases = aliases;
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
    }
}
