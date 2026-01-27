using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

public class JobLogService
{
    private readonly TableClient _logTableClient;

    public JobLogService(string storageConnectionString)
    {
        var tableServiceClient = new TableServiceClient(storageConnectionString);
        _logTableClient = tableServiceClient.GetTableClient("joblogs");
        _logTableClient.CreateIfNotExists();
    }

    public async Task AddLogAsync(string jobId, string message)
    {
        var logEntry = new JobLogEntry
        {
            PartitionKey = jobId,
            RowKey = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        await _logTableClient.AddEntityAsync(logEntry);
    }

    public async Task<List<JobLogEntry>> GetLogsAsync(string jobId)
    {
        var logs = new List<JobLogEntry>();
        
        await foreach (var log in _logTableClient.QueryAsync<JobLogEntry>(
            filter: $"PartitionKey eq '{jobId}'"))
        {
            logs.Add(log);
        }

        return logs.OrderBy(l => l.CreatedAt).ToList();
    }

    public async Task DeleteLogsAsync(string jobId)
    {
        var logs = new List<JobLogEntry>();
        
        // Get all logs for this job
        await foreach (var log in _logTableClient.QueryAsync<JobLogEntry>(
            filter: $"PartitionKey eq '{jobId}'"))
        {
            logs.Add(log);
        }

        // Delete each log entry
        foreach (var log in logs)
        {
            try
            {
                await _logTableClient.DeleteEntityAsync(log.PartitionKey, log.RowKey);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Log already deleted
            }
        }
    }
}

public class JobLogEntry : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // JobId
    public string RowKey { get; set; } = string.Empty; // Log entry ID
    public DateTimeOffset? Timestamp { get; set; }
    public Azure.ETag ETag { get; set; }
    
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
