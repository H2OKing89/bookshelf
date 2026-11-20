using System.Collections.Generic;
using System.IO;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class LocalBookCustomFormatScores
    {
        public List<CustomFormat> OriginalFormats { get; set; }
        public int OriginalScore { get; set; }
        public List<CustomFormat> IntendedFormats { get; set; }
        public int IntendedScore { get; set; }
    }

    public interface ILocalBookCustomFormatCalculationService
    {
        LocalBookCustomFormatScores Calculate(LocalBook localBook, DownloadClientItem downloadClientItem);
    }

    public class LocalBookCustomFormatCalculationService : ILocalBookCustomFormatCalculationService
    {
        private readonly IBuildFileNames _fileNames;
        private readonly ICustomFormatCalculationService _formats;
        private readonly IHistoryService _history;

        public LocalBookCustomFormatCalculationService(IBuildFileNames fileNames,
                                                       ICustomFormatCalculationService formats,
                                                       IHistoryService history)
        {
            _fileNames = fileNames;
            _formats = formats;
            _history = history;
        }

        public LocalBookCustomFormatScores Calculate(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            var candidate = ImportBookFileMetadata.Create(localBook, downloadClientItem, _history);
            var original = _formats.ParseCustomFormat(candidate, localBook.Author);
            var intendedName = _fileNames.BuildBookFileName(localBook.Author, localBook.Edition, candidate, null, original);
            candidate.Path = intendedName + Path.GetExtension(localBook.Path);
            var intended = _formats.ParseCustomFormat(candidate, localBook.Author);
            var profile = localBook.Author.QualityProfile.Value;

            return new LocalBookCustomFormatScores
            {
                OriginalFormats = original,
                OriginalScore = profile.CalculateCustomFormatScore(original),
                IntendedFormats = intended,
                IntendedScore = profile.CalculateCustomFormatScore(intended)
            };
        }
    }
}
