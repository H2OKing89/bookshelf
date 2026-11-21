using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;
using Readarr.Http;

namespace Readarr.Api.V1.Author
{
    [V1ApiController("authorreconciliation")]
    public class AuthorReconciliationController : Controller
    {
        private readonly IAuthorReconciliationRepository _repository;
        private readonly IManageCommandQueue _commands;

        public AuthorReconciliationController(IAuthorReconciliationRepository repository, IManageCommandQueue commands)
        {
            _repository = repository;
            _commands = commands;
        }

        [HttpGet("candidates")]
        [Produces("application/json")]
        public List<AuthorReconciliationCandidate> Candidates(int sourceAuthorId)
        {
            return _repository.Candidates(sourceAuthorId);
        }

        [HttpPost("preview")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public AuthorReconciliationPreview Preview([FromBody] AuthorReconciliationRequest request)
        {
            return _repository.Preview(request);
        }

        [HttpPost]
        [Consumes("application/json")]
        [Produces("application/json")]
        public ActionResult<AuthorReconciliationQueuedResource> Apply([FromBody] AuthorReconciliationConfirmation confirmation)
        {
            var preview = _repository.Preview(confirmation);
            preview.ValidateConfirmation(confirmation);

            var command = new AuthorReconciliationCommand
            {
                Confirmation = confirmation,
                Trigger = CommandTrigger.Manual
            };
            var queued = _commands.Push(command, CommandPriority.Normal, CommandTrigger.Manual);
            return Accepted(new AuthorReconciliationQueuedResource { CommandId = queued.Id });
        }
    }

    public class AuthorReconciliationQueuedResource
    {
        public int CommandId { get; set; }
    }
}
