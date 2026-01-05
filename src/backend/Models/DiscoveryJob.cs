using Azure;
using Azure.Data.Tables;

namespace AzFilesOptimizer.Backend.Models;

public class DiscoveryJob : ITableEntity
{
    public string PartitionKey { get; set; } = "DiscoveryJob";
    public string RowKey { get; set; } = string.Empty; // JobId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Job properties
    public string JobId => RowKey;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // User context
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    
    // Discovery scope
    public string SubscriptionId { get; set; } = string.Empty;
    public string[]? ResourceGroupNames { get; set; }
    public string TenantId { get; set; } = string.Empty;
    
    // Results summary
    public int AzureFilesSharesFound { get; set; }
    public int AnfVolumesFound { get; set; }
    public int AnfAccountsFound { get; set; }
    public long TotalCapacityBytes { get; set; }
    
    // Error tracking
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}
