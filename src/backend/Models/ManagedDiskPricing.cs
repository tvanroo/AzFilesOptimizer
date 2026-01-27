namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Managed Disk types
/// </summary>
public enum ManagedDiskType
{
    StandardHDD,
    StandardSSD,
    PremiumSSD,
    PremiumSSDv2,
    UltraDisk
}

/// <summary>
/// Managed Disk meter-based pricing for a specific disk type and SKU
/// </summary>
public class ManagedDiskMeterPricing
{
    /// <summary>
    /// Disk type
    /// </summary>
    public ManagedDiskType DiskType { get; set; }
    
    /// <summary>
    /// SKU name (e.g., P10, P20, P30, P40, E10, S10, etc.)
    /// </summary>
    public string SKU { get; set; } = string.Empty;
    
    /// <summary>
    /// Region name
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Redundancy type
    /// </summary>
    public StorageRedundancy Redundancy { get; set; }
    
    // Fixed tier pricing (Premium SSD, Standard SSD, Standard HDD)
    
    /// <summary>
    /// Disk size in GiB for this tier
    /// </summary>
    public int SizeGiB { get; set; }
    
    /// <summary>
    /// Fixed price per disk per month
    /// Premium SSD, Standard SSD, and Standard HDD use fixed monthly pricing per tier
    /// </summary>
    public double PricePerMonth { get; set; }
    
    /// <summary>
    /// Provisioned IOPS for this tier
    /// </summary>
    public int ProvisionedIOPS { get; set; }
    
    /// <summary>
    /// Provisioned throughput in MB/s for this tier
    /// </summary>
    public int ProvisionedThroughputMBps { get; set; }
    
    // Premium SSD v2 pricing (usage-based)
    
    /// <summary>
    /// Capacity price per GiB per month (Premium SSD v2)
    /// </summary>
    public double CapacityPricePerGibMonth { get; set; }
    
    /// <summary>
    /// IOPS price per IOPS per month (Premium SSD v2)
    /// 3,000 IOPS baseline is free
    /// </summary>
    public double IOPSPricePerMonth { get; set; }
    
    /// <summary>
    /// Baseline IOPS included for free (Premium SSD v2)
    /// </summary>
    public int BaselineIOPS { get; set; } = 3000;
    
    /// <summary>
    /// Throughput price per MiB/sec per month (Premium SSD v2)
    /// 125 MiB/s baseline is free
    /// </summary>
    public double ThroughputPricePerMiBSecMonth { get; set; }
    
    /// <summary>
    /// Baseline throughput included for free in MiB/s (Premium SSD v2)
    /// </summary>
    public int BaselineThroughputMiBps { get; set; } = 125;
    
    // Ultra Disk pricing (usage-based)
    
    /// <summary>
    /// Capacity price per GiB per month (Ultra Disk)
    /// </summary>
    public double UltraCapacityPricePerGibMonth { get; set; }
    
    /// <summary>
    /// IOPS price per IOPS per month (Ultra Disk)
    /// </summary>
    public double UltraIOPSPricePerMonth { get; set; }
    
    /// <summary>
    /// Throughput price per MiB/sec per month (Ultra Disk)
    /// </summary>
    public double UltraThroughputPricePerMiBSecMonth { get; set; }
    
    // Bursting pricing (Premium SSD P30+)
    
    /// <summary>
    /// On-demand bursting enablement fee per month (P30+)
    /// Charged when bursting is enabled
    /// </summary>
    public double BurstingEnablementFeePerMonth { get; set; }
    
    /// <summary>
    /// Bursting transaction fee per 10K IOs (P30+)
    /// Charged for additional IOPS during burst
    /// </summary>
    public double BurstingTransactionPricePer10K { get; set; }
    
    /// <summary>
    /// Maximum burst IOPS (P30+)
    /// </summary>
    public int MaxBurstIOPS { get; set; } = 30000;
    
    /// <summary>
    /// Maximum burst throughput in MB/s (P30+)
    /// </summary>
    public int MaxBurstThroughputMBps { get; set; } = 1000;
    
    // Snapshot pricing
    
    /// <summary>
    /// Snapshot price per GiB per month
    /// Charged based on used size, not provisioned size
    /// </summary>
    public double SnapshotPricePerGibMonth { get; set; }
    
    /// <summary>
    /// Shared disk additional cost per month
    /// Extra charge when disk is used as shared disk
    /// </summary>
    public double SharedDiskAdditionalCostPerMonth { get; set; }
    
    /// <summary>
    /// Last time this pricing was updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Indicates if this is Premium SSD v2
    /// </summary>
    public bool IsPremiumSSDv2 => DiskType == ManagedDiskType.PremiumSSDv2;
    
    /// <summary>
    /// Indicates if this is Ultra Disk
    /// </summary>
    public bool IsUltraDisk => DiskType == ManagedDiskType.UltraDisk;
    
    /// <summary>
    /// Indicates if bursting is supported (P30+)
    /// </summary>
    public bool SupportsBursting => DiskType == ManagedDiskType.PremiumSSD && 
                                    (SKU == "P30" || SKU == "P40" || SKU == "P50" || 
                                     SKU == "P60" || SKU == "P70" || SKU == "P80");
}
