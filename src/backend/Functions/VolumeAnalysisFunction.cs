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
    }

    [Function("StartAnalysis")]
    public async Task<HttpResponseData> StartAnalysis(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "discovery/{jobId}/analyze")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var analysisJobId = Guid.NewGuid().ToString();
            
            var analysisJob = new AnalysisJob
            {
                RowKey = analysisJobId,
                DiscoveryJobId = jobId,
                Status = AnalysisJobStatus.Pending.ToString(),
                CreatedAt = DateTime.UtcNow
            };
            
            await _analysisJobsTable.AddEntityAsync(analysisJob);
            
            // Queue analysis message
            var message = JsonSerializer.Serialize(new { analysisJobId, discoveryJobId = jobId });
            await _analysisQueue.SendMessageAsync(message);
            
            _logger.LogInformation("Started analysis job {AnalysisJobId} for discovery {DiscoveryJobId}", analysisJobId, jobId);
            
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new StartAnalysisResponse
            {
                AnalysisJobId = analysisJobId,
                Status = AnalysisJobStatus.Pending.ToString()
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting analysis for job {JobId}", jobId);
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

    [Function("GetVolumes")]
    public async Task<HttpResponseData> GetVolumes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "discovery/{jobId}/volumes")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var workloadFilter = query["workloadFilter"];
            var statusFilter = query["statusFilter"];
            var confidenceMin = double.TryParse(query["confidenceMin"], out var conf) ? conf : (double?)null;
            var page = int.TryParse(query["page"], out var p) ? p : 1;
            var pageSize = int.TryParse(query["pageSize"], out var ps) ? ps : 50;
            
            var result = await _annotationService.GetVolumesWithFiltersAsync(
                jobId, workloadFilter, statusFilter, confidenceMin, page, pageSize);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
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

    [Function("UpdateVolumeAnnotations")]
    public async Task<HttpResponseData> UpdateAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "discovery/{jobId}/volumes/{volumeId}/annotations")] HttpRequestData req,
        string jobId,
        string volumeId)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<UpdateAnnotationsRequest>(req.Body);
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
                Notes = request.Notes
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
            var request = await JsonSerializer.DeserializeAsync<BulkUpdateAnnotationsRequest>(req.Body);
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
