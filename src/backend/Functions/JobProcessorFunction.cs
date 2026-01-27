using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure.Identity;
using AzFilesOptimizer.Backend.Services;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Functions;

public class JobProcessorFunction
{
    private readonly ILogger _logger;
    private readonly JobStorageService _jobStorage;

    public JobProcessorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<JobProcessorFunction>();
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _jobStorage = new JobStorageService(connectionString);
    }

    [Function("JobProcessor")]
    public async Task Run(
        [QueueTrigger("job-queue", Connection = "AzureWebJobsStorage")] string message,
        FunctionContext context)
    {
        _logger.LogInformation("JobProcessor triggered. Message: {Message}", message);

        try
        {
            var jobMessage = JsonSerializer.Deserialize<JobMessage>(message);
            if (jobMessage == null || string.IsNullOrEmpty(jobMessage.JobId))
            {
                _logger.LogError("Invalid job message format");
                return;
            }

            if (jobMessage.JobType == "Discovery")
            {
                await ProcessDiscoveryJobAsync(jobMessage.JobId);
            }
            else
            {
                _logger.LogWarning("Unknown job type: {JobType}", jobMessage.JobType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job message");
        }
    }

    private async Task ProcessDiscoveryJobAsync(string jobId)
    {
        _logger.LogInformation("Processing discovery job: {JobId}", jobId);

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

            // For now, use DefaultAzureCredential which will use managed identity in Azure
            // TODO: Later we can use the user's token from the request
            var credential = new DefaultAzureCredential();
            var discoveryService = new DiscoveryService(_logger);

            if (string.IsNullOrEmpty(job.SubscriptionId))
            {
                throw new InvalidOperationException("Subscription ID is required for discovery");
            }

            // Execute discovery
            var result = await discoveryService.DiscoverResourcesAsync(
                job.SubscriptionId,
                job.ResourceGroupNames,
                credential);

            // Update job with results
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.AzureFilesSharesFound = result.AzureFileShares.Count;
            job.AnfVolumesFound = result.AnfVolumes.Count;
job.TotalCapacityBytes = result.AzureFileShares.Sum(s => (s.ShareQuotaGiB ?? 0) * 1024L * 1024L * 1024L) +
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
    }
}

public class JobMessage
{
    public string JobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
}
