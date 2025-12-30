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

    public async Task UpdateDiscoveryJobAsync(DiscoveryJob job)
    {
        job.UpdatedAt = DateTime.UtcNow;
        await _jobTableClient.UpdateEntityAsync(job, job.ETag, TableUpdateMode.Replace);
    }
}
