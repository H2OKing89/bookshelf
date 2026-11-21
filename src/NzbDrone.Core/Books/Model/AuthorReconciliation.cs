using System.Collections.Generic;
using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Books
{
    public class AuthorReconciliationRequest
    {
        public int SourceAuthorId { get; set; }
        public int TargetAuthorId { get; set; }
        public string EvidenceUrl { get; set; }
        public string Reason { get; set; }
    }

    public class AuthorReconciliationConfirmation : AuthorReconciliationRequest
    {
        public string PreviewToken { get; set; }
        public bool KeepCanonicalSettings { get; set; }
    }

    public class AuthorReconciliationCandidate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ForeignAuthorId { get; set; }
    }

    public class AuthorReconciliationProfile : AuthorReconciliationCandidate
    {
        public int AuthorMetadataId { get; set; }
        public string Path { get; set; }
        public string TitleSlug { get; set; }
        public int QualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }
        public bool Monitored { get; set; }
        public NewItemMonitorTypes MonitorNewItems { get; set; }
        public HashSet<int> Tags { get; set; }
    }

    public class AuthorReconciliationRecording
    {
        public int BookId { get; set; }
        public string ForeignBookId { get; set; }
        public string Title { get; set; }
        public int EditionId { get; set; }
        public string ForeignEditionId { get; set; }
        public string Asin { get; set; }
        public string AudibleRegion { get; set; }
        public bool Monitored { get; set; }
    }

    public class AuthorReconciliationFile
    {
        public int Id { get; set; }
        public int EditionId { get; set; }
        public string Path { get; set; }
    }

    public class AuthorReconciliationReference
    {
        public string Table { get; set; }
        public int Count { get; set; }
    }

    public class AuthorReconciliationPreview
    {
        public AuthorReconciliationProfile Source { get; set; }
        public AuthorReconciliationProfile Target { get; set; }
        public List<AuthorReconciliationRecording> Recordings { get; set; }
        public List<AuthorReconciliationFile> Files { get; set; }
        public List<AuthorReconciliationReference> References { get; set; }
        public List<string> SettingsDifferences { get; set; }
        public List<string> Blockers { get; set; }
        public int? ExistingOverrideId { get; set; }
        public string PreviewToken { get; set; }
        public bool CanApply => Blockers.Count == 0;

        public void ValidateConfirmation(AuthorReconciliationConfirmation confirmation)
        {
            if (PreviewToken != confirmation.PreviewToken)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "The author reconciliation preview changed. Review the current author profiles again.");
            }

            if (!CanApply)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, Blockers[0]);
            }

            if (SettingsDifferences.Count > 0 && !confirmation.KeepCanonicalSettings)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Confirm the canonical author's settings before applying this reconciliation.");
            }
        }
    }
}
