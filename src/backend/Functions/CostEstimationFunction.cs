using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using AzFilesOptimizer.Backend.Services;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Functions;

public class CostEstimationFunction
{
    private readonly AccurateCostEstimationService _estimationService;
    private readonly DiscoveredResourceStorageService _storageService;
    private readonly ILogger<CostEstimationFunction> _logger;

    public CostEstimationFunction(
        AccurateCostEstimationService estimationService,
        DiscoveredResourceStorageService storageService,
        ILogger<CostEstimationFunction> logger)
    {
        _estimationService = estimationService;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Calculate 30-day cost estimates for all resources in a discovery job
    /// GET /api/discovery/{jobId}/cost-estimates
    /// </summary>
    [Function("GetCostEstimates")]
    public async Task<HttpResponseData> GetCostEstimates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/cost-estimates")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting cost estimates for job {JobId}", jobId);

        try
        {
            // Get discovered resources from all three tables
            var shareTask = _storageService.GetSharesByJobIdAsync(jobId);
            var volumeTask = _storageService.GetVolumesByJobIdAsync(jobId);
            var diskTask = _storageService.GetDisksByJobIdAsync(jobId);

            await Task.WhenAll(shareTask, volumeTask, diskTask);

            var shares = await shareTask;
            var volumes = await volumeTask;
            var disks = await diskTask;

            // Convert to UnifiedResource
            var unifiedResources = new List<UnifiedResource>();
            unifiedResources.AddRange(shares.Select(s => UnifiedResource.FromFileShare(s)));
            unifiedResources.AddRange(volumes.Select(v => UnifiedResource.FromAnfVolume(v)));
            unifiedResources.AddRange(disks.Select(d => UnifiedResource.FromManagedDisk(d)));
            
            if (!unifiedResources.Any())
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new
                {
                    error = $"No resources found for job {jobId}"
                });
                return notFoundResponse;
            }

            // Calculate estimates
            var estimates = await _estimationService.CalculateBulkEstimatesAsync(unifiedResources);

            // Calculate summary
            var summary = new
            {
                TotalEstimatedCost = estimates.Sum(e => e.TotalEstimatedCost),
                AverageConfidence = estimates.Average(e => e.ConfidenceLevel),
                ResourceCount = estimates.Count,
                HighConfidenceCount = estimates.Count(e => e.ConfidenceLevel >= 80),
                MediumConfidenceCount = estimates.Count(e => e.ConfidenceLevel >= 50 && e.ConfidenceLevel < 80),
                LowConfidenceCount = estimates.Count(e => e.ConfidenceLevel < 50)
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                jobId,
                summary,
                estimates
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating cost estimates for job {JobId}", jobId);
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to calculate cost estimates",
                message = ex.Message
            });
            return errorResponse;
        }
    }

    /// <summary>
    /// Calculate 30-day cost estimate for a single resource
    /// POST /api/cost-estimates/calculate
    /// </summary>
    [Function("CalculateSingleEstimate")]
    public async Task<HttpResponseData> CalculateSingleEstimate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-estimates/calculate")] HttpRequestData req)
    {
        _logger.LogInformation("Calculating single resource cost estimate");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var resource = JsonSerializer.Deserialize<UnifiedResource>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (resource == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new
                {
                    error = "Invalid resource data"
                });
                return badRequest;
            }

            // Calculate estimate
            var estimate = await _estimationService.Calculate30DayCostEstimateAsync(resource);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(estimate);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating cost estimate");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to calculate cost estimate",
                message = ex.Message
            });
            return errorResponse;
        }
    }

    /// <summary>
    /// Get pricing information from Azure Retail Prices API
    /// GET /api/pricing/test?region={region}&service={service}
    /// </summary>
    [Function("TestPricing")]
    public async Task<HttpResponseData> TestPricing(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "pricing/test")] HttpRequestData req)
    {
        _logger.LogInformation("Testing Azure Retail Prices API");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var region = query["region"] ?? "eastus";
            var service = query["service"] ?? "files";

            var pricesClient = req.FunctionContext.InstanceServices.GetService(typeof(AzureRetailPricesClient)) as AzureRetailPricesClient;
            
            if (pricesClient == null)
            {
                throw new InvalidOperationException("AzureRetailPricesClient not available");
            }

            List<PriceItem> prices;

            switch (service.ToLowerInvariant())
            {
                case "files":
                    prices = await pricesClient.GetAzureFilesPricingAsync(region);
                    break;
                case "premium-files":
                    prices = await pricesClient.GetPremiumFilesPricingAsync(region);
                    break;
                case "anf":
                    prices = await pricesClient.GetAnfPricingAsync(region);
                    break;
                case "disk":
                    prices = await pricesClient.GetManagedDiskPricingAsync(region);
                    break;
                default:
                    prices = await pricesClient.GetAzureFilesPricingAsync(region);
                    break;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                region,
                service,
                priceCount = prices.Count,
                prices = prices.Take(10) // Return first 10 for testing
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing pricing API");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to test pricing API",
                message = ex.Message
            });
            return errorResponse;
        }
    }
}
