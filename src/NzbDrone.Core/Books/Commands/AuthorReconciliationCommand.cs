using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    public class AuthorReconciliationCommand : Command
    {
        public AuthorReconciliationConfirmation Confirmation { get; set; }
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsExclusive => true;
        public override bool UpdateScheduledTask => false;
        public override string CompletionMessage => "Author profiles reconciled";
    }
}
