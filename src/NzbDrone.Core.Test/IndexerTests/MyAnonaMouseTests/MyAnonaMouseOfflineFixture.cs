using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MyAnonaMouse;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonaMouseTests
{
    [TestFixture]
    public class MyAnonaMouseOfflineFixture : CoreTest<MyAnonaMouse>
    {
        private readonly List<JObject> _requests = new List<JObject>();
        private JToken _mediaInfo;
        private JToken _reportedFileCount;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _mediaInfo = null;
            _reportedFileCount = null;
            Subject.Definition = new IndexerDefinition
            {
                Name = "MAM fixture",
                Settings = new MyAnonaMouseSettings
                {
                    BaseUrl = "https://indexer.example/", MamId = "fixture-session", SearchLanguages = new[] { 1 },
                    SearchInSeries = true, SearchInFilenames = true
                }
            };
        }

        private static JObject Torrent(int id) => JObject.FromObject(new
        {
            id, title = "Example Series, Vol. 1 &amp; More", author_info = "{\"1\":\"Example &amp; Author\"}",
            narrator_info = "{\"2\":\"Reader One\"}", series_info = "{\"3\":[\"Example Series\",\"1\"]}",
            filetype = "m4b", lang_code = "ENG", category = 41, added = "2026-11-01 12:00:00",
            size = "100 MiB", seeders = "7", leechers = "2", times_completed = "9000",
            vip = "0", free = "1", personal_freeleech = "0", fl_vip = "2"
        });

        private void GivenPages(params int[] counts)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    request.Method.Should().Be(HttpMethod.Post);
                    request.Url.FullUri.Should().Be("https://indexer.example/tor/js/loadSearchJSONbasic.php");
                    request.Cookies["mam_id"].Should().Be("fixture-session");
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var offset = body["tor"].Value<int>("startNumber");
                    var count = counts[_requests.Count];
                    _requests.Add(body);
                    var items = new JArray(Enumerable.Range(offset + 1, count).Select(id =>
                    {
                        var item = Torrent(id);
                        if (_mediaInfo != null)
                        {
                            item["mediainfo"] = _mediaInfo.DeepClone();
                        }

                        if (_reportedFileCount != null)
                        {
                            item["numfiles"] = _reportedFileCount.DeepClone();
                        }

                        return item;
                    }));
                    var content = new JObject { ["data"] = items, ["found"] = counts.Sum(), ["total"] = count }.ToString();
                    return Task.FromResult(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, content));
                });
        }

        [Test]
        public async Task should_request_only_audiobooks_and_page_with_documented_parameters()
        {
            GivenPages(100, 1);

            var releases = await Subject.Fetch(new BookSearchCriteria { Author = new Author { Name = "Example Author" }, BookTitle = "Example Series" });

            releases.Should().HaveCount(101);
            releases.Should().OnlyContain(release => release.Languages.Count == 1 && release.Languages[0] == Language.English);
            _requests.Select(request => request["tor"].Value<int>("startNumber")).Should().Equal(0, 100);
            _requests[0]["tor"]["main_cat"].Values<int>().Should().Equal(13);
            _requests[0].Value<int>("perpage").Should().Be(100);
            _requests[0]["tor"]["srchIn"].Values<string>().Should().Contain(new[] { "title", "author", "series", "filenames" });
            _requests[0]["tor"].Value<string>("text").Should().Be("Example Author Example Series");
            _requests[0]["dlLink"].Should().BeNull();
            _requests.Should().OnlyContain(request => request.Value<string>("mediaInfo") == "1");
        }

        [Test]
        public async Task should_find_a_book_using_an_observed_author_spelling()
        {
            GivenPages(0, 1);
            var author = new Author { Name = "pirate aba" };
            author.Metadata.Value.Aliases.Add("pirateaba");

            var releases = await Subject.Fetch(new BookSearchCriteria { Author = author, BookTitle = "Memory's Flame" });

            releases.Should().ContainSingle();
            _requests.Select(request => request["tor"].Value<string>("text")).Should()
                .Equal("pirate aba Memory's Flame", "pirateaba Memory's Flame");
            _requests.Select(request => request["tor"].Value<int>("startNumber")).Should().Equal(0, 0);
        }

        [Test]
        public async Task should_search_all_author_spellings_and_deduplicate_overlapping_results()
        {
            GivenPages(1, 1);
            var author = new Author { Name = "Example Author" };
            author.Metadata.Value.Aliases.Add("Another Name");

            var releases = await Subject.Fetch(new AuthorSearchCriteria { Author = author });

            releases.Should().ContainSingle();
            _requests.Select(request => request["tor"].Value<string>("text")).Should().Equal("Example Author", "Another Name");
        }

        [Test]
        public async Task should_skip_blank_aliases_and_search_equivalent_queries_only_once()
        {
            GivenPages(1, 1);
            var author = new Author { Name = "pirate aba" };
            author.Metadata.Value.Aliases.AddRange(new[] { null, "", " ", "PIRATE ABA", "pirate-aba", "pirateaba", " Pirateaba " });

            await Subject.Fetch(new BookSearchCriteria { Author = author, BookTitle = "Memory's Flame" });

            _requests.Select(request => request["tor"].Value<string>("text")).Should()
                .Equal("pirate aba Memory's Flame", "pirateaba Memory's Flame");
        }

        [Test]
        public async Task should_search_authors_without_an_alias_list()
        {
            GivenPages(1);
            var author = new Author { Name = "Example Author" };
            author.Metadata.Value.Aliases = null;

            await Subject.Fetch(new AuthorSearchCriteria { Author = author });

            _requests.Select(request => request["tor"].Value<string>("text")).Should().Equal("Example Author");
        }

        [Test]
        public async Task should_keep_structured_metadata_and_use_seeders_instead_of_completions()
        {
            GivenPages(1);

            var release = (TorrentInfo)(await Subject.FetchRecent()).Single();

            release.Book.Should().Be("Example Series, Vol. 1 & More");
            release.BookAuthors.Should().Equal("Example & Author");
            release.Narrators.Should().Equal("Reader One");
            release.BookSeries.Should().ContainSingle(series => series.Name == "Example Series" && series.Position == "1");
            release.Seeders.Should().Be(7);
            release.Peers.Should().Be(9);
            release.IndexerFlags.Should().HaveFlag(IndexerFlags.Freeleech);
            _requests[0]["tor"].Value<string>("sortType").Should().Be("dateDesc");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task should_keep_indexer_reported_audio_details(bool directObject)
        {
            var mediaInfo = new JObject
            {
                ["Audio1"] = new JObject
                {
                    ["Format"] = "AAC",
                    ["BitRate"] = "126k",
                    ["Channels"] = 2,
                    ["BitRate_Mode"] = "VBR",
                    ["CodecID"] = "mp4a-40-2"
                },
                ["menu"] = new JObject { ["00:00:00.000"] = "Chapter One" }
            };
            _mediaInfo = directObject ? mediaInfo : new JValue(mediaInfo.ToString());
            GivenPages(1);

            var release = (await Subject.FetchRecent()).Single();

            release.MediaInfo.Should().NotBeNull();
            release.MediaInfo.AudioFormat.Should().Be("AAC");
            release.MediaInfo.AudioBitrate.Should().Be(126);
            release.MediaInfo.AudioChannels.Should().Be(2);
            release.MediaInfo.Source.Should().Be("MyAnonaMouse");
            release.Title.Should().Contain("[ENG / M4B]");
        }

        [TestCase("1", 100, true, true)]
        [TestCase("2", 100, true, false)]
        [TestCase(null, 100, true, true)]
        [TestCase(null, 20, true, false)]
        [TestCase("many", 100, true, false)]
        [TestCase("1", 100, false, false)]
        public async Task should_quarantine_partial_or_ambiguous_audio_duration(string reportedFileCount, int mediaFileMib, bool hasContainerDuration, bool proven)
        {
            _reportedFileCount = reportedFileCount == null ? null : new JValue(reportedFileCount);
            var general = new JObject { ["FileSize"] = mediaFileMib * 1024L * 1024 };
            if (hasContainerDuration)
            {
                general["Duration"] = 3600;
            }

            _mediaInfo = new JObject
            {
                ["General"] = general,
                ["Audio1"] = new JObject { ["Format"] = "AAC", ["Duration"] = 1200, ["Default"] = "Yes" },
                ["Audio2"] = new JObject { ["Format"] = "AAC", ["Duration"] = 3600 }
            };
            GivenPages(1);

            var release = (await Subject.FetchRecent()).Single();

            release.MediaInfo.DurationSeconds.Should().Be(1200, "the default stream duration stays available independently");
            release.AudioDurationSeconds.Should().Be(proven ? 3600d : (double?)null);
        }

        [TestCase(null)]
        [TestCase("null")]
        [TestCase("\"\"")]
        [TestCase("\"{}\"")]
        [TestCase("{}")]
        [TestCase("\"not json\"")]
        [TestCase("[]")]
        [TestCase("true")]
        public async Task should_keep_release_when_optional_media_info_is_unavailable(string json)
        {
            _mediaInfo = json == null ? null : JToken.Parse(json);
            GivenPages(1);

            var release = (TorrentInfo)(await Subject.FetchRecent()).Single();

            release.MediaInfo.Should().BeNull();
            release.Book.Should().Be("Example Series, Vol. 1 & More");
            release.Seeders.Should().Be(7);
        }

        [Test]
        public async Task should_stop_when_the_api_reports_the_end_of_results()
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Task.FromResult(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"error\":\"Nothing returned, out of 0\"}")));

            (await Subject.FetchRecent()).Should().BeEmpty();
        }

        [Test]
        public void should_authenticate_torrent_requests_only_to_the_configured_indexer()
        {
            Subject.GetDownloadRequest("https://indexer.example/tor/download.php?tid=1").Cookies["mam_id"].Should().Be("fixture-session");
            Subject.GetDownloadRequest("https://other.example/tor/download.php?tid=1").Cookies.Should().BeEmpty();
        }

        [TestCase("true", true)]
        [TestCase("1", true)]
        [TestCase("\"2\"", true)]
        [TestCase("\"0\"", false)]
        [TestCase("false", false)]
        public void should_accept_documented_numeric_and_boolean_flags(string value, bool expected)
        {
            var item = Newtonsoft.Json.JsonConvert.DeserializeObject<MyAnonaMouseTorrent>("{\"fl_vip\":" + value + "}");
            item.FreeVip.Should().Be(expected);
        }
    }
}
