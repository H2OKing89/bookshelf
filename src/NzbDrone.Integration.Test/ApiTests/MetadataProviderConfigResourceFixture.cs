using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using Readarr.Api.V1.Config;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class MetadataProviderConfigResourceFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_expose_the_path_asin_preference(bool enabled)
        {
            var config = new Mock<IConfigService>();
            config.Setup(x => x.PreferPathAsin).Returns(enabled);

            var resource = MetadataProviderConfigResourceMapper.ToResource(config.Object);

            resource.PreferPathAsin.Should().Be(enabled);
            Json.Deserialize<MetadataProviderConfigResource>(resource.ToJson()).PreferPathAsin.Should().Be(enabled);
        }
    }
}
