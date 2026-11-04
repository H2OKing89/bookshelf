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
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonaMouseTests
{
    [TestFixture]
    public class MyAnonaMouseOfflineFixture : CoreTest<MyAnonaMouse>
    {
        private readonly List<JObject> _requests = new List<JObject>();

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
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
                    var items = new JArray(Enumerable.Range(offset + 1, count).Select(Torrent));
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
            _requests.Select(request => request["tor"].Value<int>("startNumber")).Should().Equal(0, 100);
            _requests[0]["tor"]["main_cat"].Values<int>().Should().Equal(13);
            _requests[0].Value<int>("perpage").Should().Be(100);
            _requests[0]["tor"]["srchIn"].Values<string>().Should().Contain(new[] { "title", "author", "series", "filenames" });
            _requests[0]["tor"].Value<string>("text").Should().Be("Example Author Example Series");
            _requests[0]["dlLink"].Should().BeNull();
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
