using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(160)]
    public class add_recording_language_evidence : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("BookFiles").AddColumn("TaggedLanguage").AsString(int.MaxValue).Nullable();
            Alter.Table("Blocklist").AddColumn("RecordingLanguages").AsString(128).Nullable();
            Alter.Table("Blocklist").AddColumn("SelectedEditionLanguageId").AsInt32().Nullable();
        }
    }
}
