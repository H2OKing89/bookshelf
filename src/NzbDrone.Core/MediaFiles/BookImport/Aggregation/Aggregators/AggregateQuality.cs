using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators
{
    public class AggregateQuality : IAggregate<LocalBook>
    {
        public LocalBook Aggregate(LocalBook item, bool otherFiles)
        {
            var quality = item.FileTrackInfo?.Quality;

            if (quality == null)
            {
                quality = item.FolderTrackInfo?.Quality;
            }

            if (quality == null)
            {
                quality = item.DownloadClientBookInfo?.Quality;
            }

            item.Quality = quality;
            return item;
        }
    }
}
