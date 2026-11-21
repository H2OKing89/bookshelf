using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class CommittedImportScores
    {
        public const string HistoryKey = "committedImportScoresV1";
        private const int MaximumSnapshotLength = 32 * 1024;

        public int ProfileId { get; set; }
        public List<CommittedFileScore> Before { get; set; }
        public CommittedFileScore After { get; set; }

        public static string Capture(TrackImportedEvent message, ICustomFormatCalculationService calculator)
        {
            var profile = message.BookInfo.Author.QualityProfile?.Value;
            if (profile?.Id is not > 0 || calculator == null || message.ImportedBook.Id <= 0 ||
                message.OldFiles == null || message.OldFiles.Count > 256 || message.OldFiles.Any(file => file?.Id is not > 0))
            {
                return null;
            }

            var author = message.BookInfo.Author;
            var scores = new CommittedImportScores
            {
                ProfileId = profile.Id,
                Before = message.OldFiles.DistinctBy(file => file.Id).OrderBy(file => file.Id)
                    .Select(file => Score(file, profile, calculator.ParseCustomFormat(file, author))).ToList(),
                After = Score(message.ImportedBook, profile, calculator.ParseCustomFormat(message.ImportedBook, author))
            };

            if (scores.After == null || scores.Before.Any(file => file == null))
            {
                return null;
            }

            var stored = STJson.ToJson(scores);
            return stored.Length <= MaximumSnapshotLength ? stored : null;
        }

        public static CommittedImportScores Read(string stored, BookFile file)
        {
            if (string.IsNullOrWhiteSpace(stored) || stored.Length > MaximumSnapshotLength ||
                !STJson.TryDeserialize<CommittedImportScores>(stored, out var scores) || scores?.ProfileId is not > 0 ||
                scores.After?.FileId != file.Id || scores.After.EditionId != file.EditionId || scores.After.Part != file.Part || scores.Before == null)
            {
                return null;
            }

            return scores;
        }

        private static CommittedFileScore Score(BookFile file, QualityProfile profile, List<CustomFormat> formats)
        {
            if (formats == null || formats.Count > 256)
            {
                return null;
            }

            return new CommittedFileScore
            {
                FileId = file.Id,
                EditionId = file.EditionId,
                Part = file.Part,
                Score = profile.CalculateCustomFormatScore(formats),
                CustomFormatIds = formats.Select(format => format.Id).Where(id => id > 0).Distinct().OrderBy(id => id).ToList()
            };
        }
    }

    public class CommittedFileScore
    {
        public int FileId { get; set; }
        public int EditionId { get; set; }
        public int Part { get; set; }
        public int Score { get; set; }
        public List<int> CustomFormatIds { get; set; }
    }
}
