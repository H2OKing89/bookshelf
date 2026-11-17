namespace NzbDrone.Core.Messaging.Commands
{
    // Only commands with cancellation-safe work boundaries may opt into user cancellation.
    public interface ICancellableCommand
    {
    }
}
