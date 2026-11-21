using System;
using System.Net;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Http
{
    public interface ICachedHttpResponseService
    {
        HttpResponse Get(HttpRequest request, bool useCache, TimeSpan ttl);
        HttpResponse Get(HttpRequest request, bool useCache, TimeSpan ttl, CancellationToken cancellationToken);
        HttpResponse<T> Get<T>(HttpRequest request, bool useCache, TimeSpan ttl)
            where T : new();
        HttpResponse<T> Get<T>(HttpRequest request, bool useCache, TimeSpan ttl, CancellationToken cancellationToken)
            where T : new();
    }

    public class CachedHttpResponseService : ICachedHttpResponseService
    {
        private readonly ICachedHttpResponseRepository _repo;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public CachedHttpResponseService(ICachedHttpResponseRepository httpResponseRepository,
                                         IHttpClient httpClient,
                                         Logger logger)
        {
            _repo = httpResponseRepository;
            _httpClient = httpClient;
            _logger = logger;
        }

        public HttpResponse Get(HttpRequest request, bool useCache, TimeSpan ttl)
        {
            return Get(request, useCache, ttl, CommandExecutionContext.CancellationToken);
        }

        public HttpResponse Get(HttpRequest request, bool useCache, TimeSpan ttl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = _repo.FindByUrl(request.Url.ToString());
            cancellationToken.ThrowIfCancellationRequested();

            if (useCache && cached != null && cached.Expiry > DateTime.UtcNow)
            {
                _logger.Trace($"Returning cached response for [GET] {request.Url}");
                return new HttpResponse(request, new HttpHeader(), cached.Value, (HttpStatusCode)cached.StatusCode)
                {
                    CachedAtUtc = cached.LastRefresh
                };
            }

            var result = cancellationToken.CanBeCanceled
                ? _httpClient.Get(request, cancellationToken)
                : _httpClient.Get(request);
            cancellationToken.ThrowIfCancellationRequested();

            if (!result.HasHttpError)
            {
                if (cached == null)
                {
                    cached = new CachedHttpResponse
                    {
                        Url = request.Url.ToString(),
                    };
                }

                var now = DateTime.UtcNow;

                cached.LastRefresh = now;
                cached.Expiry = now.Add(ttl);
                cached.Value = result.Content;
                cached.StatusCode = (int)result.StatusCode;

                cancellationToken.ThrowIfCancellationRequested();
                _repo.Upsert(cached);
            }

            return result;
        }

        public HttpResponse<T> Get<T>(HttpRequest request, bool useCache, TimeSpan ttl)
            where T : new()
        {
            return Get<T>(request, useCache, ttl, CommandExecutionContext.CancellationToken);
        }

        public HttpResponse<T> Get<T>(HttpRequest request, bool useCache, TimeSpan ttl, CancellationToken cancellationToken)
            where T : new()
        {
            var response = Get(request, useCache, ttl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponse<T>(response);
        }
    }
}
