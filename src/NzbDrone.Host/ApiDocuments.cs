using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Readarr.Http;
using Readarr.Http.ClientSchema;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NzbDrone.Host
{
    public static class ApiDocuments
    {
        private static readonly JsonSerializerOptions DocumentJsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static void AddBookshelfApiDocuments(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", Info("1.0.0", "Existing Bookshelf API and compatibility routes"));
                options.SwaggerDoc("v2", Info("2.0.0", "Bookshelf download-level queue API"));
                options.DocInclusionPredicate(Include);
                options.DescribeAllParametersInCamelCase();
                options.OperationFilter<ApiAuthorizationDocumentation>();

                var header = ApiKey("X-Api-Key", ParameterLocation.Header);
                var query = ApiKey("apikey", ParameterLocation.Query);
                options.AddSecurityDefinition(header.Name, header);
                options.AddSecurityDefinition(query.Name, query);
                var bearer = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "API key supplied as a Bearer token"
                };
                options.AddSecurityDefinition("BearerApiKey", bearer);
                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference(header.Name, document), new List<string>() } });
                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference(query.Name, document), new List<string>() } });
                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("BearerApiKey", document), new List<string>() } });
            });
        }

        public static bool Include(string documentName, ApiDescription description)
        {
            if (description.ActionDescriptor is not ControllerActionDescriptor action)
            {
                return false;
            }

            var versions = action.ControllerTypeInfo.GetCustomAttributes<VersionedApiControllerAttribute>(true).Select(attribute => attribute.Version)
                .Concat(action.ControllerTypeInfo.GetCustomAttributes<VersionedFeedControllerAttribute>(true).Select(attribute => attribute.Version)).ToList();

            // Keep the unversioned legacy discovery, login and other controller
            // routes in v1 only. A partial new API does not replace them.
            return versions.Count == 0 ? documentName == "v1" : versions.Any(version => documentName == $"v{version}");
        }

        public static void MapBookshelfApiDocuments(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/docs/{version}/openapi.json", (string version, HttpContext context, ISwaggerProvider provider) =>
            {
                if (version != "v1" && version != "v2")
                {
                    return Results.NotFound();
                }

                var document = provider.GetSwagger(version);

                // Use a relative origin path. This works at a reverse proxy or
                // base path without trusting a supplied Host header.
                document.Servers = new List<OpenApiServer> { new OpenApiServer { Url = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/" } };
                return Results.Text(SerializeOpenApi30(document), "application/json");
            }).RequireAuthorization(new AuthorizationPolicyBuilder("API").RequireAuthenticatedUser().Build()).ExcludeFromDescription();
        }

        private static string SerializeOpenApi30(OpenApiDocument document)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            document.SerializeAsV3(new OpenApiJsonWriter(writer));

            // Keep the existing OpenAPI 3.0 contract across the generator upgrade.
            // The newer generator synthesizes a top-level tag registry from the
            // existing operation tags; the published v1/v2 documents omit it.
            var json = JsonNode.Parse(writer.ToString()).AsObject();
            json.Remove("tags");

            // Field.Value is a free-form JSON value (including null, booleans,
            // numbers, strings, arrays and objects). Preserve its legacy nullable
            // marker when the generator emits an unconstrained schema. Giving it
            // a Null or Object type in the newer model would restrict valid values.
            if (json["components"]?["schemas"]?[nameof(Field)]?["properties"]?["value"] is JsonObject value && value.Count == 0)
            {
                value["nullable"] = true;
            }

            return json.ToJsonString(DocumentJsonOptions);
        }

        private static OpenApiInfo Info(string version, string description) => new OpenApiInfo
        {
            Version = version,
            Title = "Bookshelf",
            Description = description,
            License = new OpenApiLicense { Name = "GPL-3.0", Url = new Uri("https://www.gnu.org/licenses/gpl-3.0.html") }
        };

        private static OpenApiSecurityScheme ApiKey(string name, ParameterLocation location) => new OpenApiSecurityScheme
        {
            Name = name,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "apiKey",
            Description = "API key supplied as " + location.ToString().ToLowerInvariant(),
            In = location
        };
    }

    public class ApiAuthorizationDocumentation : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                operation.Security = new List<OpenApiSecurityRequirement> { new OpenApiSecurityRequirement() };
            }
            else if (metadata.OfType<IAuthorizeData>().Any(attribute => attribute.Policy == "UI"))
            {
                operation.Security = new List<OpenApiSecurityRequirement> { new OpenApiSecurityRequirement() };
                operation.Description = (operation.Description ?? string.Empty) + " Browser route; uses configured UI authentication (Forms cookie, Basic, External or None), not API-key authentication.";
            }
        }
    }
}
