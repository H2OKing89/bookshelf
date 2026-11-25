using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Host.Diagnostics
{
    public class PerformanceProfilingMiddleware
    {
        private readonly RequestDelegate _next;

        public PerformanceProfilingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IPerformanceProfiler profiler)
        {
            // Only a genuine API principal after authorization. Exclude the
            // profiler itself, static pages, SignalR and anonymous/bypass routes.
            var action = profiler.Enabled ? context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() : null;
            if (action == null || action.ControllerName == "PerformanceProfiling" ||
                !context.User.HasClaim("ApiKey", "true") ||
                action.AttributeRouteInfo?.Template?.StartsWith("api/v", StringComparison.OrdinalIgnoreCase) != true)
            {
                await _next(context);
                return;
            }

            var method = context.Request.Method switch
            {
                "GET" => "GET",
                "POST" => "POST",
                "PUT" => "PUT",
                "PATCH" => "PATCH",
                "DELETE" => "DELETE",
                "HEAD" => "HEAD",
                "OPTIONS" => "OPTIONS",
                _ => "OTHER"
            };
            using var capture = profiler.Begin(action.ControllerName + "." + action.ActionName, method);
            var failed = false;
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                capture?.Complete(failed || context.Response.StatusCode >= 400, context.RequestAborted.IsCancellationRequested);
            }
        }
    }
}
