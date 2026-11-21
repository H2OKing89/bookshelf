using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class RecordingLanguageFeedFixture
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_parse_repeated_and_comma_separated_languages(bool torrent, bool usePlainElements)
        {
            RssParser parser = torrent ? new TorznabRssParser() : new NewznabRssParser();
            var prefixNamespace = (torrent ? TorznabRssParser.ns : NewznabRssParser.ns)[1..^1];
            var mime = torrent ? "application/x-bittorrent" : "application/x-nzb";
            var languageFields = usePlainElements
                ? """<x:attr name="language" value=" " /><language> English, fReNCh </language><language>ENG, und</language>"""
                : """<x:attr name="language" value=" English, fReNCh " /><x:attr name="LANGUAGE" value=" ENG, und "/><language>German</language>""";
            var xml = $"""
                <rss xmlns:x="{prefixNamespace}"><channel><item>
                  <title>Synthetic recording</title>
                  <guid>https://indexer.example/book</guid>
                  <pubDate>Thu, 19 Nov 2026 12:00:00 GMT</pubDate>
                  <comments>https://indexer.example/book#comments</comments>
                  <enclosure url="https://indexer.example/download" type="{mime}" length="123456" />
                  <x:attr value="ignored missing name" />
                  <x:attr name="language" />
                  {languageFields}
                  <x:attr name="author" value="Synthetic author" />
                  <x:attr name="booktitle" value="Owned book" />
                </item></channel></rss>
                """;
            var request = new HttpRequest("https://indexer.example/api");
            var response = new IndexerResponse(new IndexerRequest(request), new HttpResponse(request, new HttpHeader(), xml));

            var release = parser.ParseResponse(response).Should().ContainSingle().Which;

            release.Languages.Should().Equal(Language.English, Language.French, Language.Unknown);
            release.Size.Should().Be(123456);
            if (!torrent)
            {
                release.Author.Should().Be("Synthetic author");
                release.Book.Should().Be("Owned book");
            }
        }
    }
}
