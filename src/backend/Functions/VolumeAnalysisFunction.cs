using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class VolumeAnalysisFunction
{
    private readonly ILogger _logger;
    private readonly TableClient _analysisJobsTable;
    private readonly QueueClient _analysisQueue;
    private readonly VolumeAnnotationService _annotationService;
    private readonly DiscoveryMigrationService _migrationService;
    private readonly AnalysisLogService _analysisLogService;
    private readonly AnalysisJobRunner _analysisJobRunner;
    private readonly DiscoveredResourceStorageService _resourceStorage;
    private readonly bool _useAnalysisQueue;

    public VolumeAnalysisFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<VolumeAnalysisFunction>();
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        
        var tableServiceClient = new TableServiceClient(connectionString);
        _analysisJobsTable = tableServiceClient.GetTableClient("AnalysisJobs");
        _analysisJobsTable.CreateIfNotExists();
        
        var queueServiceClient = new QueueServiceClient(connectionString);
        _analysisQueue = queueServiceClient.GetQueueClient("analysis-queue");
        _analysisQueue.CreateIfNotExists();
        
        _annotationService = new VolumeAnnotationService(connectionString, _logger);
        _migrationService = new DiscoveryMigrationService(connectionString, _logger);
        _analysisLogService = new AnalysisLogService(connectionString, _logger);
        _analysisJobRunner = new AnalysisJobRunner(connectionString, _logger);
        _resourceStorage = new DiscoveredResourceStorageService(connectionString);

        _useAnalysisQueue = string.Equals(
            Environment.GetEnvironmentVariable("AZFO_ENABLE_ANALYSIS_QUEUE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!_useAnalysisQueue)
        {
            _logger.LogWarning("AZFO_ENABLE_ANALYSIS_QUEUE is not enabled. Analysis jobs will run via background tasks started from StartAnalysis instead of using the queue. For production, enable the queue pipeline.");
        }
    }

    [Function("StartAnalysis")]
    public async Task<HttpResponseData> StartAnalysis(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "discovery/{jobId}/analyze")] HttpRequestData req,
        string jobId)
    {
        // Generate analysisJobId up-front so we can log against it even if something fails later
        var analysisJobId = Guid.NewGuid().ToString();

        try
        {
            await _analysisLogService.ClearLogsAsync(analysisJobId);
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[HTTP] StartAnalysis called for discovery job {jobId}");

            // Ensure volumes are migrated from Tables to Blob storage
            await _analysisLogService.LogProgressAsync(analysisJobId, "[HTTP] Migrating discovery volumes to Blob storage before queuing analysis...");
            await _migrationService.MigrateJobVolumesToBlobAsync(jobId);

            // Preload discovery data to know how many volumes this job has
            int totalVolumes = 0;
            try
            {
                var discoveryData = await _annotationService.GetDiscoveryDataAsync(jobId);
                totalVolumes = discoveryData?.Volumes?.Count ?? 0;
                await _analysisLogService.LogProgressAsync(analysisJobId, $"[HTTP] Discovery data loaded. Volumes found: {totalVolumes}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to preload discovery data for job {JobId} when starting analysis", jobId);
                await _analysisLogService.LogProgressAsync(analysisJobId, $"[HTTP] Warning while loading discovery data: {ex.Message}", "WARNING");
            }
            
            var analysisJob = new AnalysisJob
            {
                RowKey = analysisJobId,
                DiscoveryJobId = jobId,
                Status = AnalysisJobStatus.Pending.ToString(),
                CreatedAt = DateTime.UtcNow,
                TotalVolumes = totalVolumes,
                ProcessedVolumes = 0,
                FailedVolumes = 0
            };
            
            await _analysisJobsTable.AddEntityAsync(analysisJob);
            await _analysisLogService.LogProgressAsync(analysisJobId, "[HTTP] AnalysisJob entity created in table storage.");
            
            if (_useAnalysisQueue)
            {
                var message = JsonSerializer.Serialize(new { AnalysisJobId = analysisJobId, DiscoveryJobId = jobId });
                await _analysisQueue.SendMessageAsync(message);
                await _analysisLogService.LogProgressAsync(analysisJobId, "[HTTP] Analysis message enqueued on 'analysis-queue'. Waiting for processor...");
            }
            else
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, "[HTTP] Queue processing bypassed via AZFO_ENABLE_ANALYSIS_QUEUE!=true. Running analysis in background task...");

                // Fire-and-forget background execution so the HTTP request can return quickly
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _analysisJobRunner.RunAsync(analysisJobId, jobId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background analysis job failed for {AnalysisJobId}", analysisJobId);
                        try
                        {
                            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Background] Analysis job failed: {ex.Message}", "ERROR");
                        }
                        catch
                        {
                            // Swallow any logging exceptions here to avoid crashing the background task pipeline.
                        }
                    }
                });
            }
            
            _logger.LogInformation("Started analysis job {AnalysisJobId} for discovery {DiscoveryJobId}", analysisJobId, jobId);
            
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            var responsePayload = new StartAnalysisResponse
            {
                AnalysisJobId = analysisJobId,
                Status = AnalysisJobStatus.Pending.ToString()
            };
            await response.WriteAsJsonAsync(responsePayload);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting analysis for job {JobId}", jobId);
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[HTTP] Error starting analysis: {ex.Message}", "ERROR");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetLatestAnalysisForDiscoveryJob")]
    public async Task<HttpResponseData> GetLatestAnalysisForDiscoveryJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{discoveryJobId}/analysis-status")] HttpRequestData req,
        string discoveryJobId)
    {
        try
        {
            // Query for analysis jobs with this discovery job ID
            var filter = $"PartitionKey eq 'AnalysisJob' and DiscoveryJobId eq '{discoveryJobId}'";
            var jobs = new List<AnalysisJob>();
            
            await foreach (var entity in _analysisJobsTable.QueryAsync<AnalysisJob>(filter))
            {
                jobs.Add(entity);
            }
            
            // Get the most recent one
            var latestJob = jobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault();
            
            if (latestJob == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { message = "No analysis job found for this discovery job" });
                return notFoundResponse;
            }
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new AnalysisJobStatusResponse
            {
                JobId = latestJob.JobId,
                Status = latestJob.Status,
                TotalVolumes = latestJob.TotalVolumes,
                ProcessedVolumes = latestJob.ProcessedVolumes,
                FailedVolumes = latestJob.FailedVolumes,
                CreatedAt = latestJob.CreatedAt,
                StartedAt = latestJob.StartedAt,
                CompletedAt = latestJob.CompletedAt,
                ErrorMessage = latestJob.ErrorMessage
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest analysis for discovery job {DiscoveryJobId}", discoveryJobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
    
    [Function("GetAnalysisStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analysis/{jobId}/status")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var job = await _analysisJobsTable.GetEntityAsync<AnalysisJob>("AnalysisJob", jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new AnalysisJobStatusResponse
            {
                JobId = job.Value.JobId,
                Status = job.Value.Status,
                TotalVolumes = job.Value.TotalVolumes,
                ProcessedVolumes = job.Value.ProcessedVolumes,
                FailedVolumes = job.Value.FailedVolumes,
                CreatedAt = job.Value.CreatedAt,
                StartedAt = job.Value.StartedAt,
                CompletedAt = job.Value.CompletedAt,
                ErrorMessage = job.Value.ErrorMessage
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis status {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.NotFound);
            return response;
        }
    }

    [Function("GetAnalysisLogs")]
    public async Task<HttpResponseData> GetLogs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analysis/{jobId}/logs")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var logs = await _analysisLogService.GetLogsAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(logs);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis logs {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetVolumes")]
    public async Task<HttpResponseData> GetVolumes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/volumes")] HttpRequestData req,
        string jobId)
    {
        try
        {
            // Ensure volumes are migrated from Tables to Blob storage
            await _migrationService.MigrateJobVolumesToBlobAsync(jobId);
            
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var workloadFilter = query["workloadFilter"];
            var statusFilter = query["statusFilter"];
            var confidenceMin = double.TryParse(query["confidenceMin"], out var conf) ? conf : (double?)null;
            var page = int.TryParse(query["page"], out var p) ? p : 1;
            var pageSize = int.TryParse(query["pageSize"], out var ps) ? ps : 50;
            
            var result = await _annotationService.GetVolumesWithFiltersAsync(
                jobId, workloadFilter, statusFilter, confidenceMin, page, pageSize);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(result, options);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(json);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting volumes for job {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetVolumeDetail")]
    public async Task<HttpResponseData> GetVolumeDetail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/volumes/{volumeId}")] HttpRequestData req,
        string jobId,
        string volumeId)
    {
        try
        {
            // Ensure discovery data exists in Blob storage
            await _migrationService.MigrateJobVolumesToBlobAsync(jobId);

            var data = await _annotationService.GetDiscoveryDataAsync(jobId);
            var volume = data?.Volumes.FirstOrDefault(v => v.VolumeId == volumeId);
            if (volume == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync($"Volume {volumeId} not found in job {jobId}");
                return notFound;
            }

            var dto = new VolumeWithAnalysis
            {
                VolumeId = volume.VolumeId,
                VolumeType = volume.VolumeType,
                VolumeData = volume.VolumeData,
                AiAnalysis = volume.AiAnalysis,
                UserAnnotations = volume.UserAnnotations,
                AnnotationHistory = volume.AnnotationHistory
            };

            // Populate sizing and performance summary for the detail view using the same
            // conventions as the list view.
            var sizing = volume.AiAnalysis?.CapacitySizing;
            if (sizing != null && sizing.HasSufficientData)
            {
                dto.RequiredCapacityGiB = sizing.RecommendedCapacityGiB;
                dto.RequiredThroughputMiBps = sizing.RecommendedThroughputMiBps;
            }

            const double bufferFactor = 1.3;

            switch (volume.VolumeType)
            {
                case "AzureFiles":
                    if (volume.VolumeData is DiscoveredAzureFileShare share)
                    {
                        // Capacity: prefer used+buffer, fall back to quota when used is unknown,
                        // unless AI sizing already provided a recommendation.
                        double? usedGiB = null;
                        if (share.ShareUsageBytes.HasValue && share.ShareUsageBytes.Value > 0)
                        {
                            usedGiB = share.ShareUsageBytes.Value / (1024.0 * 1024.0 * 1024.0);
                        }

                        if (usedGiB.HasValue)
                        {
                            var recommendedGiB = usedGiB.Value * bufferFactor;
                            var currentGiB = (double)(share.ShareQuotaGiB ?? 0);
                            dto.RequiredCapacityGiB ??= Math.Max(recommendedGiB, currentGiB);
                        }
                        else
                        {
                            dto.RequiredCapacityGiB ??= share.ShareQuotaGiB;
                        }

                        double? currentThroughput = null;
                        if (share.ProvisionedBandwidthMiBps.HasValue)
                        {
                            currentThroughput = share.ProvisionedBandwidthMiBps.Value;
                        }
                        else if (share.EstimatedThroughputMiBps.HasValue)
                        {
                            currentThroughput = share.EstimatedThroughputMiBps.Value;
                        }
                        else if (!string.IsNullOrEmpty(share.StorageAccountSku))
                        {
                            var sku = share.StorageAccountSku.ToLowerInvariant();
                            if (sku.Contains("premium"))
                                currentThroughput = 100;
                            else
                                currentThroughput = 60;
                        }
                        dto.CurrentThroughputMiBps = currentThroughput;

                        double? currentIops = null;
                        if (share.ProvisionedIops.HasValue)
                        {
                            currentIops = share.ProvisionedIops.Value;
                        }
                        else if (share.EstimatedIops.HasValue)
                        {
                            currentIops = share.EstimatedIops.Value;
                        }
                        else if (!string.Equals(share.AccessTier, "Premium", StringComparison.OrdinalIgnoreCase))
                        {
                            currentIops = -1; // Unmetered for standard tiers
                        }
                        dto.CurrentIops = currentIops;

                        dto.RequiredThroughputMiBps ??= currentThroughput;
                    }
                    break;

                case "ANF":
                    if (volume.VolumeData is DiscoveredAnfVolume anf)
                    {
                        var provisionedGiB = anf.ProvisionedSizeBytes / (1024.0 * 1024.0 * 1024.0);
                        var estimatedUsedGiB = provisionedGiB * 0.7;
                        var recommendedGiB = estimatedUsedGiB * bufferFactor;
                        dto.RequiredCapacityGiB ??= Math.Max(recommendedGiB, provisionedGiB);

                        double? currentThroughput = null;
                        if (anf.ActualThroughputMibps.HasValue)
                        {
                            currentThroughput = anf.ActualThroughputMibps.Value;
                        }
                        else if (anf.ThroughputMibps.HasValue)
                        {
                            currentThroughput = anf.ThroughputMibps.Value;
                        }
                        else if (anf.EstimatedThroughputMiBps.HasValue)
                        {
                            currentThroughput = anf.EstimatedThroughputMiBps.Value;
                        }
                        else if (!string.IsNullOrEmpty(anf.ServiceLevel))
                        {
                            var level = anf.ServiceLevel.ToLowerInvariant();
                            if (level.Contains("ultra"))
                                currentThroughput = 128;
                            else if (level.Contains("premium"))
                                currentThroughput = 64;
                            else
                                currentThroughput = 16;
                        }
                        dto.CurrentThroughputMiBps = currentThroughput;

                        if (anf.EstimatedIops.HasValue)
                        {
                            dto.CurrentIops = anf.EstimatedIops.Value;
                        }

                        dto.RequiredThroughputMiBps ??= currentThroughput;
                    }
                    break;

                case "ManagedDisk":
                    if (volume.VolumeData is DiscoveredManagedDisk disk)
                    {
                        double? usedGiB = null;
                        if (disk.UsedBytes.HasValue && disk.UsedBytes.Value > 0)
                        {
                            usedGiB = disk.UsedBytes.Value / (1024.0 * 1024.0 * 1024.0);
                        }

                        if (usedGiB.HasValue)
                        {
                            var recommendedGiB = usedGiB.Value * bufferFactor;
                            var currentGiB = (double)disk.DiskSizeGB;
                            dto.RequiredCapacityGiB ??= Math.Max(recommendedGiB, currentGiB);
                        }
                        else
                        {
                            dto.RequiredCapacityGiB ??= disk.DiskSizeGB;
                        }

                        if (disk.EstimatedThroughputMiBps.HasValue)
                        {
                            dto.CurrentThroughputMiBps = disk.EstimatedThroughputMiBps.Value;
                        }
                        else if (!string.IsNullOrEmpty(disk.DiskSku))
                        {
                            var sku = disk.DiskSku.ToLowerInvariant();
                            if (sku.Contains("premium"))
                                dto.CurrentThroughputMiBps = 100;
                            else if (sku.Contains("standardssd"))
                                dto.CurrentThroughputMiBps = 60;
                            else
                                dto.CurrentThroughputMiBps = 30;
                        }

                        if (disk.EstimatedIops.HasValue)
                        {
                            dto.CurrentIops = disk.EstimatedIops.Value;
                        }

                        dto.RequiredThroughputMiBps ??= dto.CurrentThroughputMiBps;
                    }
                    break;
            }

            // Attach latest cost summary for this specific volume, if available (by normalized VolumeId)
            var jobCosts = await _resourceStorage.GetVolumeCostsByJobIdAsync(jobId);
            var cost = jobCosts
                .Where(c => string.Equals(c.VolumeId, volume.VolumeId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.AnalysisTimestamp)
                .FirstOrDefault();

            if (cost != null)
            {
                dto.CostSummary = new CostSummary
                {
                    TotalCost30Days = cost.TotalCostForPeriod,
                    DailyAverage = cost.TotalCostPerDay,
                    Currency = cost.CostComponents.FirstOrDefault()?.Currency ?? "USD",
                    IsActual = cost.CostComponents.Count > 0 && cost.CostComponents.All(c => !c.IsEstimated),
                    PeriodStart = cost.PeriodStart,
                    PeriodEnd = cost.PeriodEnd
                };
                dto.CostStatus = "Completed";
            }
            else
            {
                dto.CostStatus = "Pending";
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(dto, options);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(json);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting volume detail for {VolumeId} in job {JobId}", volumeId, jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UpdateVolumeAnnotations")]
    public async Task<HttpResponseData> UpdateAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "discovery/{jobId}/volumes/{volumeId}/annotations")] HttpRequestData req,
        string jobId,
        string volumeId)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                PropertyNameCaseInsensitive = true
            };
            var request = await JsonSerializer.DeserializeAsync<UpdateAnnotationsRequest>(req.Body, options);
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                return badRequest;
            }
            
            var annotations = new UserAnnotations
            {
                ConfirmedWorkloadId = request.ConfirmedWorkloadId,
                CustomTags = request.CustomTags,
                MigrationStatus = request.MigrationStatus,
                Notes = request.Notes,
                TargetCapacityGiB = request.TargetCapacityGiB,
                TargetThroughputMiBps = request.TargetThroughputMiBps
            };
            
            await _annotationService.UpdateVolumeAnnotationsAsync(jobId, volumeId, annotations, "user-id");
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating annotations for volume {VolumeId}", volumeId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("BulkUpdateAnnotations")]
    public async Task<HttpResponseData> BulkUpdate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "discovery/{jobId}/volumes/bulk-annotations")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                PropertyNameCaseInsensitive = true
            };
            var request = await JsonSerializer.DeserializeAsync<BulkUpdateAnnotationsRequest>(req.Body, options);
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                return badRequest;
            }
            
            await _annotationService.BulkUpdateAnnotationsAsync(jobId, request.VolumeIds, request.Annotations, "user-id");
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk updating annotations for job {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("ExportVolumes")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/export")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var format = query["format"] ?? "json";
            
            var data = await _annotationService.ExportVolumesAsync(jobId, format);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", format == "csv" ? "text/csv" : "application/json");
            response.Headers.Add("Content-Disposition", $"attachment; filename=volumes-{jobId}.{format}");
            await response.WriteStringAsync(data);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting volumes for job {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}
