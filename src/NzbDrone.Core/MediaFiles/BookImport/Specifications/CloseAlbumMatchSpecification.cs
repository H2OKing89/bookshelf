using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class CloseBookMatchSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private const double _bookThreshold = 0.50;
        private const double AudioIdentityThreshold = 0.35;
        private readonly Logger _logger;

        public CloseBookMatchSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            double dist;
            string reasons;

            var audioFiles = item.LocalBooks.Where(file => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(file.Path))).ToList();
            if (audioFiles.Any() && audioFiles.All(file => string.IsNullOrWhiteSpace(file.FileTrackInfo?.Asin)))
            {
                // A work's title and author do not identify its performance. Only a local
                // recording confirmed by the user can be matched automatically without an ASIN.
                if (!LocalRecordingId.IsLocal(item.Edition?.ForeignEditionId) &&
                    !LocalRecordingId.IsLocal(item.Edition?.Book?.Value?.ForeignBookId))
                {
                    _logger.Debug("Audio match without an ASIN needs recording identity confirmation: {0}", item);
                    return Decision.Reject("Audio files without an ASIN need recording identity confirmation; select the recording manually or create a local recording");
                }

                // Matching language, year, or publisher must not outweigh a different work or
                // author when the files have no recording identifier. Explicit manual imports
                // bypass these automatic-match specifications and remain available for review.
                foreach (var field in new[] { "book", "author" })
                {
                    if (!item.Distance.Penalties.TryGetValue(field, out var penalties) ||
                        !penalties.Any() || penalties.Max() > AudioIdentityThreshold)
                    {
                        var label = field == "book" ? "title" : "author";
                        _logger.Debug("Audio match without an ASIN has insufficient {0} evidence: {1}", label, item);
                        return Decision.Reject($"Audio files without an ASIN need a closer {label} match; select the correct recording manually");
                    }
                }
            }

            // strict when a new download
            if (item.NewDownload)
            {
                dist = item.Distance.NormalizedDistance();
                reasons = item.Distance.Reasons;
                if (dist > _bookThreshold)
                {
                    _logger.Debug($"Book match is not close enough: {dist} vs {_bookThreshold} {reasons}. Skipping {item}");
                    return Decision.Reject($"Book match is not close enough: {1 - dist:P1} vs {1 - _bookThreshold:P0} {reasons}");
                }
            }

            // otherwise importing existing files in library
            else
            {
                // get book distance ignoring whether tracks are missing
                dist = item.Distance.NormalizedDistanceExcluding(new List<string> { "missing_tracks", "unmatched_tracks" });
                reasons = item.Distance.Reasons;
                if (dist > _bookThreshold)
                {
                    _logger.Debug($"Book match is not close enough: {dist} vs {_bookThreshold} {reasons}. Skipping {item}");
                    return Decision.Reject($"Book match is not close enough: {1 - dist:P1} vs {1 - _bookThreshold:P0} {reasons}");
                }
            }

            _logger.Debug($"Accepting release {item}: dist {dist} vs {_bookThreshold} {reasons}");
            return Decision.Accept();
        }
    }
}
