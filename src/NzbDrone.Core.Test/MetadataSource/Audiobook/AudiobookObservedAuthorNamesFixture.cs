using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audiobook
{
    [TestFixture]
    public class AudiobookObservedAuthorNamesFixture : CoreTest<AudiobookInfoProxy>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAudiobookMetadataOverrideRepository>().Setup(r => r.All()).Returns(AudiobookMetadataOverrideSeed.Read());
            Mocker.SetConstant<IAudiobookMetadataOverrideService>(Mocker.Resolve<AudiobookMetadataOverrideService>());
        }

        [TestCase("Mitizou")]
        [TestCase("Miya Kazuki")]
        [TestCase("Pirateaba")]
        public void should_remember_book_credit_spellings_when_the_profile_name_differs(string query)
        {
            var fixtures = JArray.Parse(ReadAllText("Files/MetadataSource/Audiobook/AuthorSpellings.json"));
            var fixture = fixtures.Single(f => f.Value<string>("query") == query);
            var profile = Json.Deserialize<AudnexusAuthorResource>(fixture["profile"].ToString());
            var book = Json.Deserialize<AudnexusBookResource>(fixture["book"].ToString());
            var credit = book.Authors.Single(a => a.Asin == profile.Asin).Name;
            var id = AudiobookId.ForAsin("us", profile.Asin);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudnexusRegion).Returns("us");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudibleRsEnabled).Returns(true);
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.SearchBooks(query, "us"))
                .Returns(new List<AudnexusBookResource> { book });
            Mocker.GetMock<IAudnexusProxy>().Setup(x => x.GetAuthor(profile.Asin, "us")).Returns(profile);
            Mocker.GetMock<IAudibleCatalogProxy>().Setup(x => x.GetAuthorBooks(It.IsAny<string>(), "us"))
                .Returns((string name, string region) => name == credit ? new List<AudnexusBookResource> { book } : new List<AudnexusBookResource>());

            Subject.SearchForNewAuthor(query);
            var first = Subject.GetAuthorInfo(id);

            var expectedName = AudiobookAuthorIdentity.IsLatinName(profile.Name) ? profile.Name : credit;
            first.Name.Should().Be(expectedName);
            first.Metadata.Value.Aliases.Should().Contain(expectedName == credit ? profile.Name : credit);
            first.Books.Value.Should().ContainSingle(b => b.ForeignBookId == AudiobookId.ForAsin("us", book.Asin));

            // Refresh after a restart must retain persisted aliases even when the
            // provider's profile still uses a different spelling from its books.
            var stored = Json.Deserialize<AuthorMetadata>(first.Metadata.Value.ToJson());
            stored.Id = 8;
            Mocker.GetMock<IAuthorService>().Setup(x => x.FindById(id))
                .Returns(new Author { Id = 7, AuthorMetadataId = 8, Metadata = stored });
            Mocker.GetMock<IBookService>().Setup(x => x.GetBooksByAuthorMetadataId(8)).Returns(new List<Book>());
            Mocker.Resolve<ICacheManager>().GetCache<AuthorMetadata>(typeof(AudiobookInfoProxy), "AudiobookAuthors").Clear();

            var refreshed = Subject.GetAuthorInfo(id);

            refreshed.Name.Should().Be(expectedName);
            refreshed.Metadata.Value.Aliases.Should().Contain(expectedName == credit ? profile.Name : credit);
            refreshed.Books.Value.Should().ContainSingle(b => b.ForeignBookId == AudiobookId.ForAsin("us", book.Asin));
            Mocker.GetMock<IAudibleCatalogProxy>().Verify(x => x.GetAuthorBooks(credit, "us"), Times.Exactly(2));
        }

        [TestCase("audible:us:B000000002")]
        [TestCase("audible:jp:B000000001")]
        public void should_not_learn_aliases_from_a_different_identity(string previousId)
        {
            var current = new AuthorMetadata { ForeignAuthorId = "audible:us:B000000001", Name = "Miya Kazuki" };
            var previous = new AuthorMetadata { ForeignAuthorId = previousId, Name = "香月美夜" };

            AudiobookAuthorIdentity.PreserveNames(current, previous);

            current.Aliases.Should().BeEmpty();
        }
    }
}
