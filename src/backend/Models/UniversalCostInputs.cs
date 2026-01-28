namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Universal cost calculation inputs applicable to all storage platforms
/// (Azure Files, ANF, Managed Disks). Fields can be null when not applicable
/// to a specific platform, allowing platform-specific formulas to use the
/// same unified input structure.
/// </summary>
public class UniversalCostInputs
{
    // ===== Resource Identification =====
    
    /// <summary>
    /// Azure Resource ID of the volume/disk
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name of the volume/disk
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;
    
    /// <summary>
    /// Resource type: AzureFiles, ANF, ManagedDisk
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Azure region (e.g., "eastus", "westeurope")
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    // ===== Capacity Metrics =====
    
    /// <summary>
    /// Total provisioned/allocated capacity in GiB
    /// Used by: ANF (provisioned), Azure Files Premium (provisioned), Managed Disks (disk size)
    /// </summary>
    public double? ProvisionedCapacityGiB { get; set; }
    
    /// <summary>
    /// Actually consumed/used capacity in GiB
    /// Used by: Azure Files (transaction-optimized, hot, cool), ANF (for monitoring)
    /// </summary>
    public double? ConsumedCapacityGiB { get; set; }
    
    /// <summary>
    /// Total snapshot capacity in GiB
    /// Used by: All platforms
    /// </summary>
    public double? SnapshotSizeGiB { get; set; }
    
    /// <summary>
    /// Backup/vault storage size in GiB
    /// Used by: ANF with backup vaults, Azure Files with backup
    /// </summary>
    public double? BackupSizeGiB { get; set; }
    
    // ===== Cool Tier Metrics (ANF and Azure Files) =====
    
    /// <summary>
    /// Data stored in cool tier in GiB
    /// Used by: ANF with cool access, Azure Files cool tier
    /// </summary>
    public double? CoolDataSizeGiB { get; set; }
    
    /// <summary>
    /// Data read from cool tier during the period in GiB
    /// Used by: ANF cool tier data retrieval
    /// </summary>
    public double? CoolDataReadGiB { get; set; }
    
    /// <summary>
    /// Data written to cool tier during the period in GiB
    /// Used by: ANF cool tier data tiering/archival
    /// </summary>
    public double? CoolDataWriteGiB { get; set; }
    
    /// <summary>
    /// Total cool tier data transfer (read + write) in GiB
    /// Used by: Cost calculations for cool tier movement charges
    /// Calculated as: CoolDataReadGiB + CoolDataWriteGiB
    /// </summary>
    public double CoolDataTransferGiB => (CoolDataReadGiB ?? 0) + (CoolDataWriteGiB ?? 0);
    
    // ===== Performance Metrics =====
    
    /// <summary>
    /// Provisioned or consumed IOPS
    /// Used by: Managed Disks (Premium SSD v2, Ultra), Azure Files Premium v2
    /// </summary>
    public long? IopsValue { get; set; }
    
    /// <summary>
    /// Provisioned or consumed throughput in MiB/s
    /// Used by: Managed Disks (Premium SSD v2, Ultra), Azure Files Premium v2, ANF Flexible
    /// </summary>
    public double? ThroughputMiBps { get; set; }
    
    // ===== Transaction Metrics =====
    
    /// <summary>
    /// Total transactions during the billing period
    /// Used by: Azure Files (transaction-optimized, hot, cool tiers)
    /// </summary>
    public long? TransactionsTotal { get; set; }
    
    /// <summary>
    /// Read operations count
    /// Used by: Azure Files detailed costing
    /// </summary>
    public long? TransactionsRead { get; set; }
    
    /// <summary>
    /// Write operations count
    /// Used by: Azure Files detailed costing
    /// </summary>
    public long? TransactionsWrite { get; set; }
    
    /// <summary>
    /// List/enumerate operations count
    /// Used by: Azure Files detailed costing
    /// </summary>
    public long? TransactionsList { get; set; }
    
