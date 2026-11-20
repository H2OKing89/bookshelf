using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class RejectedFileExtensionSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly IRejectedFileExtensionService _extensions;

        public RejectedFileExtensionSpecification(IRejectedFileExtensionService extensions)
        {
            _extensions = extensions;
        }

        public Decision IsSatisfiedBy(LocalBook item, DownloadClientItem downloadClientItem)
        {
            var rejection = item.ExistingFile ? null : _extensions.GetRejection(item.Path);
            if (!item.ExistingFile && rejection == null && _extensions.Classify(item.Path) == DownloadFileKind.Sidecar)
            {
                rejection = new Rejection("Supporting files are not standalone books", code: RejectionCode.UnsupportedFileExtension);
            }

            return rejection == null ? Decision.Accept() : Decision.RejectCode(rejection.Code, rejection.Reason);
        }
    }
}
