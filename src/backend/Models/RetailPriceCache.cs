using Azure;
using Azure.Data.Tables;

namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Table Storage entity for caching Azure Retail Prices API data
/// </summary>
public class RetailPriceCache : ITableEntity
{
    /// <summary>
    /// Partition key: Region (e.g., "eastus", "westeurope")
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Row key: MeterKey (e.g., "azurefiles-hot-lrs-storage", "anf-premium-capacity", "manageddisk-p30-lrs")
    /// </summary>
    public string RowKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp (managed by Table Storage)
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }
    
    /// <summary>
    /// ETag (managed by Table Storage)
    /// </summary>
    public ETag ETag { get; set; }
    
    // Retail Prices API fields
    
    /// <summary>
    /// Resource type (AzureFiles, ANF, ManagedDisk)
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Meter name from Retail Prices API
    /// </summary>
    public string MeterName { get; set; } = string.Empty;
    
    /// <summary>
    /// Product name from Retail Prices API
    /// </summary>
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// SKU name from Retail Prices API
    /// </summary>
    public string SkuName { get; set; } = string.Empty;
    
    /// <summary>
    /// Service name from Retail Prices API
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;
    
    /// <summary>
    /// Meter ID from Retail Prices API
    /// </summary>
    public string MeterId { get; set; } = string.Empty;
    
    /// <summary>
    /// Unit price (retail price)
    /// </summary>
    public double UnitPrice { get; set; }
    
    /// <summary>
    /// Unit of measure (e.g., "1/Month", "1 Hour", "10K", "1 GB/Month")
    /// </summary>
    public string UnitOfMeasure { get; set; } = string.Empty;
    
    /// <summary>
    /// Currency code (e.g., "USD")
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Effective start date from Retail Prices API
    /// </summary>
    public DateTime EffectiveDate { get; set; }
    
    /// <summary>
    /// ARM region name (e.g., "eastus")
    /// </summary>
    public string ArmRegionName { get; set; } = string.Empty;
    
    /// <summary>
    /// Location display name (e.g., "US East")
    /// </summary>
    public string Location { get; set; } = string.Empty;
    
    /// <summary>
    /// ARM SKU name (if applicable)
    /// </summary>
    public string ArmSkuName { get; set; } = string.Empty;
    
    /// <summary>
    /// Tier information (if applicable)
    /// </summary>
    public string Tier { get; set; } = string.Empty;
    
    /// <summary>
    /// Redundancy type (if applicable)
    /// </summary>
    public string Redundancy { get; set; } = string.Empty;
    
    /// <summary>
    /// Service level (for ANF)
    /// </summary>
    public string ServiceLevel { get; set; } = string.Empty;
    
    /// <summary>
    /// When this cache entry was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this cache entry expires
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    
    /// <summary>
    /// Indicates if this cache entry is expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    
    /// <summary>
    /// Create a meter key for Azure Files
    /// </summary>
    public static string CreateAzureFilesMeterKey(string tier, string redundancy, string meterType)
    {
        return $"azurefiles-{tier.ToLowerInvariant()}-{redundancy.ToLowerInvariant()}-{meterType.ToLowerInvariant()}";
    }
    
    /// <summary>
    /// Create a meter key for Azure NetApp Files
    /// </summary>
    public static string CreateAnfMeterKey(string serviceLevel, string meterType)
    {
        return $"anf-{serviceLevel.ToLowerInvariant()}-{meterType.ToLowerInvariant()}";
    }
    
    /// <summary>
    /// Create a meter key for Managed Disks
    /// </summary>
    public static string CreateManagedDiskMeterKey(string sku, string redundancy)
    {
        return $"manageddisk-{sku.ToLowerInvariant()}-{redundancy.ToLowerInvariant()}";
    }
}
