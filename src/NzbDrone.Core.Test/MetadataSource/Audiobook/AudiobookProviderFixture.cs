using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookProviderFixture : CoreTest<AudibleCatalogProxy>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsUrl).Returns("https://agent.example");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsTokenFile).Returns("/token");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.ReadAllText("/token")).Returns("fixture-token");
        }

        [TestCase("B0H8F9D4GD")]
        [TestCase("B0HMVRXW3C")]
        [TestCase("B09MWP4277")]
        [TestCase("B0BK9W82PQ")]
        [TestCase("B0CLNJQQD7")]
        [TestCase("B0D6H2N1YL")]
        public void should_preserve_subtitles_and_categories_from_both_providers(string asin)
        {
            var fixture = JObject.Parse(ReadAllText($"Files/MetadataSource/Audiobook/{asin}.json"));
            Mocker.GetMock<IHttpClient>().Setup(c => c.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), fixture["audible"].ToString()));
            var audnexus = Json.Deserialize<AudnexusBookResource>(fixture["audnexus"].ToString());
            var audible = Subject.GetBook(asin, "us");
            var fromAudnexus = AudiobookMetadataMapper.Book(audnexus, "us");
            var fromAudible = AudiobookMetadataMapper.Book(audible, "us");

            fromAudible.ForeignBookId.Should().Be(fromAudnexus.ForeignBookId);
            fromAudible.ReleaseDate.Should().Be(fromAudnexus.ReleaseDate);
            fromAudible.ReleaseDate.Value.Kind.Should().Be(System.DateTimeKind.Utc);
            fromAudible.Editions.Value.Single().Subtitle.Should().Be(audnexus.Subtitle);
            fromAudible.Genres.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
            fromAudnexus.Genres.Should().NotBeEmpty();
            fromAudible.SeriesLinks.Value.Single().Position.Should().Be(fromAudnexus.SeriesLinks.Value.Single().Position);
            Mocker.GetMock<IHttpClient>().Verify(
                c => c.Post(It.Is<HttpRequest>(r =>
                    System.Text.Encoding.UTF8.GetString(r.ContentData).Contains("category_ladders"))),
                Times.Once());
        }

        [Test]
        public void should_preserve_richer_details_when_catalog_fields_are_missing()
        {
            var fixture = JObject.Parse(ReadAllText("Files/MetadataSource/Audiobook/B0H8F9D4GD.json"));
            var resource = Json.Deserialize<AudnexusBookResource>(fixture["audnexus"].ToString());
            var stored = AudiobookMetadataMapper.Book(resource, "us");
            stored.Editions.Value.Single().Overview = "Stored description";
            resource.Subtitle = null;
            resource.PublisherName = null;
            resource.RuntimeLengthMin = null;
            resource.Narrators = null;
            resource.ReleaseDate = null;
            resource.Rating = null;
            resource.Genres = new List<AudnexusGenreResource> { new AudnexusGenreResource { Name = "New genre" } };
            var incoming = AudiobookMetadataMapper.Book(resource, "us");

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            var edition = incoming.Editions.Value.Single();
            var previous = stored.Editions.Value.Single();
            edition.Subtitle.Should().Be("Light Novel");
            edition.Publisher.Should().Be(previous.Publisher);
            edition.RuntimeLengthMinutes.Should().Be(previous.RuntimeLengthMinutes);
            edition.Narrators.Should().Equal(previous.Narrators);
            edition.Overview.Should().Be("Stored description");
            edition.ReleaseDate.Should().Be(previous.ReleaseDate);
            incoming.Genres.Should().Contain(stored.Genres).And.Contain("New genre");
            incoming.ForeignBookId.Should().Be(stored.ForeignBookId);
        }

        [TestCase("B0HMVRXW3C", "us")]
        [TestCase("B0H8F9D4GD", "uk")]
        public void should_not_merge_details_across_recordings_or_marketplaces(string asin, string region)
        {
            var fixture = JObject.Parse(ReadAllText("Files/MetadataSource/Audiobook/B0H8F9D4GD.json"));
            var resource = Json.Deserialize<AudnexusBookResource>(fixture["audnexus"].ToString());
            var stored = AudiobookMetadataMapper.Book(resource, "us");
            resource.Asin = asin;
            resource.Region = region;
            resource.Genres = null;
            resource.Subtitle = null;
            var incoming = AudiobookMetadataMapper.Book(resource, region);

            AudiobookMetadataMerger.PreserveMissing(incoming, stored, stored.Editions.Value);

            incoming.Genres.Should().BeEmpty();
            incoming.Editions.Value.Single().Subtitle.Should().BeNull();
        }
    }
}
