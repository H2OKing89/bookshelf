using System;
using System.Threading;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public class MigrationContext
    {
        private static readonly AsyncLocal<MigrationContext> CurrentContext = new AsyncLocal<MigrationContext>();

        public static MigrationContext Current
        {
            get => CurrentContext.Value;
            set => CurrentContext.Value = value;
        }

        public MigrationType MigrationType { get; private set; }
        public long? DesiredVersion { get; set; }
        public Action<NzbDroneMigrationBase> BeforeMigration { get; set; }

        public MigrationContext(MigrationType migrationType, long? desiredVersion = null)
        {
            MigrationType = migrationType;
            DesiredVersion = desiredVersion;
        }
    }
}
