using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public class AuthorReconciliationService : IExecute<AuthorReconciliationCommand>
    {
        private readonly IAuthorReconciliationRepository _repository;
        private readonly IEventAggregator _events;

        public AuthorReconciliationService(IAuthorReconciliationRepository repository, IEventAggregator events)
        {
            _repository = repository;
            _events = events;
        }

        public void Execute(AuthorReconciliationCommand message)
        {
            var confirmation = message.Confirmation;
            try
            {
                _repository.Apply(confirmation);
            }
            catch (NzbDroneClientException error)
            {
                throw new CommandFailedException(error.Message);
            }

            _events.PublishEvent(new AuthorReconciledEvent(confirmation.SourceAuthorId, confirmation.TargetAuthorId));
            _events.PublishEvent(new PendingReleasesUpdatedEvent());
        }
    }
}
