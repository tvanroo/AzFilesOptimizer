using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class JobsFunction
{
    private readonly ILogger _logger;
    private readonly JobStorageService _jobStorage;
    private readonly JobLogService _jobLogService;

    public JobsFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<JobsFunction>();
        
        // Get storage connection string from environment
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _jobStorage = new JobStorageService(connectionString);
        _jobLogService = new JobLogService(connectionString);
    }

    [Function("GetJobs")]
    public async Task<HttpResponseData> GetJobs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs")] HttpRequestData req)
    {
        _logger.LogInformation("Getting all jobs");

        try
        {
            var jobs = await _jobStorage.GetAllJobsAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(jobs);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting jobs");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve jobs", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("GetJob")]
    public async Task<HttpResponseData> GetJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting job: {JobId}", jobId);

        try
        {
            var job = await _jobStorage.GetDiscoveryJobAsync(jobId);
            
            if (job == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Job not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(job);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve job", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("GetJobLogs")]
    public async Task<HttpResponseData> GetJobLogs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}/logs")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting logs for job: {JobId}", jobId);

        try
        {
            var logs = await _jobLogService.GetLogsAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(logs);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve logs", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("CreateDiscoveryJob")]
    public async Task<HttpResponseData> CreateDiscoveryJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/discovery")] HttpRequestData req)
    {
        _logger.LogInformation("Creating discovery job");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("Request body: {RequestBody}", requestBody);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var request = JsonSerializer.Deserialize<CreateDiscoveryJobRequest>(requestBody, options);

            if (request == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequestResponse;
            }
            
            _logger.LogInformation("Parsed request - SubscriptionId: {SubscriptionId}, TenantId: {TenantId}", 
                request.SubscriptionId, request.TenantId);

            // TODO: Extract user info from JWT token in Authorization header
            // For now, use placeholder values
            var job = new DiscoveryJob
            {
                UserId = "user-placeholder",
                UserEmail = "user@example.com",
                TenantId = request.TenantId ?? "unknown",
                SubscriptionId = request.SubscriptionId,
                ResourceGroupNames = request.ResourceGroupNames
            };

            var createdJob = await _jobStorage.CreateDiscoveryJobAsync(job);

            // Execute job immediately in background
            _ = Task.Run(async () => await ExecuteDiscoveryJobAsync(createdJob.JobId));

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdJob);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating discovery job");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to create job", details = ex.Message });
            return errorResponse;
        }
    }

    private async Task ExecuteDiscoveryJobAsync(string jobId)
    {
        _logger.LogInformation("Executing discovery job: {JobId}", jobId);
        await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Job started");

        var job = await _jobStorage.GetDiscoveryJobAsync(jobId);
        if (job == null)
        {
            _logger.LogError("Job not found: {JobId}", jobId);
            return;
        }

        try
        {
            // Update job status to Running
            job.Status = JobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            await _jobStorage.UpdateDiscoveryJobAsync(job);
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Authenticating to Azure...");

            var credential = new DefaultAzureCredential();
            var discoveryService = new DiscoveryService(_logger);

            if (string.IsNullOrEmpty(job.SubscriptionId))
            {
                throw new InvalidOperationException("Subscription ID is required for discovery");
            }

            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Scanning subscription {job.SubscriptionId}");
            if (job.ResourceGroupNames != null && job.ResourceGroupNames.Length > 0)
            {
                await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Filtering to resource groups: {string.Join(", ", job.ResourceGroupNames)}");
            }
            else
            {
                await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Scanning all resource groups in subscription");
            }

            // Execute discovery
            var resourceGroupName = job.ResourceGroupNames?.FirstOrDefault();
            var result = await discoveryService.DiscoverResourcesAsync(
                job.SubscriptionId,
                resourceGroupName,
                credential);
            
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Discovery complete");

            // Update job with results
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.AzureFilesSharesFound = result.AzureFileShares.Count;
            job.AnfVolumesFound = result.AnfVolumes.Count;
            job.TotalCapacityBytes = result.AzureFileShares.Sum(s => (s.QuotaGiB ?? 0) * 1024L * 1024L * 1024L) +
                                     result.AnfVolumes.Sum(v => v.ProvisionedSizeBytes);

            await _jobStorage.UpdateDiscoveryJobAsync(job);
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Found {result.AzureFileShares.Count} Azure Files shares and {result.AnfVolumes.Count} ANF volumes");
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Job completed successfully");

            _logger.LogInformation("Discovery job completed: {JobId}. Found {SharesCount} shares and {VolumesCount} volumes",
                jobId, result.AzureFileShares.Count, result.AnfVolumes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery job failed: {JobId}", jobId);
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: {ex.Message}");

            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = ex.Message;
            job.ErrorDetails = ex.ToString();

            await _jobStorage.UpdateDiscoveryJobAsync(job);
        }
    }
}

public class CreateDiscoveryJobRequest
{
    public string? TenantId { get; set; }
    public string? SubscriptionId { get; set; }
    public string[]? ResourceGroupNames { get; set; }
}
