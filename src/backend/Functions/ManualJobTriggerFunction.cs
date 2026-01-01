using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using AzFilesOptimizer.Backend.Services;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Functions;

public class ManualJobTriggerFunction
{
    private readonly ILogger _logger;
    private readonly JobStorageService _jobStorage;

    public ManualJobTriggerFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ManualJobTriggerFunction>();
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _jobStorage = new JobStorageService(connectionString);
    }

    [Function("TriggerJob")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/trigger")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Manually triggering job: {JobId}", jobId);

        try
        {
            var job = await _jobStorage.GetDiscoveryJobAsync(jobId);
            if (job == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Job not found" });
                return notFoundResponse;
            }

            // Update job status to Running
            job.Status = JobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            await _jobStorage.UpdateDiscoveryJobAsync(job);

            // Execute discovery in background (don't await to return quickly)
            _ = Task.Run(async () =>
            {
                try
                {
                    var credential = new DefaultAzureCredential();
                    var discoveryService = new DiscoveryService(_logger);

                    if (string.IsNullOrEmpty(job.SubscriptionId))
                    {
                        throw new InvalidOperationException("Subscription ID is required for discovery");
                    }

                    var resourceGroupName = job.ResourceGroupNames?.FirstOrDefault();
                    var result = await discoveryService.DiscoverResourcesAsync(
                        job.SubscriptionId,
                        resourceGroupName,
                        credential);

                    // Update job with results
                    job.Status = JobStatus.Completed;
                    job.CompletedAt = DateTime.UtcNow;
                    job.AzureFilesSharesFound = result.AzureFileShares.Count;
                    job.AnfVolumesFound = result.AnfVolumes.Count;
                    job.TotalCapacityBytes = result.AzureFileShares.Sum(s => (s.QuotaGiB ?? 0) * 1024L * 1024L * 1024L) +
                                             result.AnfVolumes.Sum(v => v.ProvisionedSizeBytes);

                    await _jobStorage.UpdateDiscoveryJobAsync(job);

                    _logger.LogInformation("Discovery job completed: {JobId}. Found {SharesCount} shares and {VolumesCount} volumes",
                        jobId, result.AzureFileShares.Count, result.AnfVolumes.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Discovery job failed: {JobId}", jobId);

                    job.Status = JobStatus.Failed;
                    job.CompletedAt = DateTime.UtcNow;
                    job.ErrorMessage = ex.Message;
                    job.ErrorDetails = ex.ToString();

                    await _jobStorage.UpdateDiscoveryJobAsync(job);
                }
            });

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new { message = "Job triggered successfully", jobId });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering job {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to trigger job", details = ex.Message });
            return errorResponse;
        }
    }
}
