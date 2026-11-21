using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class UpgradeSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly IConfigService _configService;
        private readonly ICustomFormatCalculationService _customFormatCalculationService;
        private readonly ILocalBookCustomFormatCalculationService _localFormats;
        private readonly Logger _logger;

        public UpgradeSpecification(IConfigService configService,
                                    ICustomFormatCalculationService customFormatCalculationService,
                                    ILocalBookCustomFormatCalculationService localFormats,
                                    Logger logger)
        {
            _configService = configService;
            _customFormatCalculationService = customFormatCalculationService;
            _localFormats = localFormats;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook item, DownloadClientItem downloadClientItem)
        {
            // A different narration, marketplace or local recording is not the
            // replacement target. This matches the staged import's ownership scope.
            var files = item.Book?.BookFiles?.Value?.Where(file => item.Edition?.Id > 0 && file.EditionId == item.Edition.Id).ToList();
            if (files == null || !files.Any())
            {
                return Decision.Accept();
            }

            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;
            var qualityProfile = item.Author.QualityProfile.Value;
            var qualityComparer = new QualityModelComparer(qualityProfile);
            LocalBookCustomFormatScores candidate = null;

            foreach (var bookFile in files)
            {
                var qualityCompare = qualityComparer.Compare(item.Quality.Quality, bookFile.Quality.Quality);
                var revisionCompare = item.Quality.Revision.CompareTo(bookFile.Quality.Revision);
                if (qualityCompare < 0)
                {
                    return Decision.RejectCode(RejectionCode.QualityNotUpgrade, "Not an upgrade for existing book file(s)");
                }

                if (qualityCompare == 0 && downloadPropersAndRepacks != ProperDownloadTypes.DoNotPrefer &&
                    revisionCompare < 0)
                {
                    return Decision.RejectCode(RejectionCode.RevisionNotUpgrade, "Not a quality revision upgrade for existing book file(s)");
                }

                // Reidentifying an existing file publishes no replacement. Higher
                // qualities retain the established quality-before-format ordering.
                if (item.ExistingFile || qualityCompare != 0)
                {
                    continue;
                }

                candidate ??= _localFormats.Calculate(item, downloadClientItem);
                var currentFormats = _customFormatCalculationService.ParseCustomFormat(bookFile);
                var currentScore = qualityProfile.CalculateCustomFormatScore(currentFormats);
                if (candidate.IntendedScore < currentScore)
                {
                    _logger.Debug("Recording {0}, part {1}: intended custom format score {2} is below existing {3}",
                        bookFile.EditionId,
                        bookFile.Part,
                        candidate.IntendedScore,
                        currentScore);
                    if (candidate.OriginalScore >= currentScore && candidate.OriginalScore > candidate.IntendedScore)
                    {
                        return Decision.RejectCode(RejectionCode.CustomFormatLostAfterRename,
                            "Custom format score would decrease after renaming: original {0}, intended {1}, existing recording part {2}",
                            candidate.OriginalScore,
                            candidate.IntendedScore,
                            currentScore);
                    }

                    return Decision.RejectCode(RejectionCode.CustomFormatDowngrade,
                        "Custom format score {0} is below existing recording part score {1}",
                        candidate.IntendedScore,
                        currentScore);
                }

                // Preferred revisions keep their existing precedence, including the downgrade guard above.
                if ((revisionCompare <= 0 || downloadPropersAndRepacks == ProperDownloadTypes.DoNotPrefer) &&
                    !qualityProfile.MeetsMinimumUpgradeFormatScore(currentScore, candidate.IntendedScore))
                {
                    return Decision.RejectCode(RejectionCode.CustomFormatUpgradeBelowMinimum,
                        "Custom format score after renaming must improve recording part score {0} by at least {1}; intended score is {2}",
                        currentScore,
                        qualityProfile.MinUpgradeFormatScore,
                        candidate.IntendedScore);
                }
            }

            // Every incoming part must meet every replaced part. Scores are not
            // summed or averaged, so extra parts cannot conceal a lower score.
            return Decision.Accept();
        }
    }
}
