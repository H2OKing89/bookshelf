using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class audiobook_metadata_overridesFixture : MigrationTest<audiobook_metadata_overrides>
    {
        [Test]
        public void should_seed_editable_corrections_with_provenance()
        {
            var db = WithMigrationTestDb();
            var rows = db.Query<AudiobookMetadataOverride>("SELECT * FROM \"AudiobookMetadataOverrides\"");

            rows.Should().HaveCount(3);
            rows.Should().OnlyContain(row => row.Id > 0 && row.Enabled && row.UpdatedAt != default &&
                !string.IsNullOrWhiteSpace(row.SourceUrl) && !string.IsNullOrWhiteSpace(row.Reason));
            rows.Single(row => row.Name == "Kumanano").Aliases.Should().Contain("くまなの");
            rows.Single(row => row.Name == "Rifujin na Magonote").AlternateAsins.Should().Contain("B076N4Q46Q");
            rows.Single(row => row.Kind == "title").OriginalTitle.Should().Contain("Reincarnatio,");
        }
    }
}
