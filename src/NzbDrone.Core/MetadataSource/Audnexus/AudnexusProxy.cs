using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Audiobook;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.MetadataSource.Audnexus
{
    public interface IAudnexusProxy
    {
        AudnexusBookResource GetBook(string asin, string region);
        AudnexusChapterResource GetChapters(string asin, string region);
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
        private readonly IMetadataProviderStatusService _status;
        private MetadataProviderIssue _cooldown;

        public AudnexusProxy(ICachedHttpResponseService httpClient, IConfigService configService, IMetadataProviderStatusService status)
        {
            _httpClient = httpClient;
            _configService = configService;
            _status = status;
        }

        public AudnexusBookResource GetBook(string asin, string region)
        {
            if (!AudiobookId.IsAsin(asin))
            {
                return null;
            }

            return Read<AudnexusBookResource>($"books/{asin}",
                region,
                TimeSpan.FromDays(1),
                MetadataProviderOperation.RecordingDetails,
                matches: book => book.Asin == asin && book.Region == region);
        }

        public List<AudnexusBookResource> SearchBooks(string query, string region)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<AudnexusBookResource>();
            }

            return Read<List<AudnexusBookResource>>("books/search", region, TimeSpan.FromHours(1), MetadataProviderOperation.BookSearch, "q", query.Trim())
                ?? throw new BookInfoException("Audnexus book search returned no response.");
        }

        public AudnexusChapterResource GetChapters(string asin, string region)
        {
            if (!AudiobookId.IsAsin(asin))
            {
                return null;
            }

            return Read<AudnexusChapterResource>($"books/{asin}/chapters",
                region,
                TimeSpan.FromDays(1),
                MetadataProviderOperation.Chapters,
                matches: chapters => chapters.Asin == asin && chapters.Region == region);
        }

        public AudnexusAuthorResource GetAuthor(string asin, string region)
        {
            return Read<AudnexusAuthorResource>($"authors/{asin}",
                region,
                TimeSpan.FromDays(1),
                MetadataProviderOperation.AuthorProfile,
                matches: author => author.Asin == asin && author.Region == region);
        }

        public List<AudnexusContributorResource> SearchAuthors(string name, string region)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            {
                return new List<AudnexusContributorResource>();
            }

            return Read<List<AudnexusContributorResource>>("authors", region, TimeSpan.FromHours(1), MetadataProviderOperation.AuthorSearch, "name", name.Trim())
                ?? throw new BookInfoException("Audnexus author search returned no response.");
        }

        private T Read<T>(string route,
                         string region,
                         TimeSpan ttl,
                         MetadataProviderOperation operation,
                         string queryName = null,
                         string queryValue = null,
                         Func<T, bool> matches = null)
            where T : class, new()
        {
            return MetadataProviderObservation.Observe(_status,
                MetadataProvider.Audnexus,
                operation,
                observation => Read(route, region, ttl, queryName, queryValue, matches, observation));
        }

        private T Read<T>(string route,
                         string region,
                         TimeSpan ttl,
                         string queryName,
                         string queryValue,
                         Func<T, bool> matches,
                         MetadataProviderObservation observation)
            where T : class, new()
        {
            var cancellationToken = CommandExecutionContext.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (region == null || !Regions.Contains(region))
            {
                throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.Configuration),
                    "Choose a supported Audible marketplace in Settings → Metadata.");
            }

            var cooldown = _cooldown;
            if (cooldown?.RetryAfterUtc > DateTime.UtcNow)
            {
                throw new BookInfoException(cooldown, $"Audnexus is temporarily unavailable. Retry after {cooldown.RetryAfterUtc:u}.");
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
                var response = cancellationToken.CanBeCanceled
                    ? _httpClient.Get(request, true, ttl, cancellationToken)
                    : _httpClient.Get(request, true, ttl);
                cancellationToken.ThrowIfCancellationRequested();
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    observation.Flag(MetadataProviderState.Missing, MetadataProviderReason.ConfirmedMissing);
                    return null;
                }

                if (response.HasHttpError || response.HasHttpRedirect)
                {
                    _cooldown = MetadataProviderIssue.FromHttpStatus((int)response.StatusCode, MetadataProviderIssue.RetryAfter(response.Headers));
                    throw new BookInfoException(_cooldown, $"Audnexus returned HTTP {(int)response.StatusCode}. Retry after {_cooldown.RetryAfterUtc:u}.");
                }

                var result = Json.Deserialize<T>(response.Content)
                    ?? throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.InvalidResponse),
                        "Audnexus returned an empty metadata response.");
                if (matches != null && !matches(result))
                {
                    throw new BookInfoException(new MetadataProviderIssue(MetadataProviderState.IdentityReview, MetadataProviderReason.IdentityConflict),
                        "Audnexus returned metadata for a different identity or marketplace.");
                }

                if (response.CachedAtUtc.HasValue)
                {
                    observation.UseCached(response.CachedAtUtc.Value);
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (BookInfoException)
            {
                throw;
            }
            catch (JsonException)
            {
                _cooldown = new MetadataProviderIssue(MetadataProviderState.Unavailable, MetadataProviderReason.InvalidResponse, DateTime.UtcNow.AddMinutes(1));
                throw new BookInfoException(_cooldown, "Audnexus returned invalid metadata.");
            }
            catch (Exception ex)
            {
                _cooldown = MetadataProviderIssue.FromTransport(ex, DateTime.UtcNow.AddMinutes(1));
                throw new BookInfoException(_cooldown, "Unable to read audiobook metadata from Audnexus.");
            }
        }
    }
}
