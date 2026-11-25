using System;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Validation;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;

namespace Readarr.Api.V1.Indexers
{
    [V1ApiController]
    public class IndexerController : ProviderControllerBase<IndexerResource, IndexerBulkResource, IIndexer, IndexerDefinition>
    {
        public static readonly IndexerResourceMapper ResourceMapper = new ();
        public static readonly IndexerBulkResourceMapper BulkResourceMapper = new ();

        public IndexerController(IndexerFactory indexerFactory, DownloadClientExistsValidator downloadClientExistsValidator)
            : base(indexerFactory, "indexer", ResourceMapper, BulkResourceMapper)
        {
            SharedValidator.RuleFor(c => c.Priority).InclusiveBetween(1, 50);
            SharedValidator.RuleFor(c => c.DownloadClientId).SetValidator(downloadClientExistsValidator);
            SharedValidator.RuleFor(c => c.DangerousFileAction).Must(ValidAction);
            SharedValidator.RuleFor(c => c.ExecutableFileAction).Must(ValidAction);
            SharedValidator.RuleFor(c => c.UserRejectedExtensionAction).Must(ValidAction);
        }

        private static bool ValidAction(RejectedImportAction? action) => !action.HasValue || Enum.IsDefined(action.Value);

        [RestPutById]
        [Consumes("application/json")]
        [Produces("application/json")]
        public override ActionResult<IndexerResource> UpdateProvider([FromBody] IndexerResource resource, [FromQuery] bool forceSave = false)
        {
            // Preserve the newly added choices for older v1 clients that omit them.
            // An explicit Review (zero) is not omission.
            if (!resource.DangerousFileAction.HasValue || !resource.ExecutableFileAction.HasValue || !resource.UserRejectedExtensionAction.HasValue)
            {
                var saved = GetResourceById(resource.Id);
                resource.DangerousFileAction ??= saved.DangerousFileAction;
                resource.ExecutableFileAction ??= saved.ExecutableFileAction;
                resource.UserRejectedExtensionAction ??= saved.UserRejectedExtensionAction;
            }

            return base.UpdateProvider(resource, forceSave);
        }
    }
}
