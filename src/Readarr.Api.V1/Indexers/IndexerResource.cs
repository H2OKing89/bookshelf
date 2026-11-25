using NzbDrone.Core.Indexers;

namespace Readarr.Api.V1.Indexers
{
    public class IndexerResource : ProviderResource<IndexerResource>
    {
        public bool EnableRss { get; set; }
        public bool EnableAutomaticSearch { get; set; }
        public bool EnableInteractiveSearch { get; set; }
        public bool SupportsRss { get; set; }
        public bool SupportsSearch { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public int Priority { get; set; }
        public int DownloadClientId { get; set; }
        public RejectedImportAction? DangerousFileAction { get; set; }
        public RejectedImportAction? ExecutableFileAction { get; set; }
        public RejectedImportAction? UserRejectedExtensionAction { get; set; }
    }

    public class IndexerResourceMapper : ProviderResourceMapper<IndexerResource, IndexerDefinition>
    {
        public override IndexerResource ToResource(IndexerDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            var resource = base.ToResource(definition);

            resource.EnableRss = definition.EnableRss;
            resource.EnableAutomaticSearch = definition.EnableAutomaticSearch;
            resource.EnableInteractiveSearch = definition.EnableInteractiveSearch;
            resource.SupportsRss = definition.SupportsRss;
            resource.SupportsSearch = definition.SupportsSearch;
            resource.Protocol = definition.Protocol;
            resource.Priority = definition.Priority;
            resource.DownloadClientId = definition.DownloadClientId;
            resource.DangerousFileAction = definition.DangerousFileAction;
            resource.ExecutableFileAction = definition.ExecutableFileAction;
            resource.UserRejectedExtensionAction = definition.UserRejectedExtensionAction;

            return resource;
        }

        public override IndexerDefinition ToModel(IndexerResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var definition = base.ToModel(resource);

            definition.EnableRss = resource.EnableRss;
            definition.EnableAutomaticSearch = resource.EnableAutomaticSearch;
            definition.EnableInteractiveSearch = resource.EnableInteractiveSearch;
            definition.Priority = resource.Priority;
            definition.DownloadClientId = resource.DownloadClientId;
            definition.DangerousFileAction = resource.DangerousFileAction ?? RejectedImportAction.Review;
            definition.ExecutableFileAction = resource.ExecutableFileAction ?? RejectedImportAction.Review;
            definition.UserRejectedExtensionAction = resource.UserRejectedExtensionAction ?? RejectedImportAction.Review;

            return definition;
        }
    }
}
