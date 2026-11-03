using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class audiobook_edition_metadataFixture : MigrationTest<audiobook_edition_metadata>
    {
        [Test]
        public void should_preserve_existing_editions_with_empty_audio_fields()
        {
            var db = WithMigrationTestDb(migration => migration.Insert.IntoTable("Editions").Row(new
            {
                BookId = 0,
                ForeignEditionId = "existing-edition",
                Title = "Existing book",
                TitleSlug = "existing-book",
                Asin = "B08G9PRS1K",
                Images = "[]",
                Monitored = true,
                ManualAdd = false
            }));

            var edition = db.Query<Edition>("SELECT * FROM \"Editions\"").Single();
            edition.ForeignEditionId.Should().Be("existing-edition");
            edition.Asin.Should().Be("B08G9PRS1K");
            edition.Monitored.Should().BeTrue();
            edition.Narrators.Should().BeEmpty();
            edition.RuntimeLengthMinutes.Should().BeNull();
            edition.AudibleRegion.Should().BeNull();
        }
    }
}
