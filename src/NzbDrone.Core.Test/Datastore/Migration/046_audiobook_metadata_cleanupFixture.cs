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
    public class audiobook_metadata_cleanupFixture : MigrationTest<audiobook_metadata_cleanup>
    {
        [Test]
        public void should_preserve_existing_overrides_and_seed_disabled_rules_once()
        {
            var db = WithMigrationTestDb(migration => migration.Insert.IntoTable("AudiobookMetadataOverrides").Row(new
            {
                Kind = "title", Region = "uk", Asin = "B000000001", OriginalTitle = "Original", ReplacementTitle = "Corrected",
                AlternateAsins = "[]", Aliases = "[]", SourceUrl = "https://publisher.example/book", Reason = "User correction",
                Enabled = false, UpdatedAt = DateTime.UtcNow
            }));
            var overrides = db.Query<AudiobookMetadataOverride>("SELECT * FROM \"AudiobookMetadataOverrides\"");
            var rules = db.Query<AudiobookMetadataCleanupRule>("SELECT * FROM \"AudiobookMetadataCleanupRules\"");

            overrides.Should().ContainSingle(item => item.Region == "uk" && item.ReplacementTitle == "Corrected" && !item.Enabled);
            rules.Should().HaveCount(AudiobookMetadataCleanupRuleSeed.Read().Count);
            rules.Should().OnlyContain(item => !item.Enabled);
            rules.Single(item => item.Phrase == "Light Novel").Labels.Should().Equal("Light Novel");
            db.Query("SELECT * FROM \"AudiobookMetadataOverrides\" WHERE \"Asin\" = 'B000000001'")
                .Single().Should().ContainKey("OriginalSubtitle");
        }
    }
}
