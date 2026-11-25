using System.Collections.Generic;
using System.Net;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.Hardcover;
using NzbDrone.Core.ImportLists.Readarr;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListMembershipProviderFixture : CoreTest<ReadarrImport>
    {
        private ReadarrSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new ReadarrSettings { BaseUrl = "http://127.0.0.1:9000", ApiKey = "owned-fixture-key" };
            Subject.Definition = new ImportListDefinition { Id = 1, Name = "Owned list", Settings = _settings };
            Mocker.GetMock<IReadarrV1Proxy>().Setup(proxy => proxy.GetAuthors(_settings)).Returns(new List<ReadarrAuthor>
            {
                new ReadarrAuthor { Id = 1, AuthorName = "Owned author", ForeignAuthorId = "audible:us:B000000001", Monitored = true }
            });
        }

        [Test]
        public void should_distinguish_a_complete_empty_remote_list_from_partial_results()
        {
            Mocker.GetMock<IReadarrV1Proxy>().Setup(proxy => proxy.GetBooks(_settings)).Returns(new List<ReadarrBook>());
            var empty = Subject.FetchWithStatus();
            empty.Complete.Should().BeTrue();
            empty.Items.Should().BeEmpty();

            Mocker.GetMock<IReadarrV1Proxy>().Setup(proxy => proxy.GetBooks(_settings)).Returns(new List<ReadarrBook>
            {
                new ReadarrBook { AuthorId = 1, Title = "Owned title", ForeignBookId = "audible:us:B000000002", Monitored = true },
                new ReadarrBook { AuthorId = 999, Title = "Missing author", ForeignBookId = "audible:us:B000000003", Monitored = true }
            });
            Subject.FetchWithStatus().Complete.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("{\"data\":{\"users\":[{\"lists\":[{\"slug\":\"owned\",\"list_books\":[]}]}]}}", true)]
        [TestCase("{\"data\":{\"users\":[{\"lists\":[{\"slug\":\"owned\",\"list_books\":[]}]}]},\"errors\":[{\"message\":\"Partial result\"}]}", false)]
        [TestCase("{\"data\":{\"users\":[{\"lists\":[]} ]}}", false)]
        [TestCase("{\"data\":{\"users\":[]}}", false)]
        [TestCase("{\"data\":{\"users\":[{\"lists\":[{\"slug\":\"owned\",\"list_books\":null}]}]}}", false)]
        public void should_require_a_complete_hardcover_response(string json, bool complete)
        {
            var httpRequest = new HttpRequest("http://127.0.0.1:9000");
            httpRequest.SetContent("{\"variables\":{\"slugs\":[\"owned\"]}}");
            var response = new ImportListResponse(new ImportListRequest(httpRequest), new HttpResponse(httpRequest, new HttpHeader(), Encoding.UTF8.GetBytes(json), HttpStatusCode.OK));
            var parser = new HardcoverImportParser();
            if (complete)
            {
                parser.ParseResponse(response).Should().BeEmpty();
            }
            else
            {
                Assert.Throws<ImportListException>(() => parser.ParseResponse(response));
            }
        }
    }
}
