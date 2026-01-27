using Azure;
using Azure.Data.Tables;
using System.Runtime.Serialization;
using System.Text.Json;

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
    public string TenantId { get; set; } = string.Empty;
    
    // Resource group names as comma-separated string for Table Storage
    // This is the actual property stored in Table Storage
    [System.Text.Json.Serialization.JsonIgnore] // Hide from JSON responses
    public string? ResourceGroupNamesString { get; set; }
    
    // Property for working with resource groups as array
    private string[]? _cachedResourceGroupNames;
    
    // Public array property exposed in JSON responses
    [IgnoreDataMember] // Tell Azure.Data.Tables to ignore this property
    public string[]? ResourceGroupNames
    { 
        get 
        {
            if (_cachedResourceGroupNames == null && !string.IsNullOrEmpty(ResourceGroupNamesString))
            {
                _cachedResourceGroupNames = ResourceGroupNamesString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            return _cachedResourceGroupNames;
        }
        set 
        {
            _cachedResourceGroupNames = value;
            ResourceGroupNamesString = value == null || value.Length == 0
                ? null 
                : string.Join(",", value);
        }
    }
    
    // Results summary
    public int AzureFilesSharesFound { get; set; }
    public int AnfVolumesFound { get; set; }
    public int ManagedDisksFound { get; set; }
    public int AnfAccountsFound { get; set; }
    public long TotalCapacityBytes { get; set; }
    
    // Error tracking
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}
