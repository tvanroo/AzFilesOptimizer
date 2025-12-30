using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class JobsFunction
{
    private readonly ILogger _logger;
    private readonly JobStorageService _jobStorage;

    public JobsFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<JobsFunction>();
        
        // Get storage connection string from environment
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _jobStorage = new JobStorageService(connectionString);
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

    [Function("CreateDiscoveryJob")]
    public async Task<HttpResponseData> CreateDiscoveryJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/discovery")] HttpRequestData req)
    {
        _logger.LogInformation("Creating discovery job");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreateDiscoveryJobRequest>(requestBody);

            if (request == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequestResponse;
            }

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
}

public class CreateDiscoveryJobRequest
{
    public string? TenantId { get; set; }
    public string? SubscriptionId { get; set; }
    public string[]? ResourceGroupNames { get; set; }
}
