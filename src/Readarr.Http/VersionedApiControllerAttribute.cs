using System;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Readarr.Http
{
    public class VersionedApiControllerAttribute : Attribute, IRouteTemplateProvider, IEnableCorsAttribute, IApiBehaviorMetadata
    {
        public const string API_CORS_POLICY = "ApiCorsPolicy";
        public const string CONTROLLER_RESOURCE = "[controller]";

        public VersionedApiControllerAttribute(int version, string resource = CONTROLLER_RESOURCE)
        {
            Version = version;
            Resource = resource;
            Template = $"api/v{version}/{resource}";
            PolicyName = API_CORS_POLICY;
        }

        public int Version { get; }
        public string Resource { get; }
        public string Template { get; }
        public int? Order => 2;
        public string Name { get; set; }
        public string PolicyName { get; set; }
    }

    public class V1ApiControllerAttribute : VersionedApiControllerAttribute
    {
        public V1ApiControllerAttribute(string resource = "[controller]")
            : base(1, resource)
        {
        }
    }

    public class V2ApiControllerAttribute : VersionedApiControllerAttribute
    {
        public V2ApiControllerAttribute(string resource = CONTROLLER_RESOURCE)
            : base(2, resource)
        {
        }
    }
}
