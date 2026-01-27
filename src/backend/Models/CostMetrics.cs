namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents time-series cost data points for a specific component.
/// </summary>
public class CostMetrics
{
    /// <summary>
    /// Unique identifier for this metric
    /// </summary>
    public string MetricId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Timestamp for this cost data point
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Type of cost component (storage, transactions, egress, snapshots, backup)
    /// </summary>
    public string ComponentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Resource ID this metric is for
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Amount of the resource consumed (e.g., GB for storage, operation count for transactions)
    /// </summary>
    public double Quantity { get; set; }
    
    /// <summary>
    /// Unit price at time of measurement
    /// </summary>
    public double UnitPrice { get; set; }
    
    /// <summary>
    /// Total cost for this time period
    /// </summary>
    public double TotalCost { get; set; }
    
    /// <summary>
    /// Hourly cost rate (if applicable)
    /// </summary>
    public double HourlyRate { get; set; }
    
    /// <summary>
    /// Currency code
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Region for pricing
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Unit of measure (e.g., "GB", "per 10k", "GB/month")
    /// </summary>
    public string Unit { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this is actual billing data or estimated
    /// </summary>
    public bool IsEstimated { get; set; }
    
    /// <summary>
    /// Whether this cost component is forecasted vs historical
    /// </summary>
    public bool IsForecast { get; set; }
    
    /// <summary>
    /// Any additional metadata or tags
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
    
    /// <summary>
    /// Creates a historical cost metric
    /// </summary>
    public static CostMetrics Historical(
        DateTime timestamp,
        string componentType,
        string resourceId,
        double quantity,
        double unitPrice,
        string unit,
        string region,
        Dictionary<string, string>? metadata = null)
    {
        return new CostMetrics
        {
            Timestamp = timestamp,
            ComponentType = componentType,
            ResourceId = resourceId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Unit = unit,
            Region = region,
            Metadata = metadata,
            IsEstimated = false,
            IsForecast = false
        };
    }
    
    /// <summary>
    /// Creates an estimated cost metric (from metrics, not billing)
    /// </summary>
    public static CostMetrics Estimated(
        DateTime timestamp,
        string componentType,
        string resourceId,
        double quantity,
        double unitPrice,
        string unit,
        string region,
        Dictionary<string, string>? metadata = null)
    {
        return new CostMetrics
        {
            Timestamp = timestamp,
            ComponentType = componentType,
            ResourceId = resourceId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Unit = unit,
            Region = region,
            Metadata = metadata,
            IsEstimated = true,
            IsForecast = false
        };
    }
    
    /// <summary>
    /// Creates a forecasted cost metric
    /// </summary>
    public static CostMetrics Forecast(
        DateTime timestamp,
        string componentType,
        string resourceId,
        double quantity,
        double unitPrice,
        string unit,
        string region,
        Dictionary<string, string>? metadata = null)
    {
        return new CostMetrics
        {
            Timestamp = timestamp,
            ComponentType = componentType,
            ResourceId = resourceId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Unit = unit,
            Region = region,
            Metadata = metadata,
            IsEstimated = true,
            IsForecast = true
        };
    }
    
    /// <summary>
    /// Calculate total cost from quantity and unit price
    /// </summary>
    public void CalculateTotalCost()
    {
        TotalCost = Quantity * UnitPrice;
    }
    
    /// <summary>
    /// Get metadata value by key
    /// </summary>
    public string? GetMetadataValue(string key)
    {
        return Metadata?.GetValueOrDefault(key);
    }
    
    /// <summary>
    /// Set metadata value
    /// </summary>
    public void SetMetadataValue(string key, string value)
    {
        Metadata ??= new Dictionary<string, string>();
        Metadata[key] = value;
    }
}