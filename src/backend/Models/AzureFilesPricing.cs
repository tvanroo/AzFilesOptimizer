namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Azure Files pay-as-you-go access tiers
/// </summary>
public enum AzureFilesAccessTier
{
    TransactionOptimized,
    Hot,
    Cool
}

/// <summary>
/// Azure Files provisioned tiers
/// </summary>
public enum AzureFilesProvisionedTier
{
    ProvisionedV1,
    ProvisionedV2SSD,
    ProvisionedV2HDD
}

/// <summary>
/// Storage redundancy options
/// </summary>
public enum StorageRedundancy
{
    LRS,  // Locally Redundant Storage
    ZRS,  // Zone Redundant Storage
    GRS,  // Geo Redundant Storage
    GZRS, // Geo-Zone Redundant Storage
    RAGRS, // Read-Access Geo Redundant Storage
    RAGZRS // Read-Access Geo-Zone Redundant Storage
}

/// <summary>
/// Azure Files meter-based pricing for a specific tier and redundancy combination
/// </summary>
public class AzureFilesMeterPricing
{
    /// <summary>
    /// Access tier (for pay-as-you-go)
    /// </summary>
    public AzureFilesAccessTier? AccessTier { get; set; }
    
    /// <summary>
    /// Provisioned tier (for provisioned billing)
    /// </summary>
    public AzureFilesProvisionedTier? ProvisionedTier { get; set; }
    
    /// <summary>
    /// Storage redundancy type
    /// </summary>
    public StorageRedundancy Redundancy { get; set; }
    
    /// <summary>
    /// Region name
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    // Pay-as-you-go pricing
    
    /// <summary>
    /// Storage price per GB per month
    /// </summary>
    public double StoragePricePerGbMonth { get; set; }
    
    /// <summary>
    /// Write operations price per 10K operations
    /// </summary>
    public double WriteOperationsPricePer10K { get; set; }
    
    /// <summary>
    /// Read operations price per 10K operations
    /// </summary>
    public double ReadOperationsPricePer10K { get; set; }
    
    /// <summary>
    /// List and create container operations price per 10K operations
    /// </summary>
    public double ListOperationsPricePer10K { get; set; }
    
    /// <summary>
    /// Other operations price per 10K operations
    /// </summary>
    public double OtherOperationsPricePer10K { get; set; }
    
    // Provisioned v2 pricing (SSD/HDD)
    
    /// <summary>
    /// Provisioned storage price per GiB per hour (Provisioned v2)
    /// </summary>
    public double ProvisionedStoragePricePerGibHour { get; set; }
    
    /// <summary>
    /// Provisioned IOPS price per IOPS per hour (Provisioned v2)
    /// </summary>
    public double ProvisionedIOPSPricePerHour { get; set; }
    
    /// <summary>
    /// Provisioned throughput price per MiB/sec per hour (Provisioned v2)
    /// </summary>
    public double ProvisionedThroughputPricePerMiBPerSecPerHour { get; set; }
    
    // Provisioned v1 pricing
    
    /// <summary>
    /// Provisioned storage price per GiB per month (Provisioned v1)
    /// Billed hourly at monthly rate
    /// </summary>
    public double ProvisionedV1StoragePricePerGibMonth { get; set; }
    
    // Shared pricing components
    
    /// <summary>
    /// Snapshot storage price per GiB per month
    /// </summary>
    public double SnapshotPricePerGbMonth { get; set; }
    
    /// <summary>
    /// Data egress (transfer out) price per GB
    /// </summary>
    public double EgressPricePerGb { get; set; }
    
    /// <summary>
    /// Soft-deleted data storage price per GiB per month
    /// </summary>
    public double SoftDeletePricePerGbMonth { get; set; }
    
    /// <summary>
    /// Metadata storage price per GiB per month
    /// </summary>
    public double MetadataPricePerGbMonth { get; set; }
    
    /// <summary>
    /// Last time this pricing was updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Indicates if this is pay-as-you-go pricing
    /// </summary>
    public bool IsPayAsYouGo => AccessTier.HasValue;
    
    /// <summary>
    /// Indicates if this is provisioned pricing
    /// </summary>
    public bool IsProvisioned => ProvisionedTier.HasValue;
}
