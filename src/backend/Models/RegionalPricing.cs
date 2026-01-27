namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents regional pricing data for storage resources.
/// </summary>
public class RegionalPricing
{
    /// <summary>
    /// Region name (e.g., "eastus", "westeurope")
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Currency code (e.g., "USD")
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Standard tier storage price per GB/month
    /// </summary>
    public double StandardStoragePricePerGb { get; set; }
    
    /// <summary>
    /// Premium tier storage price per GB/month
    /// </summary>
    public double PremiumStoragePricePerGb { get; set; }
    
    /// <summary>
    /// Ultra tier storage price per GB/month (ANF)
    /// </summary>
    public double UltraStoragePricePerGb { get; set; }
    
    /// <summary>
    /// Price per 10,000 transactions (standard tier)
    /// </summary>
    public double TransactionPricePer10kStandard { get; set; }
    
    /// <summary>
    /// Price per 10,000 transactions (premium tier)
    /// </summary>
    public double TransactionPricePer10kPremium { get; set; }
    
    /// <summary>
    /// Snapshot price per GB/month
    /// </summary>
    public double SnapshotPricePerGb { get; set; }
    
    /// <summary>
    /// Backup price per GB/month
    /// </summary>
    public double BackupPricePerGb { get; set; }
    
    /// <summary>
    /// Data egress price per GB (Internet)
    /// </summary>
    public double InternetEgressPricePerGb { get; set; }
    
    /// <summary>
    /// Data egress price per GB (intra-region)
    /// </summary>
    public double IntraRegionEgressPricePerGb { get; set; }
    
    /// <summary>
    /// Data egress price per GB (cross-region)
    /// </summary>
    public double CrossRegionEgressPricePerGb { get; set; }
    
    /// <summary>
    /// Timestamp when this pricing was fetched
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Cache expiry timestamp
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(1);
    
    /// <summary>
    /// Check if pricing cache is expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    
    /// <summary>
    /// Default pricing for US regions (fallback values)
    /// </summary>
    public static RegionalPricing DefaultForUs()
    {
        return new RegionalPricing
        {
            Region = "default",
            StandardStoragePricePerGb = 0.06,
            PremiumStoragePricePerGb = 0.20,
            UltraStoragePricePerGb = 0.50,
            TransactionPricePer10kStandard = 0.004,
            TransactionPricePer10kPremium = 0.005,
            SnapshotPricePerGb = 0.05,
            BackupPricePerGb = 0.05,
            InternetEgressPricePerGb = 0.087,
            IntraRegionEgressPricePerGb = 0.01,
            CrossRegionEgressPricePerGb = 0.02,
            LastUpdated = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(6)
        };
    }
}