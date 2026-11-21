using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Languages
{
    public static class RecordingLanguageEvidence
    {
        public const string LanguagesKey = "recordingLanguages";
        public const string EditionLanguageKey = "selectedEditionLanguageId";

        private static readonly HashSet<int> Supported = Language.All.Where(language => language.Id > 0)
            .Select(language => language.Id).ToHashSet();

        public static bool IsKnown(int id) => Supported.Contains(id);

        public static List<Language> Parse(params string[] values)
        {
            return values.Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Select(Resolve)
                .Distinct()
                .ToList();
        }

        public static List<Language> ForRelease(RemoteBook remoteBook)
        {
            if (remoteBook.RecordedLanguages != null)
            {
                return remoteBook.RecordedLanguages;
            }

            var release = remoteBook.Release;
            var audio = FromAudioStreams(release?.MediaInfo);
            return audio.Count > 0 ? audio : release?.Languages?.Select(language => Normalize(language?.Id ?? 0)).Distinct().ToList() ?? new List<Language>();
        }

        public static List<Language> ForFile(MediaInfoModel mediaInfo, string taggedLanguage, Edition edition)
        {
            var audio = FromAudioStreams(mediaInfo);
            if (audio.Count > 0)
            {
                return audio;
            }

            var tagged = Parse(taggedLanguage);
            return tagged.Count > 0 ? tagged : Parse(edition?.Language);
        }

        public static int SelectedEditionLanguage(IEnumerable<Book> books)
        {
            var languages = books.Select(book => book.AnyEditionOk
                ? 0
                : EditionLanguage(book.Editions?.Value?.Where(edition => edition.Monitored).Take(2).ToList())).Distinct().ToList();
            return languages.Count == 1 ? languages[0] : 0;
        }

        public static int EditionLanguage(Edition edition)
        {
            var languages = Parse(edition?.Language);
            return languages.Count == 1 ? languages[0].Id : 0;
        }

        public static List<Language> ParseStored(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<Language>();
            }

            return value.Split(',').Select(token => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? Normalize(id)
                : Language.Unknown).Distinct().ToList();
        }

        public static string FormatStored(IEnumerable<Language> languages)
        {
            return string.Join(',', languages.Select(language => language.Id.ToString(CultureInfo.InvariantCulture)));
        }

        public static void WriteHistory(IDictionary<string, string> data, IEnumerable<Language> languages, int selectedEditionLanguageId)
        {
            data[LanguagesKey] = FormatStored(languages);
            data[EditionLanguageKey] = selectedEditionLanguageId.ToString(CultureInfo.InvariantCulture);
        }

        public static string ReadHistory(IDictionary<string, string> data, string key)
        {
            var matches = data?.Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            return matches?.Count == 1 ? matches[0].Value : null;
        }

        public static int ReadEditionLanguage(IDictionary<string, string> data)
        {
            return int.TryParse(ReadHistory(data, EditionLanguageKey), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && IsKnown(id) ? id : 0;
        }

        private static int EditionLanguage(List<Edition> editions) => editions?.Count == 1 ? EditionLanguage(editions[0]) : 0;

        private static Language Normalize(int id) => IsKnown(id) ? Language.FindById(id) : Language.Unknown;

        private static Language Resolve(string value)
        {
            return Language.All.FirstOrDefault(language => language.Id > 0 && language.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) ??
                IsoLanguages.FindByName(value)?.Language ??
                IsoLanguages.Find(value.Replace('_', '-'))?.Language ??
                IsoLanguages.Find(value.CanonicalizeLanguage() ?? string.Empty)?.Language ??
                Language.Unknown;
        }

        private static List<Language> FromAudioStreams(MediaInfoModel mediaInfo)
        {
            if (mediaInfo?.AudioStreams?.Count > 0)
            {
                var streams = mediaInfo.AudioStreams
                    .Select(stream => Parse(stream?.Language).DefaultIfEmpty(Language.Unknown))
                    .SelectMany(languages => languages)
                    .Distinct()
                    .ToList();

                return streams.Any(language => language.Id > 0) ? streams : Parse(mediaInfo.Language);
            }

            return Parse(mediaInfo?.Language);
        }
    }
}
