using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AzFilesOptimizer.Backend.Functions;

public class ResourceGroupsFunction
{
    private readonly ILogger<ResourceGroupsFunction> _logger;

    public ResourceGroupsFunction(ILogger<ResourceGroupsFunction> logger)
    {
        _logger = logger;
    }

    [Function("GetResourceGroups")]
    public async Task<HttpResponseData> GetResourceGroups(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/{subscriptionId}/resourcegroups")] HttpRequestData req,
        string subscriptionId)
    {
        _logger.LogInformation("Getting resource groups for subscription: {SubscriptionId}", subscriptionId);

        try
        {
            var credential = new DefaultAzureCredential();
            var armClient = new ArmClient(credential);
            
            var subscription = await armClient.GetSubscriptionResource(
                new ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();

            var resourceGroups = new List<object>();
            await foreach (var rg in subscription.Value.GetResourceGroups().GetAllAsync())
            {
                resourceGroups.Add(new
                {
                    name = rg.Data.Name,
                    location = rg.Data.Location.Name,
                    id = rg.Id.ToString()
                });
            }

            _logger.LogInformation("Found {Count} resource groups in subscription {SubscriptionId}", 
                resourceGroups.Count, subscriptionId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(resourceGroups);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting resource groups for subscription {SubscriptionId}", subscriptionId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get resource groups", details = ex.Message });
            return errorResponse;
        }
    }
}
