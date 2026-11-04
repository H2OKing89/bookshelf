using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.MyAnonaMouse
{
    public class MyAnonaMouseRequestGenerator : IIndexerRequestGenerator
    {
        private readonly MyAnonaMouseSettings _settings;
        private readonly Logger _logger;

        public MyAnonaMouseRequestGenerator(MyAnonaMouseSettings settings, IndexerCapabilities capabilities, Logger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var requests = new IndexerPageableRequestChain();
            requests.Add(GetPagedRequests(string.Empty, true));
            return requests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria criteria)
        {
            var bookQuery = criteria.BookQuery;
            return Search(GetAuthorQueries(criteria).Select(author => author + " " + bookQuery));
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria criteria)
        {
            return Search(GetAuthorQueries(criteria));
        }

        private static IEnumerable<string> GetAuthorQueries(SearchCriteriaBase criteria)
        {
            return new[] { criteria.Author.Name }
                .Concat(criteria.Author.Metadata.Value.Aliases ?? new List<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(SearchCriteriaBase.GetQueryTitle);
        }

        private IndexerPageableRequestChain Search(IEnumerable<string> queries)
        {
            var requests = new IndexerPageableRequestChain();
            var terms = queries.Select(query => query.Replace('+', ' ').Trim())
                .Where(query => !string.IsNullOrWhiteSpace(query))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var term in terms)
            {
                // Search every known spelling even if another spelling returns partial results.
                requests.Add(GetPagedRequests(term, false));
            }

            if (!requests.GetAllTiers().Any())
            {
                _logger.Debug("Skipping empty MAM search.");
            }

            return requests;
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string query, bool recent)
        {
            var term = query.Replace('+', ' ').Trim();
            var fields = new List<string> { "title", "author", "narrator" };
            if (_settings.SearchInDescription)
            {
                fields.Add("description");
            }

            if (_settings.SearchInSeries)
            {
                fields.Add("series");
            }

            if (_settings.SearchInFilenames)
            {
                fields.Add("filenames");
            }

            var searchType = _settings.SearchType switch
            {
                (int)MyAnonaMouseSearchType.Active => "active",
                (int)MyAnonaMouseSearchType.Freeleech => "fl",
                (int)MyAnonaMouseSearchType.FreeleechOrVip => "fl-VIP",
                (int)MyAnonaMouseSearchType.Vip => "VIP",
                (int)MyAnonaMouseSearchType.NotVip => "nVIP",
                _ => "all"
            };

            for (var offset = 0; offset < 1000; offset += 100)
            {
                var request = new IndexerRequest(_settings.BaseUrl.TrimEnd('/') + "/tor/js/loadSearchJSONbasic.php", HttpAccept.Json);
                request.HttpRequest.Method = HttpMethod.Post;
                request.HttpRequest.Headers.ContentType = "application/json";
                request.HttpRequest.SetContent(new
                {
                    tor = new
                    {
                        text = term,
                        srchIn = fields,
                        main_cat = new[] { 13 },
                        searchIn = "torrents",
                        searchType,
                        sortType = recent ? "dateDesc" : "default",
                        startNumber = offset,
                        browse_lang = _settings.SearchLanguages?.ToArray() ?? Array.Empty<int>()
                    },
                    perpage = 100,
                    mediaInfo = "1"
                }.ToJson());
                request.HttpRequest.ContentSummary = "MAM audiobook search";
                var cookies = GetCookies?.Invoke();
                if (cookies != null)
                {
                    foreach (var cookie in cookies)
                    {
                        request.HttpRequest.Cookies[cookie.Key] = cookie.Value;
                    }
                }

                yield return request;
            }
        }
    }
}
