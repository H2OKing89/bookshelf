using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audnexus;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource.Audible
{
    public interface IAudibleCatalogProxy
    {
        List<AudnexusBookResource> GetAuthorBooks(string name, string region);
        AudnexusBookResource GetBook(string asin, string region);
    }

    public class AudibleCatalogProxy : IAudibleCatalogProxy
    {
        private const string ResponseGroups = "product_desc,contributors,series,product_attrs,product_details,media,rating,category_ladders";
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private DateTime _unavailableUntil;

        public AudibleCatalogProxy(IHttpClient httpClient, IDiskProvider diskProvider, IConfigService configService)
        {
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _configService = configService;
        }

        public List<AudnexusBookResource> GetAuthorBooks(string name, string region)
        {
            const int pageSize = 50;
            var products = new List<AudnexusBookResource>();
            var seen = new HashSet<string>();
            for (var page = 0; page < 20; page++)
            {
                var body = Read("/1.0/catalog/products", region, new Dictionary<string, object>
                {
                    { "author", name }, { "page", page }, { "num_results", pageSize },
                    { "products_sort_by", "ReleaseDate" }, { "response_groups", ResponseGroups }
                });
                if (body["products"] is not JArray items || body["total_results"]?.Type != JTokenType.Integer)
                {
                    throw new BookInfoException("audible-rs returned an incomplete catalog page.");
                }

                var total = body.Value<int>("total_results");
                if (total < 0 || total > 1000)
                {
                    throw new BookInfoException("This author catalog exceeds the 1,000-recording limit. Existing recordings have been retained.");
                }

                foreach (var item in items)
                {
                    var product = MapProduct(item, region);
                    if (!seen.Add(product.Asin))
                    {
                        throw new BookInfoException("audible-rs repeated a catalog page. Existing recordings have been retained.");
                    }

                    products.Add(product);
                }

                if (products.Count >= total)
                {
                    return products;
                }

                if (items.Count == 0)
                {
                    throw new BookInfoException("audible-rs returned a truncated catalog. Existing recordings have been retained.");
                }
            }

            throw new BookInfoException("Unable to finish reading this author catalog. Existing recordings have been retained.");
        }

        public AudnexusBookResource GetBook(string asin, string region)
        {
            var body = Read("/1.0/catalog/products/" + asin, region, new Dictionary<string, object> { { "response_groups", ResponseGroups } });
            if (body["product"] is not JObject product || product.Value<string>("asin") != asin)
            {
                throw new BookInfoException("audible-rs returned a different recording or an incomplete product.");
            }

            return MapProduct(product, region);
        }

        private JObject Read(string path, string region, Dictionary<string, object> query)
        {
            if (!_configService.AudibleRsEnabled || DateTime.UtcNow < _unavailableUntil)
            {
                throw new BookInfoException("audible-rs is disabled or temporarily unavailable. Check Settings → Metadata.");
            }

            try
            {
                var origin = new Uri(_configService.AudibleRsUrl.TrimEnd('/'));
                if (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback))
                {
                    throw new BookInfoException("audible-rs requires HTTPS, or HTTP on localhost.");
                }

                var token = _diskProvider.ReadAllText(_configService.AudibleRsTokenFile).Trim();
                if (token.IsNullOrWhiteSpace() || token.Contains('\r') || token.Contains('\n'))
                {
                    throw new BookInfoException("The audible-rs token file must contain one API token.");
                }

                var builder = new HttpRequestBuilder(origin.AbsoluteUri.TrimEnd('/') + "/v1/api/request")
                    .SetHeader("Authorization", "Bearer " + token)
                    .SetHeader("X-Audible-Marketplace", region)
                    .SetHeader("Content-Type", "application/json")
                    .WithRateLimit(1);
                if (_configService.AudibleRsAccount.IsNotNullOrWhiteSpace())
                {
                    builder.SetHeader("X-Audible-Account", _configService.AudibleRsAccount);
                }

                var request = builder.Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(30);
                request.SetContent(new { path, method = "GET", query }.ToJson());
                request.ContentSummary = "Audible catalog GET";
                var response = _httpClient.Post(request);
                if (response.HasHttpError || response.HasHttpRedirect)
                {
                    throw new BookInfoException("audible-rs returned HTTP {0}. Check its connection, token, account, and lock state.", (int)response.StatusCode);
                }

                var envelope = JObject.Parse(response.Content);
                if (envelope.Value<int?>("status") != 200 || envelope["body"] is not JObject body)
                {
                    throw new BookInfoException("Audible catalog request failed with status {0}.", envelope.Value<int?>("status"));
                }

                return body;
            }
            catch (BookInfoException)
            {
                _unavailableUntil = DateTime.UtcNow.AddMinutes(1);
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new BookInfoException("Unable to read the audible-rs API token file. Check its server path and permissions.");
            }
            catch (Exception)
            {
                _unavailableUntil = DateTime.UtcNow.AddMinutes(1);

                // Do not include credential-bearing requests or response bodies in errors.
                throw new BookInfoException("Unable to read catalog data from audible-rs.");
            }
        }

        private static AudnexusBookResource MapProduct(JToken item, string region)
        {
            if (item is not JObject product)
            {
                throw new BookInfoException("audible-rs returned an invalid catalog product.");
            }

            var images = product["product_images"] as JObject;
            var series = (product["series"] as JArray)?.FirstOrDefault();
            return new AudnexusBookResource
            {
                Asin = product.Value<string>("asin"),
                Region = region,
                Title = product.Value<string>("title"),
                Subtitle = product.Value<string>("subtitle"),
                Description = product.Value<string>("publisher_summary"),
                Language = product.Value<string>("language"),
                PublisherName = product.Value<string>("publisher_name"),
                RuntimeLengthMin = product.Value<int?>("runtime_length_min"),
                ReleaseDate = product.Value<DateTime?>("release_date"),
                FormatType = product.Value<string>("format_type"),
                Authors = product["authors"]?.ToObject<List<AudnexusContributorResource>>(),
                Narrators = product["narrators"]?.ToObject<List<AudnexusContributorResource>>(),
                Genres = (product["category_ladders"] as JArray)?.OfType<JObject>()
                    .Where(ladder => ladder.Value<string>("root") == "Genres")
                    .SelectMany(ladder => (ladder["ladder"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    .Select(category => category.Value<string>("name"))
                    .Where(name => name.IsNotNullOrWhiteSpace()).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new AudnexusGenreResource { Name = name }).ToList(),
                Image = images?.Properties().OrderByDescending(p => int.TryParse(p.Name, out var size) ? size : 0).FirstOrDefault()?.Value.Value<string>(),
                Rating = product["rating"]?["overall_distribution"]?.Value<string>("display_average_rating"),
                SeriesPrimary = series == null ? null : new AudnexusSeriesResource
                {
                    Asin = series.Value<string>("asin"), Name = series.Value<string>("title"), Position = series.Value<string>("sequence")
                }
            };
        }
    }
}
