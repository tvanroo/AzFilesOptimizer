namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Aggregates cost information for a single discovered volume (Azure Files, ANF, or Managed Disk).
/// </summary>
public class VolumeCostAnalysis
{
    /// <summary>
    /// Unique identifier for this cost analysis
    /// </summary>
    public string AnalysisId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// ID of the volume this analysis is for
    /// </summary>
    public string VolumeId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of resource: AzureFile, ANF, or ManagedDisk
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name of the volume
    /// </summary>
    public string VolumeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Resource ID in Azure
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Azure region
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    /// <summary>
    /// Storage account or pool name if applicable
    /// </summary>
    public string? StorageAccountOrPoolName { get; set; }
    
    /// <summary>
    /// Discovery job ID that discovered this volume
    /// </summary>
    public string JobId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total cost per day for this volume (averaged over the period)
    /// </summary>
    public double TotalCostPerDay { get; set; }
    
    /// <summary>
    /// Total cost for the entire analysis period
    /// </summary>
    public double TotalCostForPeriod { get; set; }
    
    /// <summary>
    /// Breakdown of costs by component type
    /// </summary>
    public List<StorageCostComponent> CostComponents { get; set; } = new();
    
    /// <summary>
    /// Start timestamp for the analysis period
    /// </summary>
    public DateTime PeriodStart { get; set; }
    
    /// <summary>
    /// End timestamp for the analysis period
    /// </summary>
    public DateTime PeriodEnd { get; set; }
    
    /// <summary>
    /// Number of days in the analysis period
    /// </summary>
    public int PeriodDays => (PeriodEnd - PeriodStart).Days;
    
    /// <summary>
    /// Historical 30-day costs (optional detailed breakdown)
    /// </summary>
    public List<CostMetrics> HistoricalCosts { get; set; } = new();
    
    /// <summary>
    /// Projected 30-day costs forecast
    /// </summary>
    public CostForecastResult? Forecast { get; set; }
    
    /// <summary>
    /// Capacity information in GB
    /// </summary>
    public double CapacityGigabytes { get; set; }
    
    /// <summary>
    /// Used capacity in GB (if available)
    /// </summary>
    public double UsedGigabytes { get; set; }
    
    /// <summary>
    /// Average transactions per day (if available)
    /// </summary>
    public double? AverageTransactionsPerDay { get; set; }
    
    /// <summary>
    /// Average egress per day in GB (if available)
    /// </summary>
    public double? AverageEgressPerDayGb { get; set; }
    
    /// <summary>
    /// Snapshot count at time of analysis
    /// </summary>
    public int? SnapshotCount { get; set; }
    
    /// <summary>
    /// Total snapshot size in GB (if available)
    /// </summary>
    public double? TotalSnapshotSizeGb { get; set; }
    
    /// <summary>
    /// Whether backup is configured
    /// </summary>
    public bool BackupConfigured { get; set; }
    
    /// <summary>
    /// Calculated cost breakdown by component type
    /// </summary>
    public Dictionary<string, double> CostBreakdown => CostComponents
        .GroupBy(c => c.ComponentType)
        .ToDictionary(g => g.Key, g => g.Sum(c => c.CostForPeriod));
    
    /// <summary>
    /// Percentage of total cost that is storage capacity
    /// </summary>
    public double StorageCostPercentage => TotalCostForPeriod > 0 
        ? (CostBreakdown.GetValueOrDefault("storage", 0) / TotalCostForPeriod) * 100 
        : 0;
    
    /// <summary>
    /// Percentage of total cost that is transactions
    /// </summary>
    public double TransactionCostPercentage => TotalCostForPeriod > 0 
        ? (CostBreakdown.GetValueOrDefault("transactions", 0) / TotalCostForPeriod) * 100 
        : 0;
    
    /// <summary>
    /// Percentage of total cost that is egress
    /// </summary>
    public double EgressCostPercentage => TotalCostForPeriod > 0 
        ? (CostBreakdown.GetValueOrDefault("egress", 0) / TotalCostForPeriod) * 100 
        : 0;
    
    /// <summary>
    /// Percentage of total cost that is snapshots
    /// </summary>
    public double SnapshotCostPercentage => TotalCostForPeriod > 0 
        ? (CostBreakdown.GetValueOrDefault("snapshots", 0) / TotalCostForPeriod) * 100 
        : 0;
    
    /// <summary>
    /// Percentage of total cost that is backup
    /// </summary>
    public double BackupCostPercentage => TotalCostForPeriod > 0 
        ? (CostBreakdown.GetValueOrDefault("backup", 0) / TotalCostForPeriod) * 100 
        : 0;
    
    /// <summary>
    /// Timestamp when this analysis was created
    /// </summary>
    public DateTime AnalysisTimestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Any notes about this analysis
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Any warnings or alerts about this volume's cost
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// Recalculate totals based on cost components
    /// </summary>
    public void RecalculateTotals()
    {
        TotalCostForPeriod = CostComponents.Sum(c => c.CostForPeriod);
        var days = PeriodDays;
        TotalCostPerDay = days > 0 ? TotalCostForPeriod / days : 0;
    }
    
    /// <summary>
    /// Add a cost component to this analysis
    /// </summary>
    public void AddCostComponent(StorageCostComponent component)
    {
        component.CalculateCost();
        CostComponents.Add(component);
        RecalculateTotals();
    }
    
    /// <summary>
    /// Get cost components by type
    /// </summary>
    public List<StorageCostComponent> GetComponentsByType(string componentType)
    {
        return CostComponents.Where(c => 
            c.ComponentType.Equals(componentType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    
    /// <summary>
    /// Check if this volume is in a cost warning state
    /// </summary>
    public bool HasCostWarnings => Warnings.Any(w => w.Contains("cost", StringComparison.OrdinalIgnoreCase));
    
    /// <summary>
    /// Get a summary of the cost analysis
    /// </summary>
    public string GetSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Volume: {VolumeName} ({ResourceType})");
        sb.AppendLine($"Total Cost (30 days): ${TotalCostForPeriod:F2}");
        sb.AppendLine($"Daily Average: ${TotalCostPerDay:F2}");
        sb.AppendLine($"Cost Breakdown:");
        foreach (var kvp in CostBreakdown.OrderByDescending(x => x.Value))
        {
            sb.AppendLine($"  - {kvp.Key}: ${kvp.Value:F2} ({(kvp.Value / TotalCostForPeriod * 100):F1}%)");
        }
        return sb.ToString();
    }
}