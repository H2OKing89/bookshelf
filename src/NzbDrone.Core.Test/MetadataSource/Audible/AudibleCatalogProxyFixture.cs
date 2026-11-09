using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audible
{
    [TestFixture]
    public class AudibleCatalogProxyFixture : CoreTest<AudibleCatalogProxy>
    {
        private readonly List<JObject> _requests = new List<JObject>();

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsUrl).Returns("https://agent.example");
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsTokenFile).Returns("/tokens/audible");
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsAccount).Returns("My account");
            Mocker.GetMock<IDiskProvider>().Setup(x => x.ReadAllText("/tokens/audible")).Returns("test-secret\n");
        }

        private void GivenPages(params string[] envelopes)
        {
            Mocker.GetMock<IHttpClient>().Setup(x => x.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var index = _requests.Count;
                    _requests.Add(body);
                    request.Url.FullUri.Should().Be("https://agent.example/v1/api/request");
                    request.Headers.GetSingleValue("X-Audible-Marketplace").Should().Be("us");
                    request.Headers.GetSingleValue("X-Audible-Account").Should().Be("My account");
                    request.Headers.GetSingleValue("Authorization").Should().Be("Bearer test-secret");
                    request.AllowAutoRedirect.Should().BeFalse();
                    body.Value<string>("method").Should().Be("GET");
                    return new HttpResponse(request, new HttpHeader(), envelopes[Math.Min(index, envelopes.Length - 1)]);
                });
        }

        [Test]
        public void should_start_at_page_zero_and_continue_until_total_results()
        {
            GivenPages(
                "{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\"}]}}",
                "{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0HMVRXW3C\",\"title\":\"Volume 2\"}]}}");
            var books = Subject.GetAuthorBooks("Yu Omiya", "us");
            books.Should().HaveCount(2);
            _requests[0]["query"].Value<int>("page").Should().Be(0);
            _requests[1]["query"].Value<int>("page").Should().Be(1);
            _requests[0]["query"].Value<string>("author").Should().Be("Yu Omiya");
            _requests[0]["query"].Value<string>("products_sort_by").Should().Be("ReleaseDate");
            _requests[0]["query"]["sort_by"].Should().BeNull();
            _requests[0]["query"].Value<string>("response_groups").Should().Contain("product_desc");
            _requests[0]["query"].Value<string>("response_groups").Should().Contain("product_extended_attrs");
            _requests[0]["query"].Value<string>("image_sizes").Should().Be("500,1024,2400");
        }

        [Test]
        public void should_reject_agent_http_success_with_upstream_failure()
        {
            GivenPages("{\"status\":429,\"body\":{\"message\":\"Throttled\"}}");
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            _requests.Should().ContainSingle();
        }

        [TestCase("{\"status\":200,\"body\":{\"total_results\":1,\"products\":[]}}")]
        [TestCase("{\"status\":200,\"body\":{\"products\":[]}}")]
        [TestCase("{\"status\":200,\"body\":{\"total_results\":1001,\"products\":[]}}")]
        [TestCase("not json")]
        public void should_reject_incomplete_catalogs(string envelope)
        {
            GivenPages(envelope);
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
        }

        [Test]
        public void should_detect_repeated_pages()
        {
            GivenPages("{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\"}]}}");
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_map_product_details_for_known_asins()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\",\"runtime_length_min\":360,\"release_date\":\"2026-07-10\",\"authors\":[{\"asin\":\"B0GVW7FF1Q\",\"name\":\"Yu Omiya\"}],\"narrators\":[{\"name\":\"David Lee Huynh\"}],\"product_images\":{\"500\":\"https://example/500.jpg\",\"1024\":\"https://example/1024.jpg\"}}}}");
            var book = Subject.GetBook("B0H8F9D4GD", "us");
            book.Region.Should().Be("us");
            book.RuntimeLengthMin.Should().Be(360);
            book.Narrators.Should().ContainSingle(n => n.Name == "David Lee Huynh");
            book.Image.Should().Be("https://example/1024.jpg");
            _requests[0].Value<string>("path").Should().Be("/1.0/catalog/products/B0H8F9D4GD");
            _requests[0]["query"].Value<string>("image_sizes").Should().Be("500,1024,2400");
        }

        [Test]
        public void should_keep_both_series_and_use_the_published_primary_order()
        {
            var product = new JObject
            {
                ["asin"] = "B0H8F9D4GD",
                ["publication_name"] = "The Chronicles of Narnia (Author's Preferred Order)",
                ["series"] = new JArray
                {
                    new JObject { ["asin"] = "B000000001", ["title"] = "The Chronicles of Narnia (Publication Order)", ["sequence"] = "1" },
                    new JObject { ["asin"] = "B000000002", ["title"] = "The Chronicles of Narnia (Author's Preferred Order)", ["sequence"] = "2" },
                    new JObject { ["asin"] = "B000000002", ["title"] = "The Chronicles of Narnia (Author's Preferred Order)", ["sequence"] = "2" }
                }
            };
            GivenPages(new JObject { ["status"] = 200, ["body"] = new JObject { ["product"] = product } }.ToString());

            var recording = Subject.GetBook("B0H8F9D4GD", "us");

            recording.SeriesPrimary.Asin.Should().Be("B000000002");
            recording.SeriesPrimary.Name.Should().Be("The Chronicles of Narnia (Author's Preferred Order)");
            recording.SeriesPrimary.Position.Should().Be("2");
            recording.SeriesSecondary.Asin.Should().Be("B000000001");
            recording.SeriesSecondary.Position.Should().Be("1");
        }

        [Test]
        public void should_keep_two_distinct_series_without_a_publication_name()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"series\":[null,{\"title\":\" \"},{\"asin\":\"B000000001\",\"title\":\"Series A\",\"sequence\":\"0.5\"},{\"asin\":\"B000000002\",\"title\":\"Series B\",\"sequence\":\"Collection\"}]}}}");

            var recording = Subject.GetBook("B0H8F9D4GD", "us");

            recording.SeriesPrimary.Name.Should().Be("Series A");
            recording.SeriesPrimary.Position.Should().Be("0.5");
            recording.SeriesSecondary.Name.Should().Be("Series B");
            recording.SeriesSecondary.Position.Should().Be("Collection");
        }

        [Test]
        public void should_preserve_category_identifiers_and_distinguish_parent_genres_from_tags()
        {
            var genreLadder = new JObject
            {
                ["root"] = "Genres",
                ["ladder"] = new JArray
                {
                    new JObject { ["id"] = "18580606011", ["name"] = "Science Fiction & Fantasy" },
                    new JObject { ["id"] = "18580628011", ["name"] = "Science Fiction" },
                    new JObject { ["id"] = "18580645011", ["name"] = "Space Opera" }
                }
            };
            var product = new JObject
            {
                ["asin"] = "B0H8F9D4GD",
                ["category_ladders"] = new JArray
                {
                    genreLadder,
                    genreLadder.DeepClone(),
                    new JObject { ["root"] = "Marketing", ["ladder"] = new JArray { new JObject { ["name"] = "Best Sellers" } } }
                }
            };
            GivenPages(new JObject { ["status"] = 200, ["body"] = new JObject { ["product"] = product } }.ToString());

            var genres = Subject.GetBook("B0H8F9D4GD", "us").Genres;

            genres.Should().HaveCount(3);
            genres[0].Asin.Should().Be("18580606011");
            genres[0].Type.Should().Be("genre");
            genres[1].Asin.Should().Be("18580628011");
            genres[1].Type.Should().Be("tag");
            genres[2].Name.Should().Be("Space Opera");
            genres[2].Type.Should().Be("tag");
        }

        [TestCase("© 1943 The Estate; (P) 2025 Publisher", 1943)]
        [TestCase("Copyright 2021 Publisher", 2021)]
        [TestCase("(c) 2020 Publisher", 2020)]
        [TestCase("2022", 2022)]
        [TestCase("(P) 2025 Publisher", null)]
        [TestCase("Publisher 2025", null)]
        public void should_only_extract_an_explicit_copyright_year(string copyright, int? expected)
        {
            var product = new JObject { ["asin"] = "B0H8F9D4GD", ["copyright"] = copyright };
            GivenPages(new JObject { ["status"] = 200, ["body"] = new JObject { ["product"] = product } }.ToString());

            Subject.GetBook("B0H8F9D4GD", "us").Copyright.Should().Be(expected);
        }

        [Test]
        public void should_keep_explicit_content_fields_without_guessing_from_the_release_date()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"is_adult_product\":false,\"literature_type\":\"nonfiction\",\"copyright\":1943,\"release_date\":\"2025-01-01\"}}}");

            var recording = Subject.GetBook("B0H8F9D4GD", "us");

            recording.IsAdult.Should().BeFalse();
            recording.LiteratureType.Should().Be("nonfiction");
            recording.Copyright.Should().Be(1943);
        }

        [Test]
        public void should_leave_missing_content_fields_unknown()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"release_date\":\"2025-01-01\"}}}");

            var recording = Subject.GetBook("B0H8F9D4GD", "us");

            recording.IsAdult.Should().BeNull();
            recording.Copyright.Should().BeNull();
            recording.LiteratureType.Should().BeNull();
        }

        [TestCase("extended_product_description")]
        [TestCase("publisher_summary")]
        [TestCase("merchandising_description")]
        [TestCase("merchandising_summary")]
        public void should_read_available_catalog_description_fields(string field)
        {
            var product = new JObject { ["asin"] = "B0H8F9D4GD", [field] = "<p>Recording synopsis</p>" };
            GivenPages(new JObject { ["status"] = 200, ["body"] = new JObject { ["product"] = product } }.ToString());

            var recording = Subject.GetBook("B0H8F9D4GD", "us");
            recording.Description.Should().Be("<p>Recording synopsis</p>");
            recording.DescriptionIsSummary.Should().Be(field == "merchandising_summary");
        }

        [Test]
        public void should_prefer_the_full_description_to_the_merchandising_summary()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"extended_product_description\":\"Full synopsis\",\"publisher_summary\":\" \",\"merchandising_summary\":\"Truncated...\"}}}");

            Subject.GetBook("B0H8F9D4GD", "us").Description.Should().Be("Full synopsis");
        }

        [Test]
        public void should_count_rejected_podcasts_when_finishing_the_catalog()
        {
            GivenPages(
                "{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B08K567RML\",\"title\":\"HIP HOP RNB on FOAM RADIO\",\"content_type\":\"Podcast\",\"content_delivery_type\":\"PodcastParent\"}]}}",
                "{\"status\":200,\"body\":{\"total_results\":2,\"products\":[{\"asin\":\"B0H8F9D4GD\",\"title\":\"Volume 1\",\"content_type\":\"Product\",\"content_delivery_type\":\"SinglePartBook\"}]}}");

            Subject.GetAuthorBooks("Shirley Jackson", "us").Should().ContainSingle(b => b.Asin == "B0H8F9D4GD");
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_finish_an_entirely_non_book_catalog_without_requesting_another_page()
        {
            GivenPages("{\"status\":200,\"body\":{\"total_results\":1,\"products\":[{\"asin\":\"B08K567RML\",\"content_delivery_type\":\"PodcastEpisode\"}]}}");

            Subject.GetAuthorBooks("Shirley Jackson", "us").Should().BeEmpty();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_not_return_a_podcast_as_a_recording_lookup()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B08K567RML\",\"content_type\":\"Podcast\"}}}");

            Subject.GetBook("B08K567RML", "us").Should().BeNull();
        }

        [Test]
        public void should_keep_original_recordings_and_preorders_without_runtime()
        {
            GivenPages("{\"status\":200,\"body\":{\"total_results\":1,\"products\":[{\"asin\":\"B0H8F9D4GD\",\"title\":\"A History of Podcasts\",\"format_type\":\"original_recording\",\"content_type\":\"Product\",\"content_delivery_type\":\"MultiPartBook\"}]}}");

            var recording = Subject.GetAuthorBooks("Yu Omiya", "us").Should().ContainSingle().Subject;
            recording.RuntimeLengthMin.Should().BeNull();
            recording.ContentType.Should().Be("Product");
            recording.ContentDeliveryType.Should().Be("MultiPartBook");
        }

        [Test]
        public void should_preserve_the_recording_isbn_from_catalog_details()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0H8F9D4GD\",\"isbn\":\"9781234567890\"}}}");

            Subject.GetBook("B0H8F9D4GD", "us").Isbn.Should().Be("9781234567890");
        }

        [Test]
        public void should_reject_wrong_product_asin()
        {
            GivenPages("{\"status\":200,\"body\":{\"product\":{\"asin\":\"B0HMVRXW3C\"}}}");
            Assert.Throws<BookInfoException>(() => Subject.GetBook("B0H8F9D4GD", "us"));
        }

        [Test]
        public void should_fail_closed_on_http_error()
        {
            Mocker.GetMock<IHttpClient>().Setup(x => x.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), "Denied", HttpStatusCode.Unauthorized));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
        }

        [Test]
        public void should_refuse_credentials_over_remote_plain_http()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.AudibleRsUrl).Returns("http://agent.example");
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            Mocker.GetMock<IHttpClient>().Verify(x => x.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_not_include_token_in_errors()
        {
            Mocker.GetMock<IHttpClient>().Setup(x => x.Post(It.IsAny<HttpRequest>())).Throws(new Exception("test-secret"));
            var exception = Assert.Throws<BookInfoException>(() => Subject.GetAuthorBooks("Yu Omiya", "us"));
            exception.ToString().Should().NotContain("test-secret");
        }
    }
}
