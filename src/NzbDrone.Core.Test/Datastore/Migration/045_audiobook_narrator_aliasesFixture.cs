using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class audiobook_narrator_aliasesFixture : MigrationTest<audiobook_narrator_aliases>
    {
        [Test]
        public void should_preserve_user_corrections_and_add_a_narrator_without_an_asin()
        {
            var db = WithMigrationTestDb(migration => migration.Insert.IntoTable("AudiobookMetadataOverrides").Row(new
            {
                Kind = "title", Region = "uk", Asin = "B000000001", OriginalTitle = "Original", ReplacementTitle = "Corrected",
                AlternateAsins = "[]", Aliases = "[]", SourceUrl = "https://publisher.example/book", Reason = "User correction",
                Enabled = false, UpdatedAt = DateTime.UtcNow
            }));
            var rows = db.Query<AudiobookMetadataOverride>("SELECT * FROM \"AudiobookMetadataOverrides\"");

            rows.Should().HaveCount(5);
            rows.Should().ContainSingle(row => row.Region == "uk" && row.Asin == "B000000001" && !row.Enabled && row.ReplacementTitle == "Corrected");
            var narrator = rows.Single(row => row.Kind == "narrator");
            narrator.Asin.Should().BeNull();
            narrator.Name.Should().Be("Cliff Kirk");
            narrator.Aliases.Should().Contain("Cliff Kurt");
            narrator.SourceUrl.Should().NotBeNullOrWhiteSpace();
        }
    }
}
