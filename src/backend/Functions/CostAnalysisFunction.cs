using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class CostAnalysisFunction
{
    private readonly ILogger _logger;
    private readonly CostCollectionService _costCollection;
    private readonly CostForecastingService _costForecasting;
    private readonly DiscoveredResourceStorageService _resourceStorage;
    private readonly JobStorageService _jobStorage;

    public CostAnalysisFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CostAnalysisFunction>();
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        var credential = new DefaultAzureCredential();
        
        _costCollection = new CostCollectionService(_logger, credential);
        _costForecasting = new CostForecastingService(_logger);
        _resourceStorage = new DiscoveredResourceStorageService(connectionString);
        _jobStorage = new JobStorageService(connectionString);
    }

    /// <summary>
    /// POST /api/discovery/{jobId}/cost-analysis
    /// Trigger cost analysis for all discovered volumes in a job
    /// </summary>
    [Function("TriggerCostAnalysis")]
    [QueueOutput("cost-analysis-queue", Connection = "AzureWebJobsStorage")]
    public async Task<string> TriggerCostAnalysis(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "discovery/{jobId}/cost-analysis")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Triggering cost analysis for job: {JobId}", jobId);

        try
        {
            var job = await _jobStorage.GetDiscoveryJobAsync(jobId);
            if (job == null)
            {
                _logger.LogError("Job not found: {JobId}", jobId);
                return string.Empty;
            }

            // Return job ID as queue message
            return JsonSerializer.Serialize(new CostAnalysisMessage { JobId = jobId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering cost analysis for job {JobId}", jobId);
            return string.Empty;
        }
    }

    /// <summary>
    /// GET /api/discovery/{jobId}/costs
    /// Retrieve cost breakdown for specific volumes
    /// </summary>
    [Function("GetCosts")]
    public async Task<HttpResponseData> GetCosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/costs")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting costs for job: {JobId}", jobId);

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var volumeId = query.Get("volumeId");
            var resourceType = query.Get("resourceType");

            var costs = await _resourceStorage.GetVolumeCostsByJobIdAsync(jobId);
            
            if (!string.IsNullOrEmpty(volumeId))
            {
                costs = costs.Where(c => c.VolumeId == volumeId).ToList();
            }

            if (!string.IsNullOrEmpty(resourceType))
            {
                costs = costs.Where(c => c.ResourceType == resourceType).ToList();
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                totalCount = costs.Count,
                costs = costs,
                summary = new
                {
                    totalCost = costs.Sum(c => c.TotalCostForPeriod),
                    averageDailyCost = costs.Average(c => c.TotalCostPerDay),
                    costByType = costs.GroupBy(c => c.ResourceType)
                        .ToDictionary(g => g.Key, g => g.Sum(c => c.TotalCostForPeriod))
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting costs for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve costs", details = ex.Message });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/discovery/{jobId}/cost-forecast
    /// Get 30-day forecast with assumptions
    /// </summary>
    [Function("GetCostForecast")]
    public async Task<HttpResponseData> GetCostForecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/cost-forecast")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting cost forecast for job: {JobId}", jobId);

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var volumeId = query.Get("volumeId");

            var forecasts = await _resourceStorage.GetCostForecastsByJobIdAsync(jobId);
            
            if (!string.IsNullOrEmpty(volumeId))
            {
                forecasts = forecasts.Where(f => f.ResourceId.Contains(volumeId, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                totalCount = forecasts.Count,
                forecasts = forecasts,
                summary = new
                {
                    totalForecastedCost = forecasts.Sum(f => f.ForecastedCostFor30Days),
                    averageConfidence = forecasts.Average(f => f.ConfidencePercentage),
                    trendBreakdown = forecasts.GroupBy(f => f.Trend)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    riskFactorCount = forecasts.Sum(f => f.RiskFactors.Count)
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cost forecast for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve forecast", details = ex.Message });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/discovery/{jobId}/costs/export
    /// Export cost data (JSON or CSV)
    /// </summary>
    [Function("ExportCosts")]
    public async Task<HttpResponseData> ExportCosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/costs/export")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Exporting costs for job: {JobId}", jobId);

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var format = query.Get("format") ?? "json";

            var costs = await _resourceStorage.GetVolumeCostsByJobIdAsync(jobId);

            if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                return await ExportAsCsv(req, costs);
            }
            else
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                response.Headers.Add("Content-Disposition", $"attachment; filename=costs-{jobId}.json");
                await response.WriteAsJsonAsync(costs);
                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting costs for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to export costs", details = ex.Message });
            return errorResponse;
        }
    }

    /// <summary>
    /// PUT /api/discovery/{jobId}/cost-assumptions
    /// Override cost assumptions (manual pricing adjustments)
    /// </summary>
    [Function("UpdateCostAssumptions")]
    public async Task<HttpResponseData> UpdateCostAssumptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "discovery/{jobId}/cost-assumptions")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Updating cost assumptions for job: {JobId}", jobId);

        try
        {
            var content = await req.ReadAsStringAsync();
            var body = JsonSerializer.Deserialize<CostAssumptionsRequest>(content);
            if (body == null || string.IsNullOrEmpty(body.VolumeId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Missing volumeId in request body" });
                return badResponse;
            }

            // Update assumptions in storage
            await _resourceStorage.UpdateCostAssumptionsAsync(jobId, body.VolumeId, body.Assumptions);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Cost assumptions updated" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cost assumptions for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to update assumptions", details = ex.Message });
            return errorResponse;
        }
    }

    /// <summary>
    /// Queue-triggered function to process cost analysis jobs
    /// </summary>
    [Function("ProcessCostAnalysis")]
    public async Task ProcessCostAnalysis(
        [QueueTrigger("cost-analysis-queue")] string message,
        FunctionContext context)
    {
        var logger = context.GetLogger("ProcessCostAnalysis");
        var parsedMessage = JsonSerializer.Deserialize<CostAnalysisMessage>(message);
        if (parsedMessage == null)
        {
            logger.LogWarning("Failed to parse cost analysis message");
            return;
        }
        logger.LogInformation("Processing cost analysis for job: {JobId}", parsedMessage.JobId);

        try
        {
            var job = await _jobStorage.GetDiscoveryJobAsync(parsedMessage.JobId);
            if (job == null)
            {
                logger.LogWarning("Job not found: {JobId}", parsedMessage.JobId);
                return;
            }

            // Get all discovered resources
            var shares = await _resourceStorage.GetSharesByJobIdAsync(parsedMessage.JobId);
            var volumes = await _resourceStorage.GetVolumesByJobIdAsync(parsedMessage.JobId);
            var disks = await _resourceStorage.GetDisksByJobIdAsync(parsedMessage.JobId);

            var costAnalyses = new List<VolumeCostAnalysis>();
            var forecasts = new List<CostForecastResult>();

            // Process Azure Files
            foreach (var share in shares)
            {
                try
                {
                    var cost = await _costCollection.GetAzureFilesCostAsync(
                        share.ResourceId,
                        share.ShareName,
                        share.Location,
                        share.ShareQuotaGiB?.Value * 1024 * 1024 * 1024 ?? 0,
                        share.ShareUsageBytes ?? 0,
                        100, // Estimate transactions per day
                        share.ShareUsageBytes ?? 0 / 10, // Estimate egress
                        share.SnapshotCount ?? 0,
                        share.TotalSnapshotSizeBytes ?? 0,
                        share.BackupPolicyConfigured ?? false,
                        DateTime.UtcNow.AddDays(-30),
                        DateTime.UtcNow);
                    
            cost.JobId = parsedMessage.JobId;
            costAnalyses.Add(cost);

            // Generate forecast
            var forecast = _costForecasting.ForecastCosts(cost, new List<CostMetrics>(), null);
                    forecasts.Add(forecast);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to calculate costs for share {Share}", share.ShareName);
                }
            }

            // Process ANF Volumes
            foreach (var volume in volumes)
            {
                try
                {
                    var cost = await _costCollection.GetAnfVolumeCostAsync(
                        volume.ResourceId,
                        volume.VolumeName,
                        volume.CapacityPoolName,
                        volume.Location,
                        volume.ProvisionedSizeBytes,
                        (long)(volume.ProvisionedSizeBytes * 0.7), // Estimate used as 70% of provisioned
                        volume.SnapshotCount ?? 0,
                        volume.TotalSnapshotSizeBytes ?? 0,
                        volume.BackupPolicyConfigured ?? false,
                        DateTime.UtcNow.AddDays(-30),
                        DateTime.UtcNow);
                    
            cost.JobId = parsedMessage.JobId;
            costAnalyses.Add(cost);

                    var forecast = _costForecasting.ForecastCosts(cost, new List<CostMetrics>());
                    forecasts.Add(forecast);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to calculate costs for volume {Volume}", volume.VolumeName);
                }
            }

            // Process Managed Disks
            foreach (var disk in disks)
            {
                try
                {
                    var cost = await _costCollection.GetManagedDiskCostAsync(
                        disk.ResourceId,
                        disk.DiskName,
                        disk.Location,
                        disk.DiskSizeBytes ?? 0,
                        0, // Snapshots
                        0,
                        DateTime.UtcNow.AddDays(-30),
                        DateTime.UtcNow);
                    
            cost.JobId = parsedMessage.JobId;
            costAnalyses.Add(cost);

            var forecast = _costForecasting.ForecastCosts(cost, new List<CostMetrics>(), null);
                    forecasts.Add(forecast);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to calculate costs for disk {Disk}", disk.DiskName);
                }
            }

            // Persist results
            await _resourceStorage.SaveVolumeCostAnalysesAsync(parsedMessage.JobId, costAnalyses);
            await _resourceStorage.SaveCostForecastsAsync(parsedMessage.JobId, forecasts);

            logger.LogInformation("Cost analysis completed for job {JobId}: {Count} volumes analyzed", 
                message.JobId, costAnalyses.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing cost analysis for job {JobId}", message.JobId);
        }
    }

    /// <summary>
    /// Export costs as CSV
    /// </summary>
    private async Task<HttpResponseData> ExportAsCsv(HttpRequestData req, List<VolumeCostAnalysis> costs)
    {
        var csv = new System.Text.StringBuilder();
        
        // Header
        csv.AppendLine("Volume Name,Resource Type,Region,Total Cost,Daily Cost,Used GB,Capacity GB," +
                      "Storage Cost,Transaction Cost,Egress Cost,Snapshot Cost,Backup Cost");

        // Data rows
        foreach (var cost in costs)
        {
            csv.AppendLine($"\"{cost.VolumeName}\",\"{cost.ResourceType}\",\"{cost.Region}\"," +
                          $"{cost.TotalCostForPeriod:F2},{cost.TotalCostPerDay:F2}," +
                          $"{cost.UsedGigabytes:F2},{cost.CapacityGigabytes:F2}," +
                          $"{cost.CostBreakdown.GetValueOrDefault("storage", 0):F2}," +
                          $"{cost.CostBreakdown.GetValueOrDefault("transactions", 0):F2}," +
                          $"{cost.CostBreakdown.GetValueOrDefault("egress", 0):F2}," +
                          $"{cost.CostBreakdown.GetValueOrDefault("snapshots", 0):F2}," +
                          $"{cost.CostBreakdown.GetValueOrDefault("backup", 0):F2}");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/csv");
        response.Headers.Add("Content-Disposition", $"attachment; filename=costs-export.csv");
        await response.WriteStringAsync(csv.ToString());
        return response;
    }

    /// <summary>
    /// Request model for cost analysis
    /// </summary>
    public class CostAnalysisMessage
    {
        public string JobId { get; set; } = "";
    }

    /// <summary>
    /// Request model for cost assumptions update
    /// </summary>
    public class CostAssumptionsRequest
    {
        public string VolumeId { get; set; } = "";
        public Dictionary<string, object> Assumptions { get; set; } = new();
    }
}