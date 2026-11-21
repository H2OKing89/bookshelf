using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    public class RefreshStaleMediaInfoCommand : Command
    {
        public override bool RequiresDiskAccess => true;
        public override bool IsTypeExclusive => true;
        public override bool IsLongRunning => true;
    }
}
