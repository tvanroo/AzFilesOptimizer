using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class HypotheticalCostFunction
{
    private readonly HypotheticalAnfCostCalculator _calculator;
    private readonly VolumeAnnotationService _annotationService;
    private readonly ILogger<HypotheticalCostFunction> _logger;

    public HypotheticalCostFunction(
        HypotheticalAnfCostCalculator calculator,
        VolumeAnnotationService annotationService,
        ILogger<HypotheticalCostFunction> logger)
    {
        _calculator = calculator;
        _annotationService = annotationService;
        _logger = logger;
    }

    /// <summary>
    /// Calculate hypothetical ANF Flexible cost for a single volume
    /// POST /api/hypothetical-cost/anf-flexible
    /// </summary>
    [Function("CalculateHypotheticalAnfFlexibleCost")]
    public async Task<HttpResponseData> CalculateSingle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hypothetical-cost/anf-flexible")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Request body is required");
                return badRequest;
            }

            var request = JsonSerializer.Deserialize<HypotheticalCostRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request format");
                return badRequest;
            }

            // Validate required fields
            if (request.RequiredCapacityGiB <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("RequiredCapacityGiB must be greater than 0");
                return badRequest;
            }

            if (request.RequiredThroughputMiBps < 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("RequiredThroughputMiBps cannot be negative");
                return badRequest;
            }

            if (string.IsNullOrEmpty(request.Region))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Region is required");
                return badRequest;
            }

            // Build assumptions if provided
            CoolDataAssumptions? assumptions = null;
            if (request.CoolAccessEnabled && 
                (request.CoolDataPercentage.HasValue || request.CoolDataRetrievalPercentage.HasValue))
            {
                assumptions = new CoolDataAssumptions
                {
                    CoolDataPercentage = request.CoolDataPercentage ?? 80.0,
                    CoolDataRetrievalPercentage = request.CoolDataRetrievalPercentage ?? 15.0,
                    Source = AssumptionSource.Global // Override source
                };
            }

            // Calculate cost
            var result = await _calculator.CalculateFlexibleTierCostAsync(
                request.RequiredCapacityGiB,
                request.RequiredThroughputMiBps,
                request.Region,
                request.CoolAccessEnabled,
                assumptions,
                request.VolumeId,
                request.JobId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating hypothetical ANF Flexible cost");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    /// <summary>
    /// Calculate hypothetical ANF Flexible costs for multiple volumes in batch
    /// POST /api/hypothetical-cost/batch
    /// </summary>
    [Function("CalculateHypotheticalAnfFlexibleCostBatch")]
    public async Task<HttpResponseData> CalculateBatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hypothetical-cost/batch")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Request body is required");
                return badRequest;
            }

            var request = JsonSerializer.Deserialize<HypotheticalCostBatchRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || request.VolumeIds == null || request.VolumeIds.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("VolumeIds array is required");
                return badRequest;
            }

            if (string.IsNullOrEmpty(request.JobId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("JobId is required for batch calculation");
                return badRequest;
            }

            _logger.LogInformation("Processing batch hypothetical cost calculation for JobId={JobId}, VolumeCount={Count}, CoolAccess={Cool}", 
                request.JobId, request.VolumeIds.Length, request.CoolAccessEnabled);
            
            // Filter out empty volume IDs
            var validVolumeIds = request.VolumeIds.Where(id => !string.IsNullOrEmpty(id)).ToArray();
            if (validVolumeIds.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("No valid VolumeIds provided");
                return badRequest;
            }

            // Load discovery data for the job
            var discoveryData = await _annotationService.GetDiscoveryDataAsync(request.JobId);
            if (discoveryData == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync($"Discovery job {request.JobId} not found");
                return notFound;
            }

            // Build assumptions if provided
            CoolDataAssumptions? assumptions = null;
            if (request.CoolAccessEnabled &&
                (request.CoolDataPercentage.HasValue || request.CoolDataRetrievalPercentage.HasValue))
            {
                assumptions = new CoolDataAssumptions
                {
                    CoolDataPercentage = request.CoolDataPercentage ?? 80.0,
                    CoolDataRetrievalPercentage = request.CoolDataRetrievalPercentage ?? 15.0,
                    Source = AssumptionSource.Global // Override source
                };
            }

            // Calculate costs for each volume in parallel
            var results = new Dictionary<string, HypotheticalCostResult>();
            var tasks = new List<Task>();

            foreach (var volumeId in validVolumeIds)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var volume = discoveryData.Volumes.FirstOrDefault(v => v.VolumeId == volumeId);
                        if (volume == null)
                        {
                            _logger.LogWarning("Volume {VolumeId} not found in job {JobId}", volumeId, request.JobId);
                            return;
                        }

                        // Extract required values from volume
                        double requiredCapacity = volume.AiAnalysis?.CapacitySizing?.RecommendedCapacityGiB ?? GetDefaultCapacity(volume);
                        double requiredThroughput = volume.AiAnalysis?.CapacitySizing?.RecommendedThroughputMiBps ?? GetDefaultThroughput(volume);
                        string region = GetRegion(volume);

                        if (string.IsNullOrEmpty(region))
                        {
                            _logger.LogWarning("Could not determine region for volume {VolumeId}", volumeId);
                            return;
                        }

                        var result = await _calculator.CalculateFlexibleTierCostAsync(
                            requiredCapacity,
                            requiredThroughput,
                            region,
                            request.CoolAccessEnabled,
                            assumptions,
                            volumeId,
                            request.JobId);

                        lock (results)
                        {
                            results[volumeId] = result;
                        }
                    }
                    catch (Exception taskEx)
                    {
                        _logger.LogError(taskEx, "Error processing volume {VolumeId} in batch", volumeId);
                    }
                }));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception taskException)
            {
                _logger.LogError(taskException, "Error in batch task execution");
                // Don't fail entirely - return partial results
            }

            _logger.LogInformation("Batch calculation completed with {ResultCount} results out of {TotalVolumes} volumes", results.Count, validVolumeIds.Length);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(results, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating batch hypothetical ANF Flexible costs");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    /// <summary>
    /// Extract default capacity from volume data
    /// </summary>
    private double GetDefaultCapacity(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.ShareQuotaGiB ?? 100.0,
            "ANF" => ((volume.VolumeData as DiscoveredAnfVolume)?.ProvisionedSizeBytes ?? 100L * 1024 * 1024 * 1024) / (1024.0 * 1024.0 * 1024.0),
            "ManagedDisk" => (volume.VolumeData as DiscoveredManagedDisk)?.DiskSizeGB ?? 100.0,
            _ => 100.0
        };
    }

    /// <summary>
    /// Extract default throughput from volume data
    /// </summary>
    private double GetDefaultThroughput(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.EstimatedThroughputMiBps ?? 60.0,
            "ANF" => (volume.VolumeData as DiscoveredAnfVolume)?.ActualThroughputMibps ?? 64.0,
            "ManagedDisk" => 60.0, // Default throughput assumption for disks
            _ => 60.0
        };
    }

    /// <summary>
    /// Extract region from volume data
    /// </summary>
    private string GetRegion(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.Location ?? string.Empty,
            "ANF" => (volume.VolumeData as DiscoveredAnfVolume)?.Location ?? string.Empty,
            "ManagedDisk" => (volume.VolumeData as DiscoveredManagedDisk)?.Location ?? string.Empty,
            _ => string.Empty
        };
    }
}

/// <summary>
/// Request model for single hypothetical cost calculation
/// </summary>
public class HypotheticalCostRequest
{
    public double RequiredCapacityGiB { get; set; }
    public double RequiredThroughputMiBps { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool CoolAccessEnabled { get; set; }
    public double? CoolDataPercentage { get; set; }
    public double? CoolDataRetrievalPercentage { get; set; }
    public string? VolumeId { get; set; }
    public string? JobId { get; set; }
}

/// <summary>
/// Request model for batch hypothetical cost calculation
/// </summary>
public class HypotheticalCostBatchRequest
{
    public string[] VolumeIds { get; set; } = Array.Empty<string>();
    public string JobId { get; set; } = string.Empty;
    public bool CoolAccessEnabled { get; set; }
    public double? CoolDataPercentage { get; set; }
    public double? CoolDataRetrievalPercentage { get; set; }
}
