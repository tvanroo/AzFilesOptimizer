using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Attributes
{
    // Marker attribute (currently not used by middleware, but kept for clarity on HTTP functions)
    [AttributeUsage(AttributeTargets.Method)]
    public class RequireAuthenticationAttribute : Attribute { }

    /// <summary>
    /// Functions worker middleware that enforces authentication on all HTTP-triggered functions.
    /// It extracts the bearer token, resolves the AuthenticatedUser, and attaches it to
    /// context.Items["AuthenticatedUser"]. If authentication fails, a 401 response is returned.
    /// </summary>
    public class AuthenticationMiddleware : IFunctionsWorkerMiddleware
    {
        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            // Only apply auth to HTTP-triggered invocations
            var httpRequest = await context.GetHttpRequestDataAsync();
            if (httpRequest == null)
            {
                await next(context);
                return;
            }

            var authService = context.InstanceServices.GetRequiredService<AuthenticationService>();
            var user = await authService.ExtractUserAsync(httpRequest);

            if (user == null)
            {
                var res = httpRequest.CreateResponse(HttpStatusCode.Unauthorized);
                await res.WriteStringAsync("Authentication failed.");
                context.GetInvocationResult().Value = res;
                return;
            }

            context.Items["AuthenticatedUser"] = user;
            await next(context);
        }
    }
}
