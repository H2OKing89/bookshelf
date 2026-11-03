using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource.Audnexus
{
    public interface IAudnexusProxy
    {
        AudnexusBookResource GetBook(string asin, string region);
        List<AudnexusBookResource> SearchBooks(string query, string region);
        AudnexusAuthorResource GetAuthor(string asin, string region);
        List<AudnexusContributorResource> SearchAuthors(string name, string region);
    }

    public class AudnexusProxy : IAudnexusProxy
    {
        public static readonly HashSet<string> Regions = new HashSet<string>
        {
            "au", "ca", "de", "es", "fr", "in", "it", "jp", "us", "uk"
        };

        private readonly ICachedHttpResponseService _httpClient;
        private readonly IConfigService _configService;
        private DateTime _unavailableUntil;

        public AudnexusProxy(ICachedHttpResponseService httpClient, IConfigService configService)
        {
            _httpClient = httpClient;
            _configService = configService;
        }

        public AudnexusBookResource GetBook(string asin, string region)
        {
            if (!AudiobookId.IsAsin(asin))
            {
                return null;
            }

            var book = Read<AudnexusBookResource>($"books/{asin}", region, TimeSpan.FromDays(1));
            if (book != null && (book.Asin != asin || book.Region != region))
            {
                throw new BookInfoException("Audnexus returned metadata for a different ASIN or marketplace.");
            }

            return book;
        }

        public List<AudnexusBookResource> SearchBooks(string query, string region)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<AudnexusBookResource>();
            }

            return Read<List<AudnexusBookResource>>("books/search", region, TimeSpan.FromHours(1), "q", query.Trim())
                ?? throw new BookInfoException("Audnexus book search returned no response.");
        }

        public AudnexusAuthorResource GetAuthor(string asin, string region)
        {
            var author = Read<AudnexusAuthorResource>($"authors/{asin}", region, TimeSpan.FromDays(1));
            if (author != null && (author.Asin != asin || author.Region != region))
            {
                throw new BookInfoException("Audnexus returned a different author or marketplace.");
            }

            return author;
        }

        public List<AudnexusContributorResource> SearchAuthors(string name, string region)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            {
                return new List<AudnexusContributorResource>();
            }

            return Read<List<AudnexusContributorResource>>("authors", region, TimeSpan.FromHours(1), "name", name.Trim())
                ?? throw new BookInfoException("Audnexus author search returned no response.");
        }

        private T Read<T>(string route, string region, TimeSpan ttl, string queryName = null, string queryValue = null)
            where T : class, new()
        {
            if (region == null || !Regions.Contains(region))
            {
                throw new BookInfoException("Choose a supported Audible marketplace in Settings → Metadata.");
            }

            if (DateTime.UtcNow < _unavailableUntil)
            {
                throw new BookInfoException("Audnexus is temporarily unavailable. Retry after {0:u}.", _unavailableUntil);
            }

            try
            {
                var builder = new HttpRequestBuilder(_configService.AudnexusUrl.TrimEnd('/') + "/" + route)
                    .AddQueryParam("region", region).WithRateLimit(1);
                if (queryName != null)
                {
                    builder.AddQueryParam(queryName, queryValue);
                }

                var request = builder.Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);
                var response = _httpClient.Get(request, true, ttl);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.HasHttpError || response.HasHttpRedirect)
                {
                    _unavailableUntil = DateTime.UtcNow.AddMinutes(1);
                    var retryAfter = response.Headers.GetSingleValue("Retry-After");
                    if (int.TryParse(retryAfter, out var seconds) && seconds > 0)
                    {
                        _unavailableUntil = DateTime.UtcNow.AddSeconds(seconds);
                    }
                    else if (DateTime.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var retryDate) && retryDate > _unavailableUntil)
                    {
                        _unavailableUntil = retryDate;
                    }

                    throw new BookInfoException("Audnexus returned HTTP {0}. Retry after {1:u}.", (int)response.StatusCode, _unavailableUntil);
                }

                return Json.Deserialize<T>(response.Content)
                    ?? throw new BookInfoException("Audnexus returned an empty metadata response.");
            }
            catch (BookInfoException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _unavailableUntil = DateTime.UtcNow.AddMinutes(1);
                throw new BookInfoException("Unable to read audiobook metadata from Audnexus.", ex);
            }
        }
    }
}
