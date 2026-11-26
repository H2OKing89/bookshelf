using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Serializer;
using Readarr.Http;
using Readarr.Http.REST;
using RestSharp;

namespace NzbDrone.Integration.Test.Client
{
    public class ClientBase
    {
        protected readonly IRestClient _restClient;
        protected readonly string _resource;
        protected readonly string _apiKey;
        protected readonly Logger _logger;

        public ClientBase(IRestClient restClient, string apiKey, string resource)
        {
            _restClient = restClient;
            _resource = resource;
            _apiKey = apiKey;

            _logger = LogManager.GetLogger("REST");
        }

        public RestRequest BuildRequest(string command = "")
        {
            var request = new RestRequest(_resource + "/" + command.Trim('/'));

            request.AddHeader("Authorization", _apiKey);

            // RestSharp 114 combines default and request headers instead of overriding.
            // Send a single API key and refuse mismatched defaults in the test client.
            var defaultKeys = _restClient.DefaultParameters.Where(parameter =>
                parameter.Type == ParameterType.HttpHeader && string.Equals(parameter.Name, "X-Api-Key", StringComparison.OrdinalIgnoreCase)).ToList();
            if (defaultKeys.Count == 0)
            {
                request.AddHeader("X-Api-Key", _apiKey);
            }
            else if (defaultKeys.Count != 1 || !string.Equals(defaultKeys[0].Value?.ToString(), _apiKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The test client has a different default API key.");
            }

            return request;
        }

        public string Execute(RestRequest request, HttpStatusCode statusCode)
        {
            _logger.Info("{0}: {1}", request.Method, _restClient.BuildUri(request));

            var response = _restClient.Execute(request);
            _logger.Info("Response: {0}", response.Content);

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            AssertDisableCache(response);

            response.ErrorMessage.Should().BeNullOrWhiteSpace();

            response.StatusCode.Should().Be(statusCode, response.Content ?? string.Empty);

            return response.Content;
        }

        public T Execute<T>(RestRequest request, HttpStatusCode statusCode)
            where T : class, new()
        {
            var content = Execute(request, statusCode);

            return Json.Deserialize<T>(content);
        }

        private static void AssertDisableCache(RestResponse response)
        {
            // RestSharp 114 separates response and content headers; Expires is a content header.
            // Header names are case-insensitive, and cache-control directive order can vary.
            var headers = (response.Headers ?? Array.Empty<HeaderParameter>())
                .Concat(response.ContentHeaders ?? Array.Empty<HeaderParameter>())
                .ToList();
            var cache = headers.Where(x => string.Equals(x.Name, "Cache-Control", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Value.ToString().Split(','))
                .Select(x => x.Trim());
            cache.Should().BeEquivalentTo("no-store", "no-cache");
            headers.Single(c => string.Equals(c.Name, "Pragma", StringComparison.OrdinalIgnoreCase)).Value.Should().Be("no-cache");
            headers.Single(c => string.Equals(c.Name, "Expires", StringComparison.OrdinalIgnoreCase)).Value.Should().Be("-1");
        }
    }

    public class ClientBase<TResource> : ClientBase
        where TResource : RestResource, new()
    {
        public ClientBase(IRestClient restClient, string apiKey, string resource = null)
            : base(restClient, apiKey, resource ?? new TResource().ResourceName)
        {
        }

        public List<TResource> All()
        {
            var request = BuildRequest();
            return Get<List<TResource>>(request);
        }

        public PagingResource<TResource> GetPaged(int pageNumber, int pageSize, string sortKey, string sortDir, string filterKey = null, object filterValue = null)
        {
            var request = BuildRequest();
            request.AddQueryParameter("page", pageNumber);
            request.AddQueryParameter("pageSize", pageSize);
            request.AddQueryParameter("sortKey", sortKey);
            request.AddQueryParameter("sortDir", sortDir);

            if (filterKey != null && filterValue != null)
            {
                request.AddQueryParameter(filterKey, Convert.ToString(filterValue, CultureInfo.InvariantCulture));
            }

            return Get<PagingResource<TResource>>(request);
        }

        public TResource Post(TResource body, HttpStatusCode statusCode = HttpStatusCode.Created)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Post<TResource>(request, statusCode);
        }

        public TResource Put(TResource body, HttpStatusCode statusCode = HttpStatusCode.Accepted)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Put<TResource>(request, statusCode);
        }

        public TResource Get(int id, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var request = BuildRequest(id.ToString());
            return Get<TResource>(request, statusCode);
        }

        public TResource GetSingle(HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var request = BuildRequest();
            return Get<TResource>(request, statusCode);
        }

        public void Delete(int id)
        {
            var request = BuildRequest(id.ToString());
            Delete(request);
        }

        public object InvalidGet(int id, HttpStatusCode statusCode = HttpStatusCode.NotFound)
        {
            var request = BuildRequest(id.ToString());
            return Get<object>(request, statusCode);
        }

        public object InvalidPost(TResource body, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Post<object>(request, statusCode);
        }

        public object InvalidPut(TResource body, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Put<object>(request, statusCode);
        }

        public T Get<T>(RestRequest request, HttpStatusCode statusCode = HttpStatusCode.OK)
            where T : class, new()
        {
            request.Method = Method.Get;
            return Execute<T>(request, statusCode);
        }

        public T Post<T>(RestRequest request, HttpStatusCode statusCode = HttpStatusCode.Created)
            where T : class, new()
        {
            request.Method = Method.Post;
            return Execute<T>(request, statusCode);
        }

        public T Put<T>(RestRequest request, HttpStatusCode statusCode = HttpStatusCode.Accepted)
            where T : class, new()
        {
            request.Method = Method.Put;
            return Execute<T>(request, statusCode);
        }

        public void Delete(RestRequest request, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            request.Method = Method.Delete;
            Execute<object>(request, statusCode);
        }
    }
}
