using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Azure.Data.Tables;
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
    private readonly JobLogService _jobLogService;
    private readonly CostHistoryService _costHistory;

    public CostAnalysisFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CostAnalysisFunction>();
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        var credential = new DefaultAzureCredential();
        
        var tableServiceClient = new TableServiceClient(connectionString);
        var httpClient = new HttpClient();
        var pricingService = new RetailPricingService(_logger, tableServiceClient, httpClient);
        var metricsService = new MetricsCollectionService(_logger, credential);
        var normalizationService = new MetricsNormalizationService(_logger, credential);
        _jobLogService = new JobLogService(connectionString);
        _costCollection = new CostCollectionService(_logger, credential, pricingService, metricsService, normalizationService, _jobLogService);
        _costForecasting = new CostForecastingService(_logger);
        _resourceStorage = new DiscoveredResourceStorageService(connectionString);
        _jobStorage = new JobStorageService(connectionString);
        _costHistory = new CostHistoryService(connectionString, _logger);
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
                    averageDailyCost = costs.Count > 0 ? costs.Average(c => c.TotalCostPerDay) : 0,
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
                    averageConfidence = forecasts.Count > 0 ? forecasts.Average(f => f.ConfidencePercentage) : 0,
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
    /// GET /api/discovery/{jobId}/costs/history
    /// Get cost history for a specific volume
    /// </summary>
    [Function("GetCostHistory")]
    public async Task<HttpResponseData> GetCostHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/costs/history")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting cost history for job: {JobId}", jobId);

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var volumeId = query.Get("volumeId");
            var monthsBackStr = query.Get("monthsBack") ?? "3";
            
            if (string.IsNullOrEmpty(volumeId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "volumeId query parameter is required" });
                return badResponse;
            }

            int monthsBack = int.TryParse(monthsBackStr, out var mb) ? mb : 3;
            var history = await _costHistory.GetCostHistoryAsync(jobId, volumeId, monthsBack);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                volumeId = volumeId,
                monthsBack = monthsBack,
                snapshotCount = history.Count,
                snapshots = history,
                summary = history.Count > 0 ? new
                {
                    latestCost = history.First().TotalCost,
                    oldestCost = history.Last().TotalCost,
                    averageCost = history.Average(h => h.TotalCost),
                    trend = history.Count > 1 && history.First().TotalCost > history.Last().TotalCost ? "Increasing" : "Decreasing"
                } : null
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cost history for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve cost history", details = ex.Message });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/discovery/{jobId}/costs/trends
    /// Get cost trends across all volumes in a job
    /// </summary>
    [Function("GetCostTrends")]
    public async Task<HttpResponseData> GetCostTrends(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/costs/trends")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting cost trends for job: {JobId}", jobId);

        try
        {
            var trends = await _costHistory.GetCostTrendsAsync(jobId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(trends);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cost trends for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve cost trends", details = ex.Message });
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
        await RunCostAnalysisForJobAsync(parsedMessage.JobId);
    }

    /// <summary>
    /// Shared helper that performs the full cost analysis pipeline for a given discovery job.
    /// Used both by the queue trigger and inline from the discovery job execution.
    /// </summary>
    public async Task RunCostAnalysisForJobAsync(string jobId)
    {
        try
        {
            var job = await _jobStorage.GetDiscoveryJobAsync(jobId);
            if (job == null)
            {
                _logger.LogWarning("Job not found when running cost analysis: {JobId}", jobId);
                return;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await _jobLogService.AddLogAsync(job.JobId, $"[{timestamp}] Starting cost analysis phase for discovery job {job.JobId}");

            // Get all discovered resources
            await _jobLogService.AddLogAsync(job.JobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Loading discovered resources for cost analysis...");
            var shares = await _resourceStorage.GetSharesByJobIdAsync(job.JobId);
            var volumes = await _resourceStorage.GetVolumesByJobIdAsync(job.JobId);
            var disks = await _resourceStorage.GetDisksByJobIdAsync(job.JobId);

            await _jobLogService.AddLogAsync(job.JobId,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Cost analysis scope: {shares.Count} Azure Files shares, {volumes.Count} ANF volumes, {disks.Count} managed disks");

            var costAnalyses = new List<VolumeCostAnalysis>();
            var forecasts = new List<CostForecastResult>();

            // Cost Management API has 1-2 day lag, so query up to 2 days ago
            var costPeriodEnd = DateTime.UtcNow.AddDays(-2);
            var costPeriodStart = costPeriodEnd.AddDays(-30);

            await _jobLogService.AddLogAsync(job.JobId,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Cost analysis period: {costPeriodStart:yyyy-MM-dd} to {costPeriodEnd:yyyy-MM-dd} (accounting for Cost Management API lag)");

            // Process Azure Files
            if (shares.Count > 0)
            {
                await _jobLogService.AddLogAsync(job.JobId,
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Calculating costs for Azure Files shares...");
            }

            foreach (var share in shares)
            {
                try
                {
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   → Processing share '{share.ShareName}' | ResourceId: {share.ResourceId}");
                    
                    var cost = await _costCollection.GetAzureFilesCostAsync(
                        share,
                        costPeriodStart,
                        costPeriodEnd);
                    
                    cost.JobId = job.JobId;
                    
                    // Azure Files costs are calculated from retail pricing, not Cost Management API
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   ✓ Calculated from retail pricing: ${cost.TotalCostForPeriod:F2}");
                    
                    costAnalyses.Add(cost);

                    // Generate forecast
                    var forecast = _costForecasting.ForecastCosts(cost, new List<CostMetrics>(), null);
                    forecasts.Add(forecast);
                    
                    // Save cost history snapshot
                    try
                    {
                        await _costHistory.SaveCostSnapshotAsync(job.JobId, share.ResourceId, cost);
                    }
                    catch (Exception histEx)
                    {
                        _logger.LogWarning(histEx, "Failed to save cost history for share {Share}", share.ShareName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate costs for share {Share}", share.ShareName);
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] WARNING: Failed to calculate costs for Azure Files share {share.ShareName}: {ex.Message}");
                }
            }

            // Process ANF Volumes
            if (volumes.Count > 0)
            {
                await _jobLogService.AddLogAsync(job.JobId,
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Calculating costs for Azure NetApp Files volumes...");
            }

            foreach (var volume in volumes)
            {
                try
                {
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   → Processing ANF volume '{volume.VolumeName}' | ResourceId: {volume.ResourceId}");
                    
                    var cost = await _costCollection.GetAnfVolumeCostAsync(
                        volume,
                        costPeriodStart,
                        costPeriodEnd,
                        job.JobId);
                    
                    cost.JobId = job.JobId;
                    
                    // ANF costs are calculated from retail pricing, not Cost Management API
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   ✓ Calculated from retail pricing: ${cost.TotalCostForPeriod:F2}");
                    
                    costAnalyses.Add(cost);

                    var forecast = _costForecasting.ForecastCosts(cost, new List<CostMetrics>());
                    forecasts.Add(forecast);
                    
                    // Save cost history snapshot
                    try
                    {
                        await _costHistory.SaveCostSnapshotAsync(job.JobId, volume.ResourceId, cost);
                    }
                    catch (Exception histEx)
                    {
                        _logger.LogWarning(histEx, "Failed to save cost history for ANF volume {Volume}", volume.VolumeName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate costs for volume {Volume}", volume.VolumeName);
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] WARNING: Failed to calculate costs for ANF volume {volume.VolumeName}: {ex.Message}");
                }
            }

            // Process Managed Disks
            if (disks.Count > 0)
            {
                await _jobLogService.AddLogAsync(job.JobId,
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Calculating costs for managed disks...");
            }

            foreach (var disk in disks)
            {
                try
                {
                    var cost = await _costCollection.GetManagedDiskCostAsync(
                        disk,
                        costPeriodStart,
                        costPeriodEnd);
                    
                    cost.JobId = job.JobId;
                    
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   → Processing disk '{disk.DiskName}' | ResourceId: {disk.ResourceId}");
                    
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   → Querying Cost Management API for actual costs...");
                    
                    // Managed disks: enrich with detailed actual costs from Cost Management API
                    await _costCollection.EnrichWithDetailedActualCostsAsync(cost, costPeriodStart, costPeriodEnd);
                    
                    // Log the result
                    if (cost.ActualCostsApplied)
                    {
                        await _jobLogService.AddLogAsync(job.JobId,
                            $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   ✓ Actual costs applied: ${cost.TotalCostForPeriod:F2} ({cost.ActualCostMeterCount} meters)");
                    }
                    else
                    {
                        await _jobLogService.AddLogAsync(job.JobId,
                            $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]   ⚠ Using retail estimates: ${cost.TotalCostForPeriod:F2} | Reason: {cost.ActualCostsNotAppliedReason}");
                    }
                    
                    costAnalyses.Add(cost);

                    var forecast = _costForecasting.ForecastCosts(cost, new List<CostMetrics>(), null);
                    forecasts.Add(forecast);
                    
                    // Save cost history snapshot
                    try
                    {
                        await _costHistory.SaveCostSnapshotAsync(job.JobId, disk.ResourceId, cost);
                    }
                    catch (Exception histEx)
                    {
                        _logger.LogWarning(histEx, "Failed to save cost history for managed disk {Disk}", disk.DiskName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate costs for disk {Disk}", disk.DiskName);
                    await _jobLogService.AddLogAsync(job.JobId,
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] WARNING: Failed to calculate costs for managed disk {disk.DiskName}: {ex.Message}");
                }
            }

            // Persist results
            await _jobLogService.AddLogAsync(job.JobId,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Persisting cost analysis results ({costAnalyses.Count} resources)...");
            await _resourceStorage.SaveVolumeCostAnalysesAsync(job.JobId, costAnalyses);
            await _resourceStorage.SaveCostForecastsAsync(job.JobId, forecasts);

            _logger.LogInformation("Cost analysis completed for job {JobId}: {Count} resources analyzed", 
                job.JobId, costAnalyses.Count);
            await _jobLogService.AddLogAsync(job.JobId,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Cost analysis phase completed. Analyzed {costAnalyses.Count} resources.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running cost analysis for job {JobId}", jobId);
            try
            {
                await _jobLogService.AddLogAsync(jobId,
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: Cost analysis phase failed: {ex.Message}");
            }
            catch
            {
                // Swallow logging failures to avoid poison queue loops.
            }
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