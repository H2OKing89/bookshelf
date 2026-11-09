using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audnexus;

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

        public static void PreferDisplayName(
            AuthorMetadata target,
            AuthorMetadata previous = null,
            AudiobookAuthorIdentity identity = null,
            IEnumerable<AudnexusBookResource> recordings = null)
        {
            var parts = target?.ForeignAuthorId?.Split(':');
            if (parts?.Length != 3 || parts[0] != "audible" || !AudnexusProxy.Regions.Contains(parts[1]) || !AudiobookId.IsAsin(parts[2]))
            {
                return;
            }

            var samePrevious = previous?.ForeignAuthorId == target.ForeignAuthorId ? previous : null;
            var observed = (recordings ?? Enumerable.Empty<AudnexusBookResource>())
                .Where(recording => recording?.Region == parts[1] && AudiobookId.IsAsin(recording.Asin) &&
                    AudiobookTextNormalizer.Language(recording.Language) == "eng")
                .SelectMany(recording => recording.Authors ?? new List<AudnexusContributorResource>())
                .Select(credit => AudiobookContributors.Credit(credit, "Author"))
                .Where(credit => credit?.Role == "Author" && credit.Asin == parts[2])
                .Select(credit => credit.Name).ToList();
            var aliases = AudiobookTextNormalizer.Names((target.Aliases ?? new List<string>())
                .Concat(samePrevious?.Aliases ?? new List<string>()).Concat(observed)
                .Append(samePrevious?.Name).Append(target.Name));
            string preferred = null;
            if (identity?.Region == parts[1] && identity.Asin == parts[2] && !string.IsNullOrWhiteSpace(identity.Name))
            {
                preferred = identity.Name;
            }
            else if (!IsLatinName(target.Name))
            {
                // Only an exact author identity connects these spellings. An English
                // recording supplies a name; this never transliterates native text.
                if (IsLatinName(samePrevious?.Name))
                {
                    preferred = samePrevious.Name;
                }
                else
                {
                    var names = AudiobookTextNormalizer.Names(observed.Where(IsLatinName));
                    if (names.Count == 0)
                    {
                        names = AudiobookTextNormalizer.Names(aliases.Where(IsLatinName));
                    }

                    // Conflicting observed Latin spellings need an explicit choice.
                    if (names.Count == 1)
                    {
                        preferred = names[0];
                    }
                }
            }

            if (preferred != null)
            {
                target.Name = preferred;
                target.SortName = preferred;
                target.NameLastFirst = preferred;
                target.SortNameLastFirst = preferred;
            }

            target.Aliases = aliases.Where(name => !string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public static bool IsLatinName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var hasLetter = false;
            foreach (var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
            {
                if (!Rune.IsLetter(rune))
                {
                    continue;
                }

                hasLetter = true;
                var code = rune.Value;
                if (!((code >= 0x0041 && code <= 0x007a) || (code >= 0x00c0 && code <= 0x024f) ||
                    (code >= 0x1e00 && code <= 0x1eff) || (code >= 0x2c60 && code <= 0x2c7f) ||
                    (code >= 0xa720 && code <= 0xa7ff) || (code >= 0xab30 && code <= 0xab6f) ||
                    (code >= 0x10780 && code <= 0x107bf) || (code >= 0x1df00 && code <= 0x1dfff)))
                {
                    return false;
                }
            }

            return hasLetter;
        }
    }
}
