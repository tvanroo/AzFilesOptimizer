namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents a billable component of storage cost (e.g., storage capacity, transactions, egress, snapshots).
/// </summary>
public class StorageCostComponent
{
    public string ComponentId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Type of cost component (storage, transactions, egress, snapshots, backup)
    /// </summary>
    public string ComponentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Azure region for pricing (e.g., "eastus", "westeurope")
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Unit price from Azure pricing API (e.g., 0.06 per GB/month)
    /// </summary>
    public double UnitPrice { get; set; }
    
    /// <summary>
    /// Unit of measure (e.g., "GB/month", "per 10k transactions", "GB", "snapshot GB")
    /// </summary>
    public string Unit { get; set; } = string.Empty;
    
    /// <summary>
    /// Quantity consumed during the period
    /// </summary>
    public double Quantity { get; set; }
    
    /// <summary>
    /// Total cost for this component over the period
    /// </summary>
    public double CostForPeriod { get; set; }
    
    /// <summary>
    /// Currency code (e.g., "USD")
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Name of the pricing tier or SKU
    /// </summary>
    public string? SkuName { get; set; }
    
    /// <summary>
    /// Start timestamp for this cost calculation
    /// </summary>
    public DateTime PeriodStart { get; set; }
    
    /// <summary>
    /// End timestamp for this cost calculation
    /// </summary>
    public DateTime PeriodEnd { get; set; }
    
    /// <summary>
    /// Whether this cost is estimated or actual (from billing)
    /// </summary>
    public bool IsEstimated { get; set; }
    
    /// <summary>
    /// Any notes or assumptions about this cost calculation
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Resource ID this cost is attributed to
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Helper method to calculate cost from quantity and unit price
    /// </summary>
    public void CalculateCost()
    {
        CostForPeriod = Quantity * UnitPrice;
    }
    
    /// <summary>
    /// Creates a storage cost component for capacity
    /// </summary>
    public static StorageCostComponent ForCapacity(
        string resourceId, 
        string region, 
        double gigabytes, 
        double pricePerGigabyte, 
        DateTime periodStart, 
        DateTime periodEnd,
        string? skuName = null)
    {
        return new StorageCostComponent
        {
            ComponentType = "storage",
            Region = region,
            UnitPrice = pricePerGigabyte,
            Unit = "GB/month",
            Quantity = gigabytes,
            ResourceId = resourceId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SkuName = skuName,
            IsEstimated = true
        };
    }
    
    /// <summary>
    /// Creates a storage cost component for transactions
    /// </summary>
    public static StorageCostComponent ForTransactions(
        string resourceId,
        string region,
        double transactionCountTensOfThousands,
        double pricePerTenThousand,
        DateTime periodStart,
        DateTime periodEnd)
    {
        return new StorageCostComponent
        {
            ComponentType = "transactions",
            Region = region,
            UnitPrice = pricePerTenThousand,
            Unit = "per 10k transactions",
            Quantity = transactionCountTensOfThousands,
            ResourceId = resourceId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsEstimated = true
        };
    }
    
    /// <summary>
    /// Creates a storage cost component for egress
    /// </summary>
    public static StorageCostComponent ForEgress(
        string resourceId,
        string region,
        double gigabytes,
        double pricePerGigabyte,
        DateTime periodStart,
        DateTime periodEnd)
    {
        return new StorageCostComponent
        {
            ComponentType = "egress",
            Region = region,
            UnitPrice = pricePerGigabyte,
            Unit = "GB",
            Quantity = gigabytes,
            ResourceId = resourceId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsEstimated = true
        };
    }
    
    /// <summary>
    /// Creates a storage cost component for snapshots
    /// </summary>
    public static StorageCostComponent ForSnapshots(
        string resourceId,
        string region,
        double gigabytes,
        double pricePerGigabyte,
        DateTime periodStart,
        DateTime periodEnd)
    {
        return new StorageCostComponent
        {
            ComponentType = "snapshots",
            Region = region,
            UnitPrice = pricePerGigabyte,
            Unit = "GB/month",
            Quantity = gigabytes,
            ResourceId = resourceId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsEstimated = true
        };
    }
    
    /// <summary>
    /// Creates a storage cost component for backup
    /// </summary>
    public static StorageCostComponent ForBackup(
        string resourceId,
        string region,
        double gigabytes,
        double pricePerGigabyte,
        DateTime periodStart,
        DateTime periodEnd)
    {
        return new StorageCostComponent
        {
            ComponentType = "backup",
            Region = region,
            UnitPrice = pricePerGigabyte,
            Unit = "GB/month",
            Quantity = gigabytes,
            ResourceId = resourceId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsEstimated = true
        };
    }
}