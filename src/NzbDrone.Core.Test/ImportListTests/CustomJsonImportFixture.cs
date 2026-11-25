using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.CustomJson;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class CustomJsonImportFixture : CoreTest<CustomJsonImport>
    {
        private const string AuthorId = "audible:us:B000000001";
        private const string RecordingId = "audible:us:B000000002";
        private const string UkRecordingId = "audible:uk:B000000002";
        private const string FixtureToken = "synthetic-owned-token";
        private CustomJsonImportSettings _settings;
        private Mock<IHttpClient> _http;
        private List<HttpRequest> _requests;

        [SetUp]
        public void Setup()
        {
            _settings = new CustomJsonImportSettings { BaseUrl = "http://127.0.0.1:9000/owned-list", ApiKey = FixtureToken };
            _http = new Mock<IHttpClient>();
            _requests = new List<HttpRequest>();
            Mocker.SetConstant<ICustomJsonImportProxy>(new CustomJsonImportProxy(_http.Object));
            Subject.Definition = new ImportListDefinition { Id = 17, Name = "Owned custom list", Settings = _settings };
        }

        [Test]
        public void should_preserve_exact_regional_and_local_recordings_and_author_entries()
        {
            const string localAuthor = "local:author:00000000000000000000000000000001";
            const string localRecording = "local:recording:00000000000000000000000000000002";
            Responses(Page(1, 4, new object[]
            {
                Author(),
                Recording(RecordingId),
                Recording(UkRecordingId),
                Recording(localRecording, localAuthor)
            }));

            var result = Subject.FetchWithStatus();
            result.Complete.Should().BeTrue();
            result.Items.Should().HaveCount(4);
            result.Items.Select(item => item.BookGoodreadsId).Should().Equal(null, RecordingId, UkRecordingId, localRecording);
            result.Items.Select(item => item.EditionGoodreadsId).Should().Equal(null, RecordingId, UkRecordingId, localRecording);
            result.Items.Last().AuthorGoodreadsId.Should().Be(localAuthor);
            result.Items.Should().OnlyContain(item => item.ImportListId == 17 && item.ImportList == "Owned custom list");
            var request = _requests.Should().ContainSingle().Subject;
            request.Url.FullUri.Should().Be("http://127.0.0.1:9000/owned-list?page=1&pageSize=100");
            request.Headers["Authorization"].Should().Be("Bearer " + FixtureToken);
            request.AllowAutoRedirect.Should().BeFalse();
            request.UseCookies.Should().BeFalse();
            request.RedactSensitiveDetails.Should().BeTrue();
            request.LogResponseContent.Should().BeFalse();
            request.MaxBufferedResponseBytes.Should().Be(CustomJsonImportProxy.MaxPageBytes);
            Mocker.GetMock<IImportListStatusService>().Verify(status => status.RecordSuccess(17), Times.Once());
        }

        [Test]
        public void should_allow_public_endpoints_and_confirmed_empty_results()
        {
            _settings.ApiKey = null;
            Responses(Page(1, 0, Array.Empty<object>()));
            var result = Subject.FetchWithStatus();
            result.Complete.Should().BeTrue();
            result.Items.Should().BeEmpty();
            _requests.Single().Headers["Authorization"].Should().BeNull();
        }

        [Test]
        public void should_expand_only_explicit_work_recordings_without_creating_duplicate_membership()
        {
            object Work(string id, params object[] recordings) => new
            {
                kind = "work",
                authorId = AuthorId,
                author = "Owned author",
                workId = id,
                workTitle = "Owned work",
                recordings
            };
            object Entry(string id) => new { title = "Same title", recordingId = id, editionId = id };

            Responses(Page(1, 2, new[]
            {
                Work("owned-work-1", Entry(RecordingId), Entry(UkRecordingId)),
                Work("owned-work-2", Entry(RecordingId))
            }));

            var result = Subject.FetchWithStatus();
            result.Complete.Should().BeTrue();
            result.Items.Select(item => item.BookGoodreadsId).Should().Equal(RecordingId, UkRecordingId);
        }

        [Test]
        public void should_complete_a_consistent_multi_page_snapshot()
        {
            Responses(Page(1, 101, FirstPage()), Page(2, 101, new[] { Recording(UkRecordingId) }));
            var result = Subject.FetchWithStatus();
            result.Complete.Should().BeTrue();
            result.Items.Should().HaveCount(101);
            _requests.Select(request => request.Url.Query).Should().Equal("page=1&pageSize=100", "page=2&pageSize=100");
        }

        [TestCase("snapshot-changed")]
        [TestCase("total-changed")]
        [TestCase("page-repeated")]
        [TestCase("empty-page")]
        [TestCase("incomplete-page")]
        [TestCase("malformed-page")]
        [TestCase("network-failure")]
        [TestCase("http-failure")]
        public void should_discard_every_item_if_a_later_page_fails(string reason)
        {
            var first = Page(1, 101, FirstPage());
            object[] last = { Recording(UkRecordingId) };
            switch (reason)
            {
                case "snapshot-changed":
                    Responses(first, Page(2, 101, last, snapshot: "changed"));
                    break;
                case "total-changed":
                    Responses(first, Page(2, 102, last));
                    break;
                case "page-repeated":
                    Responses(first, first);
                    break;
                case "empty-page":
                    Responses(first, Page(2, 101, Array.Empty<object>()));
                    break;
                case "incomplete-page":
                    Responses(first, Page(2, 101, last, complete: false));
                    break;
                case "malformed-page":
                    Responses(first, "not JSON");
                    break;
                case "network-failure":
                    _http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
                    {
                        _requests.Add(request);
                        return _requests.Count == 1 ? Response(request, first) : throw new WebException("private provider detail " + FixtureToken);
                    });
                    break;
                case "http-failure":
                    _http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
                    {
                        _requests.Add(request);
                        return _requests.Count == 1 ? Response(request, first) : Response(request, FixtureToken, HttpStatusCode.ServiceUnavailable);
                    });
                    break;
            }

            AssertFailed();
            _requests.Should().HaveCount(2);
        }

        [TestCase("unsupported-version")]
        [TestCase("bare-asin")]
        [TestCase("unknown-marketplace")]
        [TestCase("recording-name-id")]
        [TestCase("missing-edition")]
        [TestCase("contradictory-edition")]
        [TestCase("invalid-local-id")]
        [TestCase("conflicting-author")]
        [TestCase("unknown-kind")]
        [TestCase("work-without-recordings")]
        [TestCase("null-items")]
        [TestCase("duplicate-member")]
        [TestCase("unexpected-error-field")]
        [TestCase("too-many-items")]
        public void should_reject_unsupported_or_ambiguous_payloads(string reason)
        {
            var valid = Page(1, 1, new[] { Recording(RecordingId) });
            var content = reason switch
            {
                "unsupported-version" => Page(1, 0, Array.Empty<object>(), version: 2),
                "bare-asin" => Page(1, 1, new[] { Recording("B000000002") }),
                "unknown-marketplace" => Page(1, 1, new[] { Recording("audible:invalid:B000000002") }),
                "recording-name-id" => Page(1, 1, new[] { Recording("audible:us:name:Title") }),
                "missing-edition" => Page(1, 1, new[] { new { kind = "recording", author = "Owned author", authorId = AuthorId, title = "Owned", recordingId = RecordingId } }),
                "contradictory-edition" => Page(1, 1, new[] { Recording(RecordingId, editionId: UkRecordingId) }),
                "invalid-local-id" => Page(1, 1, new[] { Recording("local:recording:abc") }),
                "conflicting-author" => Page(1, 2, new[] { Recording(RecordingId), Recording(RecordingId, "audible:us:B000000003") }),
                "unknown-kind" => Page(1, 1, new[] { new { kind = "series", author = "Owned author", authorId = AuthorId } }),
                "work-without-recordings" => Page(1, 1, new[] { new { kind = "work", author = "Owned", authorId = AuthorId, workId = "owned", workTitle = "Owned work", recordings = Array.Empty<object>() } }),
                "null-items" => Page(1, 0, null),
                "duplicate-member" => valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal),
                "unexpected-error-field" => valid.Replace("\"complete\":true", "\"complete\":true,\"errors\":[\"incomplete\"]", StringComparison.Ordinal),
                "too-many-items" => Page(1, 1001, FirstPage()),
                _ => throw new ArgumentException(nameof(reason))
            };

            Responses(content);
            AssertFailed();
            _requests.Should().ContainSingle();
        }

        [TestCase(HttpStatusCode.Found)]
        [TestCase(HttpStatusCode.PartialContent)]
        [TestCase(HttpStatusCode.Unauthorized)]
        public void should_reject_non_200_responses_without_including_provider_details_in_validation(HttpStatusCode status)
        {
            _http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                var response = Response(request, FixtureToken, status);
                response.Headers.Set("Location", "https://example.invalid/" + FixtureToken);
                return response;
            });

            var test = Subject.Test();
            test.IsValid.Should().BeFalse();
            test.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain($"HTTP {(int)status}").And.NotContain(FixtureToken);
            _requests.Should().ContainSingle();
            Mocker.GetMock<IImportListStatusService>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_enforce_the_size_limit_when_the_http_client_does_not()
        {
            Responses(new string('x', CustomJsonImportProxy.MaxPageBytes + 1));
            var test = Subject.Test();
            test.IsValid.Should().BeFalse();
            test.Errors.Single().ErrorMessage.Should().Contain("1 MiB");
        }

        [Test]
        public void should_not_treat_an_html_login_as_a_successful_empty_result()
        {
            _http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
                new HttpResponse(request, new HttpHeader { ContentType = "text/html" }, Page(1, 0, Array.Empty<object>())));

            var test = Subject.Test();
            test.IsValid.Should().BeFalse();
            test.Errors.Single().ErrorMessage.Should().Contain("JSON content type");
        }

        [Test]
        public void should_accept_a_canonical_native_name_author_id()
        {
            Responses(Page(1, 1, new[] { new { kind = "author", authorId = "audible:uk:name:Owned%20Author", author = "Owned Author" } }));
            var result = Subject.FetchWithStatus();
            result.Complete.Should().BeTrue();
            result.Items.Should().ContainSingle().Which.AuthorGoodreadsId.Should().Be("audible:uk:name:Owned%20Author");
        }

        [Test]
        public void should_reject_expansion_past_membership_limit()
        {
            var recordings = Enumerable.Range(1, 501).Select(index => new { title = "Owned", recordingId = $"audible:us:B{index:D9}", editionId = $"audible:us:B{index:D9}" }).ToArray();
            object Work(string id) => new { kind = "work", author = "Owned author", authorId = AuthorId, workId = id, workTitle = "Owned work", recordings };
            Responses(Page(1, 2, new[] { Work("one"), Work("two") }));
            AssertFailed();
        }

        [TestCase("file:///etc/hosts")]
        [TestCase("http://user:secret@127.0.0.1/list")]
        [TestCase("http://127.0.0.1/list?secret=fixture")]
        [TestCase("http://127.0.0.1/list#token")]
        public void should_reject_invalid_or_credential_bearing_urls_before_fetching(string endpoint)
        {
            _settings.BaseUrl = endpoint;
            var test = Subject.Test();
            test.IsValid.Should().BeFalse();
            _http.VerifyNoOtherCalls();
        }

        [TestCase("owned\r\nX-Fixture: extra")]
        [TestCase("owned key")]
        public void should_reject_malformed_bearer_tokens_before_fetching(string token)
        {
            _settings.ApiKey = token;
            Subject.Test().IsValid.Should().BeFalse();
            _http.VerifyNoOtherCalls();
        }

        private void AssertFailed()
        {
            var result = Subject.FetchWithStatus();
            result.Complete.Should().BeFalse();
            result.Items.Should().BeEmpty();
            Mocker.GetMock<IImportListStatusService>().Verify(status => status.RecordFailure(17, TimeSpan.Zero), Times.Once());
            Mocker.GetMock<IImportListStatusService>().Verify(status => status.RecordSuccess(17), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        private void Responses(params string[] responses)
        {
            _http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                return Response(request, responses[_requests.Count - 1]);
            });
        }

        private static HttpResponse Response(HttpRequest request, string content, HttpStatusCode status = HttpStatusCode.OK) =>
            new (request, new HttpHeader { ContentType = "application/json; charset=utf-8" }, Encoding.UTF8.GetBytes(content), status);

        private static object Author() => new { kind = "author", author = "Owned author", authorId = AuthorId };

        private static object Recording(string id, string authorId = AuthorId, string editionId = null) =>
            new { kind = "recording", author = "Owned author", authorId, title = "Same title", recordingId = id, editionId = editionId ?? id };

        private static object[] FirstPage() => Enumerable.Range(100, 100).Select(index => Recording($"audible:us:B{index:D9}")).ToArray();

        private static string Page(int page, int total, object[] items, string snapshot = "owned-snapshot", bool complete = true, int version = 1) =>
            JsonSerializer.Serialize(new { schemaVersion = version, snapshotId = snapshot, page, pageSize = 100, totalItems = total, complete, items });
    }
}
