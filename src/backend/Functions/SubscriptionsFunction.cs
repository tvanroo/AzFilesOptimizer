using System.Net;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Functions;

public class SubscriptionsFunction
{
    private readonly ILogger _logger;

    public SubscriptionsFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SubscriptionsFunction>();
    }

    [Function("GetSubscriptions")]
    public async Task<HttpResponseData> GetSubscriptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions")] HttpRequestData req)
    {
        _logger.LogInformation("Listing subscriptions for authenticated user");

        try
        {
            // Use DefaultAzureCredential to authenticate as the signed-in user or managed identity
            var credential = new DefaultAzureCredential();
            var armClient = new ArmClient(credential);
            
            var subscriptions = new List<object>();
            
            // List all accessible subscriptions
            await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync())
            {
                subscriptions.Add(new
                {
                    id = subscription.Id.SubscriptionId,
                    name = subscription.Data.DisplayName,
                    state = subscription.Data.State?.ToString() ?? "Unknown",
                    tenantId = subscription.Data.TenantId?.ToString() ?? ""
                });
            }
            
            _logger.LogInformation("Found {Count} subscriptions", subscriptions.Count);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(subscriptions);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve subscriptions", details = ex.Message });
            return errorResponse;
        }
    }
}
