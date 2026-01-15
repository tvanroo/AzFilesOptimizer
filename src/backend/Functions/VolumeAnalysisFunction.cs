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

        _useAnalysisQueue = string.Equals(
            Environment.GetEnvironmentVariable("AZFO_ENABLE_ANALYSIS_QUEUE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!_useAnalysisQueue)
        {
            _logger.LogWarning("AZFO_ENABLE_ANALYSIS_QUEUE is not enabled. Analysis jobs will run inline via StartAnalysis until queue processing is re-enabled.");
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
                await _analysisLogService.LogProgressAsync(analysisJobId, "[HTTP] Queue processing bypassed via AZFO_ENABLE_ANALYSIS_QUEUE!=true. Running analysis inline...");
                await _analysisJobRunner.RunAsync(analysisJobId, jobId);
            }
            
            _logger.LogInformation("Started analysis job {AnalysisJobId} for discovery {DiscoveryJobId}", analysisJobId, jobId);
            
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            var responsePayload = new StartAnalysisResponse
            {
                AnalysisJobId = analysisJobId,
                Status = _useAnalysisQueue ? AnalysisJobStatus.Pending.ToString() : AnalysisJobStatus.Completed.ToString()
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
