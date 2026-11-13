using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    public class RefreshMediaInfoCommand : Command
    {
        public int? AuthorId { get; set; }
        public int? BookId { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsTypeExclusive => true;
        public override bool IsLongRunning => true;
        public override bool UpdateScheduledTask => false;
        public override string CompletionMessage => "Media info refresh completed";
    }
}
