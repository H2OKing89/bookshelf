using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookAuthorIdentityFixture : CoreTest<AudiobookInfoProxy>
    {
        private const string CanonicalId = "audible:us:B0DSLB5CG5";
        private JObject _fixture;

        [SetUp]
        public void Setup()
        {
            _fixture = JObject.Parse(ReadAllText("Files/MetadataSource/Audiobook/KumaKumaKumaBear.json"));
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudnexusRegion).Returns("us");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsUrl).Returns("https://agent.example");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsTokenFile).Returns("/token");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.ReadAllText("/token")).Returns("fixture-token");
            Mocker.GetMock<IHttpClient>().Setup(c => c.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var name = body["query"].Value<string>("author");
                    var catalog = _fixture["catalogs"].Single(c => c.Value<string>("name") == name);
                    return new HttpResponse(request, new HttpHeader(), catalog["response"].ToString());
                });
            Mocker.SetConstant<IAudibleCatalogProxy>(Mocker.Resolve<AudibleCatalogProxy>());
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor("B0DSLB5CG5", "us"))
                .Returns(new AudnexusAuthorResource { Asin = "B0DSLB5CG5", Region = "us", Name = "Kumanano" });
        }

        [TestCase(CanonicalId)]
        [TestCase("audible:us:B074QPMWRL")]
        [TestCase("audible:us:name:Kumanano")]
        [TestCase("audible:us:name:%E3%81%8F%E3%81%BE%E3%81%AA%E3%81%AE")]
        public void should_discover_all_kuma_recordings_from_either_spelling_or_profile(string id)
        {
            var author = Subject.GetAuthorInfo(id);

            author.ForeignAuthorId.Should().Be(CanonicalId);
            author.Name.Should().Be("Kumanano");
            author.Metadata.Value.Aliases.Should().Contain("くまなの");
            author.Books.Value.Should().HaveCount(14);
            author.Books.Value.Select(b => b.ForeignBookId).Should().OnlyHaveUniqueItems();
            author.Books.Value.Should().OnlyContain(b => b.AuthorMetadata.Value.ForeignAuthorId == CanonicalId);
            author.Books.Value.SelectMany(b => b.Editions.Value).Should().OnlyContain(e => e.Language == "eng");
            author.Series.Value.Should().ContainSingle(s => s.LinkItems.Value.Count == 14);
            author.Books.Value.SelectMany(b => b.SeriesLinks.Value).Select(l => l.Position)
                .Should().Contain("11.5");
            var expectedTitles = _fixture["catalogs"].SelectMany(c => c["response"]["body"]["products"])
                .Select(p => p.Value<string>("title"));
            author.Books.Value.Select(b => b.Title).Should().BeEquivalentTo(expectedTitles);
        }

        [TestCase("B0DSLB5CG5", "Kumanano")]
        [TestCase("B074QPMWRL", "くまなの")]
        [TestCase(null, "Kumanano")]
        [TestCase(null, "くまなの")]
        public void should_use_the_same_identity_for_search_and_individual_refresh(string asin, string name)
        {
            var recording = Recording(asin, name);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.SearchBooks(It.IsAny<string>(), "us"))
                .Returns(new List<AudnexusBookResource> { recording });
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetBook(recording.Asin, "us")).Returns(recording);

            Subject.SearchForNewAuthor(name).Should().ContainSingle(a => a.ForeignAuthorId == CanonicalId);
            var result = Subject.GetBookInfo("audible:us:" + recording.Asin);
            result.Item1.Should().Be(CanonicalId);
            result.Item2.AuthorMetadata.Value.ForeignAuthorId.Should().Be(CanonicalId);
            result.Item3.Should().ContainSingle(a => a.ForeignAuthorId == CanonicalId);
        }

        [Test]
        public void should_not_merge_an_unrecognized_profile_with_the_same_name()
        {
            var author = AudiobookMetadataMapper.Author(new AudnexusContributorResource { Asin = "B000000001", Name = "Kumanano" }, "us");
            author.ForeignAuthorId.Should().Be("audible:us:B000000001");
            author.Aliases.Should().BeEmpty();
        }

        [Test]
        public void should_not_apply_a_us_identity_mapping_in_another_marketplace()
        {
            var author = AudiobookMetadataMapper.Author(new AudnexusContributorResource { Asin = "B074QPMWRL", Name = "くまなの" }, "jp");
            author.ForeignAuthorId.Should().Be("audible:jp:B074QPMWRL");
            author.Name.Should().Be("くまなの");
        }

        [Test]
        public void should_fail_the_refresh_if_one_alias_catalog_fails()
        {
            Mocker.GetMock<IHttpClient>().Setup(c => c.Post(It.Is<HttpRequest>(r =>
                Encoding.UTF8.GetString(r.ContentData).Contains("くまなの"))))
                .Returns((HttpRequest r) => new HttpResponse(r, new HttpHeader(), "{\"status\":503,\"body\":{}}"));
            Assert.Throws<BookInfoException>(() => Subject.GetAuthorInfo(CanonicalId));
        }

        private static AudnexusBookResource Recording(string authorAsin, string name) => new AudnexusBookResource
        {
            Asin = "B0F7SC7WVW", Region = "us", Title = "Kuma Kuma Kuma Bear (Light Novel) Vol. 1",
            Authors = new List<AudnexusContributorResource> { new AudnexusContributorResource { Asin = authorAsin, Name = name } }
        };
    }
}
