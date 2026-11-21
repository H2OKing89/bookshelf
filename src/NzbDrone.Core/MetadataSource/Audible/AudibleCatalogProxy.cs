using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Audiobook;
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
        private const string ResponseGroups = "product_desc,contributors,series,product_attrs,product_extended_attrs,product_details,media,rating,category_ladders";
        private const string ImageSizes = "500,1024,2400";
        private static readonly Regex CopyrightYear = new Regex(@"(?:©|\(c\)|\bcopyright\b)\s*(?<year>\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly IMetadataProviderStatusService _status;
        private MetadataProviderIssue _cooldown;

        public AudibleCatalogProxy(IHttpClient httpClient, IDiskProvider diskProvider, IConfigService configService, IMetadataProviderStatusService status)
        {
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _configService = configService;
            _status = status;
        }

        public List<AudnexusBookResource> GetAuthorBooks(string name, string region)
        {
            return MetadataProviderObservation.Observe(_status, MetadataProvider.Audible, MetadataProviderOperation.AuthorCatalog, _ => ReadAuthorBooks(name, region));
        }

        private List<AudnexusBookResource> ReadAuthorBooks(string name, string region)
        {
            const int pageSize = 50;
            var products = new List<AudnexusBookResource>();
            var seen = new HashSet<string>();
            int? expectedTotal = null;
            for (var page = 0; page < 20; page++)
            {
                CommandExecutionContext.CancellationToken.ThrowIfCancellationRequested();
                var body = Read("/1.0/catalog/products", region, new Dictionary<string, object>
                {
                    { "author", name }, { "page", page }, { "num_results", pageSize },
                    { "products_sort_by", "ReleaseDate" }, { "response_groups", ResponseGroups },
                    { "image_sizes", ImageSizes }
                });
                if (body["products"] is not JArray items || body["total_results"]?.Type != JTokenType.Integer || items.Count > pageSize)
                {
                    throw PartialCatalog(MetadataProviderReason.IncompletePagination, "audible-rs returned an incomplete catalog page.");
                }

                var total = body.Value<long>("total_results");
                if (total > 1000)
                {
                    throw PartialCatalog(MetadataProviderReason.CatalogLimit, "This author catalog exceeds the 1,000-recording limit. Existing recordings have been retained.");
                }

                if (total < seen.Count + items.Count || (expectedTotal.HasValue && total != expectedTotal))
                {
                    throw PartialCatalog(MetadataProviderReason.IncompletePagination, "The audible-rs catalog count changed while reading pages. Existing recordings have been retained.");
                }

                expectedTotal = (int)total;
                foreach (var item in items)
                {
                    var product = ReadProduct(item, region);
                    if (!AudiobookId.IsAsin(product.Asin))
                    {
                        throw PartialCatalog(MetadataProviderReason.IncompletePagination, "audible-rs returned a catalog product without a valid ASIN. Existing recordings have been retained.");
                    }

                    if (!seen.Add(product.Asin))
                    {
                        throw PartialCatalog(MetadataProviderReason.RepeatedPage, "audible-rs repeated a catalog page. Existing recordings have been retained.");
                    }

                    if (AudiobookCatalogPolicy.IsRecording(product))
                    {
                        products.Add(product);
                    }
                }

                // The upstream total includes podcasts and other rejected products.
                if (seen.Count >= total)
                {
                    return products;
                }

                if (items.Count == 0)
                {
                    throw PartialCatalog(MetadataProviderReason.IncompletePagination, "audible-rs returned a truncated catalog. Existing recordings have been retained.");
                }
            }

            throw PartialCatalog(MetadataProviderReason.IncompletePagination, "Unable to finish reading this author catalog. Existing recordings have been retained.");
        }

        private static BookInfoException PartialCatalog(MetadataProviderReason reason, string message)
        {
            return new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Partial, reason), message);
        }

        public AudnexusBookResource GetBook(string asin, string region)
        {
            return MetadataProviderObservation.Observe(_status, MetadataProvider.Audible, MetadataProviderOperation.RecordingDetails, observation => ReadBook(asin, region, observation));
        }

        private AudnexusBookResource ReadBook(string asin, string region, MetadataProviderObservation observation)
        {
            var body = Read(
                "/1.0/catalog/products/" + asin,
                region,
                new Dictionary<string, object>
                {
                    { "response_groups", ResponseGroups }, { "image_sizes", ImageSizes }
                },
                allowMissing: true);
            if (body == null)
            {
                observation.Flag(MetadataProviderState.Missing, MetadataProviderReason.ConfirmedMissing);
                return null;
            }

            if (body["product"] is not JObject product || string.IsNullOrWhiteSpace(product.Value<string>("asin")))
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Partial, MetadataProviderReason.SparseMetadata),
                    "audible-rs returned an incomplete product.");
            }

            if (product.Value<string>("asin") != asin)
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.IdentityReview, MetadataProviderReason.IdentityConflict),
                    "audible-rs returned a different recording.");
            }

            var recording = ReadProduct(product, region);
            return AudiobookCatalogPolicy.IsRecording(recording) ? recording : null;
        }

        private JObject Read(string path, string region, Dictionary<string, object> query, bool allowMissing = false)
        {
            var cancellationToken = CommandExecutionContext.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (!_configService.AudibleRsEnabled)
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.Configuration),
                    "audible-rs is disabled. Check Settings → Metadata.");
            }

            var cooldown = _cooldown;
            if (cooldown?.RetryAfterUtc > DateTime.UtcNow)
            {
                throw new BookInfoException(cooldown, "audible-rs is temporarily unavailable. Check Settings → Metadata.");
            }

            try
            {
                var origin = new Uri(_configService.AudibleRsUrl.TrimEnd('/'));
                if (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback))
                {
                    throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.Configuration),
                        "audible-rs requires HTTPS, or HTTP on localhost.");
                }

                var token = _diskProvider.ReadAllText(_configService.AudibleRsTokenFile).Trim();
                if (token.IsNullOrWhiteSpace() || token.Contains('\r') || token.Contains('\n'))
                {
                    throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.Configuration),
                        "The audible-rs token file must contain one API token.");
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
                var response = cancellationToken.CanBeCanceled
                    ? _httpClient.Post(request, cancellationToken)
                    : _httpClient.Post(request);
                cancellationToken.ThrowIfCancellationRequested();
                if (response.HasHttpError || response.HasHttpRedirect)
                {
                    _cooldown = MetadataProviderIssue.FromHttpStatus((int)response.StatusCode, MetadataProviderIssue.RetryAfter(response.Headers));
                    throw new BookInfoException(_cooldown, $"audible-rs returned HTTP {(int)response.StatusCode}. Check its connection, token, account, and lock state.");
                }

                var envelope = JObject.Parse(response.Content);

                // The agent can successfully relay a catalog 404 for a recording
                // unavailable in this marketplace. This is not a provider outage.
                if (allowMissing && envelope.Value<int?>("status") == 404)
                {
                    return null;
                }

                if (envelope.Value<int?>("status") != 200 || envelope["body"] is not JObject body)
                {
                    var status = envelope["status"]?.Type == JTokenType.Integer ? envelope.Value<int?>("status") : null;
                    _cooldown = MetadataProviderIssue.FromHttpStatus(status, MetadataProviderIssue.RetryAfter(response.Headers));
                    throw new BookInfoException(_cooldown, "Audible catalog request could not be completed.");
                }

                return body;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (BookInfoException error)
            {
                var issue = error.ProviderIssue;
                if (issue?.RetryAfterUtc == null)
                {
                    _cooldown = new MetadataProviderIssue(issue?.State ?? MetadataProviderState.Unavailable,
                        issue?.Reason ?? MetadataProviderReason.InvalidResponse,
                        DateTime.UtcNow.AddMinutes(1));
                }

                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.Configuration),
                    "Unable to read the audible-rs API token file. Check its server path and permissions.");
            }
            catch (JsonException)
            {
                _cooldown = new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.InvalidResponse, DateTime.UtcNow.AddMinutes(1));
                throw new BookInfoException(_cooldown, "audible-rs returned an invalid response.");
            }
            catch (Exception error)
            {
                _cooldown = MetadataProviderIssue.FromTransport(error, DateTime.UtcNow.AddMinutes(1));

                // Do not include credential-bearing requests or response bodies in errors.
                throw new BookInfoException(_cooldown, "Unable to read catalog data from audible-rs.");
            }
        }

        private static AudnexusBookResource ReadProduct(JToken item, string region)
        {
            try
            {
                return MapProduct(item, region);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidCastException or OverflowException)
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.InvalidResponse),
                    "audible-rs returned an invalid catalog product.");
            }
        }

        private static AudnexusBookResource MapProduct(JToken item, string region)
        {
            if (item is not JObject product)
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.InvalidResponse),
                    "audible-rs returned an invalid catalog product.");
            }

            var images = product["product_images"] as JObject;
            var series = (product["series"] as JArray)?.OfType<JObject>()
                .Where(seriesEntry => seriesEntry.Value<string>("title").IsNotNullOrWhiteSpace())
                .Select(seriesEntry => new AudnexusSeriesResource
                {
                    Asin = seriesEntry.Value<string>("asin"),
                    Name = AudiobookTextNormalizer.Text(seriesEntry.Value<string>("title")),
                    Position = seriesEntry.Value<string>("sequence")
                })
                .DistinctBy(seriesEntry => seriesEntry.Asin.IsNotNullOrWhiteSpace() ? seriesEntry.Asin : seriesEntry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<AudnexusSeriesResource>();
            var publicationName = AudiobookTextNormalizer.Text(product.Value<string>("publication_name"));
            var primary = series.FirstOrDefault(seriesEntry => string.Equals(seriesEntry.Name, publicationName, StringComparison.OrdinalIgnoreCase)) ?? series.FirstOrDefault();
            var descriptionField = new[] { "extended_product_description", "publisher_summary", "merchandising_description", "merchandising_summary" }
                .FirstOrDefault(field => product.Value<string>(field).IsNotNullOrWhiteSpace());
            return new AudnexusBookResource
            {
                Asin = product.Value<string>("asin"),
                Region = region,
                Title = product.Value<string>("title"),
                Subtitle = product.Value<string>("subtitle"),
                Description = descriptionField == null ? null : product.Value<string>(descriptionField),
                DescriptionIsSummary = descriptionField == "merchandising_summary",
                Copyright = ParseCopyright(product["copyright"]),
                IsAdult = product["is_adult_product"]?.Type == JTokenType.Boolean ? product.Value<bool?>("is_adult_product") : null,
                LiteratureType = product["literature_type"]?.Type == JTokenType.String ? product.Value<string>("literature_type") : null,
                Language = product.Value<string>("language"),
                PublisherName = product.Value<string>("publisher_name"),
                Isbn = product.Value<string>("isbn"),
                RuntimeLengthMin = product.Value<int?>("runtime_length_min"),
                ReleaseDate = product.Value<DateTime?>("release_date"),
                FormatType = product.Value<string>("format_type"),
                ContentType = product.Value<string>("content_type"),
                ContentDeliveryType = product.Value<string>("content_delivery_type"),
                PublicationType = product.Value<string>("publication_type"),
                Authors = product["authors"]?.ToObject<List<AudnexusContributorResource>>(),
                Narrators = product["narrators"]?.ToObject<List<AudnexusContributorResource>>(),
                Genres = (product["category_ladders"] as JArray)?.OfType<JObject>()
                    .Where(ladder => string.Equals(ladder.Value<string>("root"), "Genres", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ladder => ((ladder["ladder"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        .Select((category, index) => new AudnexusGenreResource
                        {
                            Asin = category.Value<string>("id"),
                            Name = AudiobookTextNormalizer.Text(category.Value<string>("name")),
                            Type = index == 0 ? "genre" : "tag"
                        }))
                    .Where(category => category.Name.IsNotNullOrWhiteSpace())
                    .DistinctBy(category => (category.Asin.IsNotNullOrWhiteSpace() ? category.Asin : category.Name) + ":" + category.Type, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Image = AudiobookCoverSelector.Largest(images?.Properties().Select(property =>
                    new KeyValuePair<int, string>(int.TryParse(property.Name, out var size) ? size : 0,
                        property.Value.Type == JTokenType.String ? property.Value.Value<string>() : null))),
                Rating = product["rating"]?["overall_distribution"]?.Value<string>("display_average_rating"),
                SeriesPrimary = primary,
                SeriesSecondary = series.FirstOrDefault(seriesEntry => seriesEntry != primary)
            };
        }

        private static int? ParseCopyright(JToken value)
        {
            if (value?.Type != JTokenType.String && value?.Type != JTokenType.Integer)
            {
                return null;
            }

            var text = value.Value<string>();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                var match = CopyrightYear.Match(text);
                if (!match.Success || !int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year))
                {
                    return null;
                }
            }

            return year > 0 && year <= 9999 ? year : null;
        }
    }
}
