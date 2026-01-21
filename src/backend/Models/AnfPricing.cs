namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Azure NetApp Files service levels
/// </summary>
public enum AnfServiceLevel
{
    Standard,   // 16 MiB/s per TiB
    Premium,    // 64 MiB/s per TiB
    Ultra,      // 128 MiB/s per TiB
    Flexible    // Decoupled capacity and throughput
}

/// <summary>
/// Azure NetApp Files meter-based pricing for a specific service level
/// </summary>
public class AnfMeterPricing
{
    /// <summary>
    /// Service level
    /// </summary>
    public AnfServiceLevel ServiceLevel { get; set; }
    
    /// <summary>
    /// Region name
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Capacity price per GiB per hour
    /// ANF is billed hourly based on provisioned capacity
    /// </summary>
    public double CapacityPricePerGibHour { get; set; }
    
    /// <summary>
    /// Capacity price per TiB per month (for reference/display)
    /// Calculated as: CapacityPricePerGibHour * 1024 * 730
    /// </summary>
    public double CapacityPricePerTibMonth => CapacityPricePerGibHour * 1024 * 730;
    
    // Flexible service level specific pricing
    
    /// <summary>
    /// Baseline throughput included for free (MiB/s)
    /// 128 MiB/s is provided free for every pool of any size
    /// </summary>
    public double FlexibleBaselineThroughputMiBps { get; set; } = 128;
    
    /// <summary>
    /// Additional throughput price per MiB/sec per hour (Flexible only)
    /// Charged for throughput beyond the baseline
    /// Maximum: 5 x 128 x pool size in TiB
    /// </summary>
    public double FlexibleThroughputPricePerMiBSecHour { get; set; }
    
    // Cool tier pricing (available for Standard, Premium, Ultra, and Flexible)
    
    /// <summary>
    /// Cool tier storage price per GiB per month
    /// Data tiered to cool storage (Azure Blob)
    /// </summary>
    public double CoolTierStoragePricePerGibMonth { get; set; }
    
    /// <summary>
    /// Cool tier data transfer price per GiB
    /// Network transfer between hot tier and cool tier
    /// Includes GET/PUT transaction markup
    /// </summary>
    public double CoolTierDataTransferPricePerGib { get; set; }
    
    /// <summary>
    /// Cool tier retrieval price per GiB
    /// Cost to retrieve data from cool tier back to hot tier
    /// </summary>
    public double CoolTierRetrievalPricePerGib { get; set; }
    
    // Snapshot pricing
    
    /// <summary>
    /// Snapshot storage price per GiB per hour
    /// Snapshots are differential and charged against parent volume quota
    /// Same rate as capacity pool
    /// </summary>
    public double SnapshotPricePerGibHour => CapacityPricePerGibHour;
    
    // Cross-region replication pricing
    
    /// <summary>
    /// Cross-region replication price per GiB per month (10-minute frequency)
    /// </summary>
    public double CrrPrice10MinPricePerGibMonth { get; set; }
    
    /// <summary>
    /// Cross-region replication price per GiB per month (hourly frequency)
    /// </summary>
    public double CrrPriceHourlyPricePerGibMonth { get; set; }
    
    /// <summary>
    /// Cross-region replication price per GiB per month (daily frequency)
    /// </summary>
    public double CrrPriceDailyPricePerGibMonth { get; set; }
    
    /// <summary>
    /// Cross-zone replication price per GiB per month
    /// No network transfer costs (same region, different AZ)
    /// </summary>
    public double CzrPricePerGibMonth { get; set; }
    
    /// <summary>
    /// Last time this pricing was updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Indicates if this is Flexible service level
    /// </summary>
    public bool IsFlexible => ServiceLevel == AnfServiceLevel.Flexible;
}
