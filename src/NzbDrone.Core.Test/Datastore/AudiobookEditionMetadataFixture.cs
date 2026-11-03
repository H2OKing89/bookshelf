using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class AudiobookEditionMetadataFixture : DbTest<BasicRepository<Edition>, Edition>
    {
        [Test]
        public void should_store_and_read_recording_metadata()
        {
            Subject.Insert(new Edition
            {
                ForeignEditionId = "primary-edition",
                Title = "Project Hail Mary",
                TitleSlug = "primary-edition",
                Asin = "B08G9PRS1K",
                AudibleRegion = "us",
                Narrators = new List<string> { "Ray Porter", "Another Narrator" },
                RuntimeLengthMinutes = 970
            });

            StoredModel.AudibleRegion.Should().Be("us");
            StoredModel.Narrators.Should().Equal("Ray Porter", "Another Narrator");
            StoredModel.RuntimeLengthMinutes.Should().Be(970);
        }
    }
}
