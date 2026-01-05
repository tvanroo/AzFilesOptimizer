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
    private readonly DiscoveredResourceStorageService _resourceStorage;

    public JobsFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<JobsFunction>();
        
        // Get storage connection string from environment
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _jobStorage = new JobStorageService(connectionString);
        _jobLogService = new JobLogService(connectionString);
        _resourceStorage = new DiscoveredResourceStorageService(connectionString);
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
    
    [Function("GetJobShares")]
    public async Task<HttpResponseData> GetJobShares(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}/shares")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Getting discovered shares for job: {JobId}", jobId);

        try
        {
            var shares = await _resourceStorage.GetSharesByJobIdAsync(jobId);
            var volumes = await _resourceStorage.GetVolumesByJobIdAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { 
                shares = shares,
                volumes = volumes,
                totalShares = shares.Count,
                totalVolumes = volumes.Count
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting shares for job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve shares", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("DeleteJob")]
    public async Task<HttpResponseData> DeleteJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "jobs/{jobId}")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Deleting job: {JobId}", jobId);

        try
        {
            // Delete the job
            await _jobStorage.DeleteJobAsync(jobId);
            
            // Delete associated logs
            await _jobLogService.DeleteLogsAsync(jobId);
            
            // Delete discovered resources
            await _resourceStorage.DeleteSharesByJobIdAsync(jobId);
            await _resourceStorage.DeleteVolumesByJobIdAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Job deleted successfully", jobId });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to delete job", details = ex.Message });
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

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.SubscriptionId))
            {
                _logger.LogWarning("Missing required field: SubscriptionId");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "SubscriptionId is required" });
                return badRequestResponse;
            }

            // Extract user info from JWT token in Authorization header
            string userId = "anonymous";
            string userEmail = "anonymous@example.com";
            string tenantId = request.TenantId ?? "unknown";
            
            if (req.Headers.TryGetValues("Authorization", out var authHeaders))
            {
                var authHeader = authHeaders.FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    var token = authHeader.Substring("Bearer ".Length);
                    try
                    {
                        // Parse JWT token (simple parsing without validation since it's already validated by Azure AD)
                        var tokenParts = token.Split('.');
                        if (tokenParts.Length > 1)
                        {
                            var payload = tokenParts[1];
                            // Add padding if needed
                            var padding = payload.Length % 4;
                            if (padding > 0) payload += new string('=', 4 - padding);
                            
                            var payloadJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                            var payloadObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
                            
                            if (payloadObj != null)
                            {
                                if (payloadObj.TryGetValue("tid", out var tidValue))
                                    tenantId = tidValue.GetString() ?? tenantId;
                                if (payloadObj.TryGetValue("oid", out var oidValue))
                                    userId = oidValue.GetString() ?? userId;
                                if (payloadObj.TryGetValue("upn", out var upnValue))
                                    userEmail = upnValue.GetString() ?? userEmail;
                                else if (payloadObj.TryGetValue("email", out var emailValue))
                                    userEmail = emailValue.GetString() ?? userEmail;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse JWT token");
                    }
                }
            }
            
            _logger.LogInformation("Creating job for user {UserId} in tenant {TenantId}", userId, tenantId);
            
            var job = new DiscoveryJob
            {
                UserId = userId,
                UserEmail = userEmail,
                TenantId = tenantId,
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
            var discoveryService = new DiscoveryService(_logger, _jobLogService, jobId);

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
                credential,
                job.TenantId);
            
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Discovery complete");
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Persisting discovered resources...");

            // Persist discovered resources to Table Storage
            await _resourceStorage.SaveSharesAsync(jobId, result.AzureFileShares);
            await _resourceStorage.SaveVolumesAsync(jobId, result.AnfVolumes);
            
            await _jobLogService.AddLogAsync(jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Saved {result.AzureFileShares.Count} shares and {result.AnfVolumes.Count} volumes to storage");

            // Update job with results
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.AzureFilesSharesFound = result.AzureFileShares.Count;
            job.AnfVolumesFound = result.AnfVolumes.Count;
            job.TotalCapacityBytes = result.AzureFileShares.Sum(s => (s.ShareQuotaGiB ?? 0) * 1024L * 1024L * 1024L) +
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
