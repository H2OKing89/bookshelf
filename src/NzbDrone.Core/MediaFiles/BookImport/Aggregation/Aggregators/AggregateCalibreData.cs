using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators
{
    public class AggregateCalibreData : IAggregate<LocalBook>
    {
        private readonly Logger _logger;
        private readonly ICached<CalibreBook> _bookCache;

        public AggregateCalibreData(Logger logger,
                                    ICacheManager cacheManager)
        {
            _logger = logger;
            _bookCache = cacheManager.GetCache<CalibreBook>(typeof(CalibreProxy));
        }

        public LocalBook Aggregate(LocalBook item, bool otherFiles)
        {
            var book = _bookCache.Find(item.Path);
            _logger.Trace($"Searching calibre data for {item.Path}");

            if (book != null)
            {
                _logger.Trace($"Using calibre data for {item.Path}:\n{book.ToJson()}");

                item.CalibreId = book.Id;

                var parsed = item.FileTrackInfo;
                parsed.Asin = book.Identifiers.GetValueOrDefault("mobi-asin") ?? book.Identifiers.GetValueOrDefault("asin");
                parsed.Isbn = book.Identifiers.GetValueOrDefault("isbn");
                parsed.GoodreadsId = book.Identifiers.GetValueOrDefault("goodreads");
                parsed.Authors = book.Authors;
                parsed.BookTitle = book.Title;
            }

            return item;
        }
    }
}
