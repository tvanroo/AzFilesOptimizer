namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents a single meter's cost entry from Azure Cost Management API
/// </summary>
public class MeterCostEntry
{
    /// <summary>
    /// The meter name (e.g., "Standard Data Transfer Out", "Read Operations")
    /// </summary>
    public string Meter { get; set; } = string.Empty;
    
    /// <summary>
    /// The meter subcategory (e.g., "Files", "Azure NetApp Files", "Premium SSD Managed Disks")
    /// </summary>
    public string MeterSubcategory { get; set; } = string.Empty;
    
    /// <summary>
    /// The meter category (e.g., "Storage")
    /// </summary>
    public string? MeterCategory { get; set; }
    
    /// <summary>
    /// Cost in USD for this meter
    /// </summary>
    public double CostUSD { get; set; }
    
    /// <summary>
    /// Cost in local currency
    /// </summary>
    public double Cost { get; set; }
    
    /// <summary>
    /// Currency code (e.g., "USD")
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Usage date for this cost entry
    /// </summary>
    public DateTime UsageDate { get; set; }
    
    /// <summary>
    /// Resource ID this meter is associated with
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Unit of measure (if available from API)
    /// </summary>
    public string? Unit { get; set; }
    
    /// <summary>
    /// Quantity (if available from API)
    /// </summary>
    public double? Quantity { get; set; }
    
    /// <summary>
    /// Categorized component type inferred from meter name
    /// (e.g., "storage", "transactions", "egress", "operations")
    /// </summary>
    public string ComponentType { get; set; } = "unknown";
    
    /// <summary>
    /// Additional notes about this meter
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Metadata inferred from billing/meter data for a volume
/// </summary>
public class VolumeMetadataFromBilling
{
    /// <summary>
    /// Redundancy type inferred from meter names (LRS, GRS, ZRS, GZRS, RA-GRS, etc.)
    /// </summary>
    public string? RedundancyType { get; set; }
    
    /// <summary>
    /// Average read operations per day (from transaction meters)
    /// </summary>
    public double? AverageReadOperationsPerDay { get; set; }
    
    /// <summary>
    /// Average write operations per day (from transaction meters)
    /// </summary>
    public double? AverageWriteOperationsPerDay { get; set; }
    
    /// <summary>
    /// Average list operations per day
    /// </summary>
    public double? AverageListOperationsPerDay { get; set; }
    
    /// <summary>
    /// Average data transfer (egress) in GB per day
    /// </summary>
    public double? AverageDataTransferGbPerDay { get; set; }
    
    /// <summary>
    /// Average data ingress in GB per day
    /// </summary>
    public double? AverageDataIngressGbPerDay { get; set; }
    
    /// <summary>
    /// Primary meter category from billing data
    /// </summary>
    public string? PrimaryMeterCategory { get; set; }
    
    /// <summary>
    /// Storage tier inferred from meters (e.g., "Premium", "Standard", "Hot", "Cool")
    /// </summary>
    public string? StorageTier { get; set; }
    
    /// <summary>
    /// Service level for ANF (e.g., "Ultra", "Premium", "Standard")
    /// </summary>
    public string? ServiceLevel { get; set; }
    
    /// <summary>
    /// Disk SKU/type for managed disks (e.g., "Premium SSD", "Standard HDD")
    /// </summary>
    public string? DiskType { get; set; }
    
    /// <summary>
    /// Protocol types detected from meters (for ANF: NFSv3, NFSv4.1, SMB)
    /// </summary>
    public List<string>? DetectedProtocols { get; set; }
    
    /// <summary>
    /// Whether GRS/GZRS replication costs are present
    /// </summary>
    public bool HasGeoReplication { get; set; }
    
    /// <summary>
    /// Total number of distinct meters found for this resource
    /// </summary>
    public int TotalMeterCount { get; set; }
    
    /// <summary>
    /// Date range for the billing data used to infer this metadata
    /// </summary>
    public DateTime? MetadataFromDate { get; set; }
    
    /// <summary>
    /// Date range for the billing data used to infer this metadata
    /// </summary>
    public DateTime? MetadataToDate { get; set; }
    
    /// <summary>
    /// Confidence level in the metadata (0-100)
    /// Based on number of data points and consistency
    /// </summary>
    public double ConfidenceScore { get; set; }
    
    /// <summary>
    /// Additional metadata key-value pairs specific to resource type
    /// </summary>
    public Dictionary<string, string>? AdditionalMetadata { get; set; }
}

/// <summary>
/// Resource-type-specific meter patterns for intelligent categorization
/// </summary>
public static class MeterPatterns
{
    /// <summary>
    /// Storage Account / Azure Files meter patterns
    /// </summary>
    public static class StorageAccount
    {
        public static readonly string[] StorageMeters = new[]
        {
            "Data Stored",
            "LRS Data Stored",
            "GRS Data Stored",
            "ZRS Data Stored",
            "GZRS Data Stored",
            "RA-GRS Data Stored",
            "Hot Data Stored",
            "Cool Data Stored",
            "Archive Data Stored"
        };
        
        public static readonly string[] TransactionMeters = new[]
        {
            "Read Operations",
            "Write Operations",
            "List Operations",
            "Class 1 Operations",
            "Class 2 Operations",
            "All Other Operations",
            "Create Container Operations"
        };
        
        public static readonly string[] EgressMeters = new[]
        {
            "Data Transfer Out",
            "Standard Data Transfer Out",
            "Egress"
        };
        
        public static readonly string[] IngressMeters = new[]
        {
            "Data Transfer In",
            "Standard Data Transfer In"
        };
        
        public static readonly string[] ReplicationMeters = new[]
        {
            "GRS",
            "RA-GRS",
            "GZRS",
            "RA-GZRS",
            "Write Operations (Tables/Tables)"
        };
    }
    
    /// <summary>
    /// Azure NetApp Files meter patterns
    /// </summary>
    public static class AnfVolume
    {
        public static readonly string[] CapacityMeters = new[]
        {
            "Standard Capacity",
            "Premium Capacity",
            "Ultra Capacity",
            "Provisioned Capacity"
        };
        
        public static readonly string[] SnapshotMeters = new[]
        {
            "Snapshot",
            "Snapshot Storage"
        };
        
        public static readonly string[] BackupMeters = new[]
        {
            "Backup",
            "Backup Storage"
        };
        
        public static readonly string[] ProtocolMeters = new[]
        {
            "NFSv3",
            "NFSv4.1",
            "SMB",
            "Dual-Protocol"
        };
    }
    
    /// <summary>
    /// Managed Disk meter patterns
    /// </summary>
    public static class ManagedDisk
    {
        public static readonly string[] DiskTypeMeters = new[]
        {
            "Premium SSD",
            "Standard SSD",
            "Standard HDD",
            "Ultra Disk"
        };
        
        public static readonly string[] CapacityMeters = new[]
        {
            "Disk",
            "Provisioned",
            "Attached"
        };
        
        public static readonly string[] SnapshotMeters = new[]
        {
            "Snapshot"
        };
        
        public static readonly string[] OperationMeters = new[]
        {
            "Disk Operations",
            "IOPS"
        };
    }
}
