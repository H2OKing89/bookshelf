using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Readarr.Api.V1.Config
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HostAccountAuthenticationAttribute : Attribute, IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            // Authorization still requires the existing API scheme. This optional
            // secondary identity selects credentials; it never grants API access.
            var http = context.HttpContext;
            var method = http.RequestServices.GetRequiredService<IConfigFileProvider>().AuthenticationMethod;
            if (http.User.Identity?.IsAuthenticated == true &&
                (method == AuthenticationType.Forms || method == AuthenticationType.Basic))
            {
                var result = await http.AuthenticateAsync(method.ToString());
                if (result.Succeeded && result.Principal != null)
                {
                    http.User = new ClaimsPrincipal(http.User.Identities.Concat(result.Principal.Identities));
                }
            }

            await next();
        }
    }
}