    // ===== Network Transfer =====
    
    /// <summary>
    /// Data egress/outbound transfer in GiB
    /// Used by: All platforms (typically free up to 100GB, then charged)
    /// </summary>
    public double? EgressGiB { get; set; }
    
    // ===== Platform-Specific Configuration =====
    
    /// <summary>
    /// Storage tier/service level
    /// Azure Files: "Hot", "Cool", "TransactionOptimized", "Premium"
    /// ANF: "Standard", "Premium", "Ultra", "Flexible"
    /// Managed Disks: "Premium SSD", "Standard SSD", "Standard HDD", "Premium SSD v2", "Ultra Disk"
    /// </summary>
    public string? StorageTier { get; set; }
    
    /// <summary>
    /// Redundancy type (for Azure Files and Managed Disks)
    /// Values: "LRS", "ZRS", "GRS", "GZRS"
    /// </summary>
    public string? Redundancy { get; set; }
    
    /// <summary>
    /// Whether this is a provisioned model (vs consumption-based)
    /// Azure Files: Premium (v1 or v2) = true, Standard tiers = false
    /// ANF: Always true (capacity pool provisioned)
    /// Managed Disks: Always true (fixed disk sizes)
    /// </summary>
    public bool? IsProvisioned { get; set; }
    
    /// <summary>
    /// Whether cool access/tiering is enabled
    /// Used by: ANF, Azure Files
    /// </summary>
    public bool? CoolAccessEnabled { get; set; }
    
    /// <summary>
    /// Whether this is a large volume (>100 TiB)
    /// Used by: ANF large volumes
    /// </summary>
    public bool? IsLargeVolume { get; set; }
    
    // ===== Metadata =====
    
    /// <summary>
    /// When these metrics were collected
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Number of days this data represents (for monthly projections)
    /// </summary>
    public int? MetricsPeriodDays { get; set; }
    
    /// <summary>
    /// Additional platform-specific properties that don't fit the universal model
    /// </summary>
    public Dictionary<string, object>? ExtendedProperties { get; set; }
    
    // ===== Convenience Methods =====
    
    /// <summary>
    /// Get the effective capacity to use for billing (provisioned or consumed, whichever applies)
    /// </summary>
    public double GetBillableCapacityGiB()
    {
        // For provisioned models, use provisioned capacity
        if (IsProvisioned == true && ProvisionedCapacityGiB.HasValue)
            return ProvisionedCapacityGiB.Value;
        
        // For consumption models, use consumed capacity
        if (ConsumedCapacityGiB.HasValue)
            return ConsumedCapacityGiB.Value;
        
        // Fallback to provisioned if available
        return ProvisionedCapacityGiB ?? 0;
    }
    
    /// <summary>
    /// Get hot tier data size (total - cool)
    /// </summary>
    public double GetHotDataSizeGiB()
    {
        var total = GetBillableCapacityGiB();
        var cool = CoolDataSizeGiB ?? 0;
        return Math.Max(0, total - cool);
    }
    
    /// <summary>
    /// Validate that required fields are present for the given resource type
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(ResourceId))
            errors.Add("ResourceId is required");
        
        if (string.IsNullOrWhiteSpace(ResourceType))
            errors.Add("ResourceType is required");
        
        if (string.IsNullOrWhiteSpace(Region))
            errors.Add("Region is required");
        
        // Type-specific validation
        switch (ResourceType?.ToUpperInvariant())
        {
            case "ANF":
            case "AZUREFILE":
            case "AZUREFILES":
                if (!ProvisionedCapacityGiB.HasValue && !ConsumedCapacityGiB.HasValue)
                    errors.Add("Either ProvisionedCapacityGiB or ConsumedCapacityGiB is required");
                break;
            
            case "MANAGEDDISK":
                if (!ProvisionedCapacityGiB.HasValue)
                    errors.Add("ProvisionedCapacityGiB is required for Managed Disks");
                break;
        }
        
        return errors;
    }
}
