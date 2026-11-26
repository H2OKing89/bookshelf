using System.Text.Json;
using RestSharp;
using RestSharp.Serializers.Json;

namespace NzbDrone.Test.Common
{
    public static class TestRestClient
    {
        public static RestClient Create(string baseUrl)
        {
            // The API fixtures assert HTTP error statuses and bodies themselves.
            // Keep HTTP responses distinct from socket/transport failures.
            var options = new RestClientOptions(baseUrl)
            {
                CalculateResponseStatus = _ => ResponseStatus.Completed,
                SetErrorExceptionOnUnsuccessfulStatusCode = false
            };

            // The retired RestSharp.SystemTextJson plugin used plain JsonSerializerOptions,
            // not the current web defaults (which change property names and number handling).
            return new RestClient(options, configureSerialization: serializers => serializers.UseSystemTextJson(new JsonSerializerOptions()));
        }
    }
}
