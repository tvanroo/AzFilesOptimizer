using Azure.Data.Tables;
using Azure.Storage.Queues;
using AzFilesOptimizer.Backend.Models;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class JobStorageService
{
    private readonly TableClient _jobTableClient;
    private readonly QueueClient _jobQueueClient;

    public JobStorageService(string storageConnectionString)
    {
        var tableServiceClient = new TableServiceClient(storageConnectionString);
        _jobTableClient = tableServiceClient.GetTableClient("jobs");
        _jobTableClient.CreateIfNotExists();

        var queueServiceClient = new QueueServiceClient(storageConnectionString);
        _jobQueueClient = queueServiceClient.GetQueueClient("job-queue");
        _jobQueueClient.CreateIfNotExists();
    }

    public async Task<DiscoveryJob> CreateDiscoveryJobAsync(DiscoveryJob job)
    {
        job.RowKey = Guid.NewGuid().ToString();
        job.CreatedAt = DateTime.UtcNow;
        job.Status = JobStatus.Pending;

        await _jobTableClient.AddEntityAsync(job);

        // Queue the job for processing
        var message = new { JobId = job.JobId, JobType = "Discovery" };
        await _jobQueueClient.SendMessageAsync(JsonSerializer.Serialize(message));

        return job;
    }

    public async Task<DiscoveryJob?> GetDiscoveryJobAsync(string jobId)
    {
        try
        {
            var response = await _jobTableClient.GetEntityAsync<DiscoveryJob>("DiscoveryJob", jobId);
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<DiscoveryJob>> GetAllJobsAsync(int maxResults = 100)
    {
        var jobs = new List<DiscoveryJob>();
        
        await foreach (var job in _jobTableClient.QueryAsync<DiscoveryJob>(
            filter: $"PartitionKey eq 'DiscoveryJob'",
            maxPerPage: maxResults))
        {
            jobs.Add(job);
        }

        return jobs.OrderByDescending(j => j.CreatedAt).ToList();
    }

    public async Task UpdateDiscoveryJobAsync(DiscoveryJob job, int maxRetries = 3)
    {
        job.UpdatedAt = DateTime.UtcNow;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await _jobTableClient.UpdateEntityAsync(job, job.ETag, TableUpdateMode.Replace);
                return;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                // ETag conflict - fetch latest version and retry
                var latestJob = await GetDiscoveryJobAsync(job.RowKey);
                if (latestJob == null)
                {
                    throw new InvalidOperationException($"Job {job.RowKey} not found during retry");
                }
                
                // Preserve the updates we want to make
                latestJob.Status = job.Status;
                latestJob.StartedAt = job.StartedAt;
                latestJob.CompletedAt = job.CompletedAt;
                latestJob.AzureFilesSharesFound = job.AzureFilesSharesFound;
                latestJob.AnfVolumesFound = job.AnfVolumesFound;
                latestJob.ManagedDisksFound = job.ManagedDisksFound;
                latestJob.TotalCapacityBytes = job.TotalCapacityBytes;
                latestJob.ErrorMessage = job.ErrorMessage;
                latestJob.ErrorDetails = job.ErrorDetails;
                latestJob.UpdatedAt = DateTime.UtcNow;
                
                // Use the latest ETag and retry
                job = latestJob;
                
                // Small delay before retry
                await Task.Delay(100 * (attempt + 1));
            }
        }
    }

    public async Task DeleteJobAsync(string jobId)
    {
        try
        {
            await _jobTableClient.DeleteEntityAsync("DiscoveryJob", jobId);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Job already deleted or doesn't exist
        }
    }
}
