using Azure;
using Azure.Data.Tables;
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
    
    // Resource group names stored as JSON string for Table Storage compatibility
    // This property is stored in Table Storage
    private string? _resourceGroupNamesJson;
    
    // Internal property for Table Storage (not exposed in JSON responses)
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ResourceGroupNamesJson 
    { 
        get 
        {
            // Always sync from array to JSON before reading
            if (_cachedResourceGroupNames != null)
            {
                _resourceGroupNamesJson = _cachedResourceGroupNames.Length == 0 
                    ? null 
                    : JsonSerializer.Serialize(_cachedResourceGroupNames);
            }
            return _resourceGroupNamesJson;
        }
        set 
        {
            _resourceGroupNamesJson = value;
            _cachedResourceGroupNames = null; // Clear cache
        }
    }
    
    // Property for working with resource groups as array (used by code and JSON responses)
    private string[]? _cachedResourceGroupNames;
    public string[]? ResourceGroupNames 
    { 
        get 
        {
            if (_cachedResourceGroupNames == null && !string.IsNullOrEmpty(_resourceGroupNamesJson))
            {
                _cachedResourceGroupNames = JsonSerializer.Deserialize<string[]>(_resourceGroupNamesJson);
            }
            return _cachedResourceGroupNames;
        }
        set 
        {
            _cachedResourceGroupNames = value;
            _resourceGroupNamesJson = value == null || value.Length == 0
                ? null 
                : JsonSerializer.Serialize(value);
        }
    }
    
    // Results summary
    public int AzureFilesSharesFound { get; set; }
    public int AnfVolumesFound { get; set; }
    public int AnfAccountsFound { get; set; }
    public long TotalCapacityBytes { get; set; }
    
    // Error tracking
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}
